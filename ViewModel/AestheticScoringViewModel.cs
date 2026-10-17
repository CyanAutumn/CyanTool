using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel;

public sealed record AestheticScoreRow(string ImagePath, string Label, double? Score, double? Confidence,
    string OutputPath, string Error, IReadOnlyDictionary<string, double>? Probabilities, double? Percentile = null)
{
    public string FileName => Path.GetFileName(ImagePath);
}

public sealed class AestheticScoringViewModel : INotifyPropertyChanged
{
    private string _inputPath = "";
    private string _outputPath = "";
    private string _modelDirectory = AestheticModelStore.DefaultDirectory;
    private bool _recursive = true;
    private bool _useGpu = true;
    private bool _useProxy;
    private bool _isBusy;
    private double _progress;
    private string _status = "准备就绪";
    private string _log = "";
    private readonly Queue<string> _logLines = new();
    private CancellationTokenSource? _cancellation;
    private AestheticScoreRow? _selectedResult;

    public string InputPath
    {
        get => _inputPath;
        set
        {
            string previous = _inputPath;
            if (Set(ref _inputPath, value))
                OutputPath = ToolOutputDirectoryDefaults.RefreshForInputChange(OutputPath, previous, value, "美学评分");
        }
    }
    public string OutputPath { get => _outputPath; set => Set(ref _outputPath, value); }
    public string ModelDirectory
    {
        get => _modelDirectory;
        set { if (Set(ref _modelDirectory, value)) Changed(nameof(ModelStatus)); }
    }
    public bool Recursive { get => _recursive; set => Set(ref _recursive, value); }
    public bool UseGpu { get => _useGpu; set => Set(ref _useGpu, value); }
    public bool UseProxy { get => _useProxy; set => Set(ref _useProxy, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) Changed(nameof(IsIdle)); }
    }
    public bool IsIdle => !IsBusy;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Log { get => _log; private set => Set(ref _log, value); }
    public string ModelStatus => AestheticModelStore.IsPresent(ModelDirectory) ? "模型已就绪" : "请先下载模型或选择已有模型目录。";
    public ObservableCollection<AestheticScoreRow> Results { get; } = new();
    public AestheticScoreRow? SelectedResult { get => _selectedResult; set => Set(ref _selectedResult, value); }
    public ICommand SelectImageCommand { get; }
    public ICommand SelectFolderCommand { get; }
    public ICommand SelectOutputCommand { get; }
    public ICommand SelectModelCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand OpenImageCommand { get; }
    public ICommand OpenOutputCommand { get; }

    public AestheticScoringViewModel()
    {
        SelectImageCommand = new RelayCommand(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.Translate("选择图片"),
                Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff"
            };
            if (dialog.ShowDialog() == true) InputPath = dialog.FileName;
        }, () => IsIdle);
        SelectFolderCommand = new RelayCommand(() => SelectDirectory("选择输入路径", p => InputPath = p), () => IsIdle);
        SelectOutputCommand = new RelayCommand(() => SelectDirectory("选择输出路径", p => OutputPath = p), () => IsIdle);
        SelectModelCommand = new RelayCommand(() => SelectDirectory("选择模型目录", p => ModelDirectory = p), () => IsIdle);
        DownloadCommand = new RelayCommand(async () => await DownloadAsync(), () => IsIdle && !string.IsNullOrWhiteSpace(ModelDirectory));
        StartCommand = new RelayCommand(async () => await RunAsync(), () => IsIdle && !string.IsNullOrWhiteSpace(InputPath));
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsBusy);
        ExportCommand = new RelayCommand(async () => await ExportAsync(), () => IsIdle && Results.Count > 0);
        OpenImageCommand = new RelayCommand(() => OpenPath(SelectedResult!.ImagePath), () => SelectedResult != null);
        OpenOutputCommand = new RelayCommand(() => OpenPath(OutputPath), () => Directory.Exists(OutputPath));
    }

    private static void SelectDirectory(string title, Action<string> set)
    {
        using var dialog = new CommonOpenFileDialog { IsFolderPicker = true, Title = LocalizationManager.Translate(title) };
        if (dialog.ShowDialog() == CommonFileDialogResult.Ok) set(dialog.FileName);
    }

    public async Task DownloadAsync()
    {
        if (IsBusy) return;
        Begin("正在下载并校验模型…");
        string directory = ModelDirectory;
        bool useProxy = UseProxy;
        var log = new Progress<string>(AppendLog);
        ManagedMihomoService.ManagedMihomoLease? lease = null;
        try
        {
            using var handler = new HttpClientHandler();
            if (useProxy)
            {
                var node = ProxySettingsStore.Shared.GetSelectedNodeOrLocal();
                if (node.UsesManagedCore)
                {
                    lease = await ManagedMihomoService.Shared.AcquireAsync(node, _cancellation!.Token, log);
                    node = lease.TransportNode;
                }
                handler.Proxy = node.CreateWebProxy();
                handler.UseProxy = true;
            }
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            await new AestheticModelStore().DownloadAsync(directory,
                new Progress<double>(p => Progress = p), _cancellation!.Token, client);
            Status = "模型已就绪";
        }
        catch (OperationCanceledException) { Status = "已取消"; }
        catch (Exception ex) { Failed(ex); }
        finally
        {
            try { if (lease != null) await lease.DisposeAsync(); }
            finally { End(); Changed(nameof(ModelStatus)); }
        }
    }

    public async Task RunAsync()
    {
        if (IsBusy) return;
        if (await MaterialClassificationSession.TryRunAsync(this, RunAsync)) return;
        string input = InputPath.Trim();
        OutputPath = ToolOutputDirectoryDefaults.FillWhenEmpty(OutputPath, input, "美学评分");
        string output = OutputPath.Trim();
        string modelDirectory = ModelDirectory;
        bool recursive = Recursive, useGpu = UseGpu;
        Begin("正在校验模型…");
        Results.Clear();
        var progress = new Progress<double>(p => Progress = p);
        var log = new Progress<string>(AppendLog);
        var result = new Progress<AestheticScoreRow>(row => Results.Add(row));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation!.Token, MaterialClassificationSession.Current?.Token ?? CancellationToken.None);
        CancellationToken token = linkedCancellation.Token;
        try
        {
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException(LocalizationManager.Translate("请选择输出路径。"));
            AestheticFileOrganizer.ValidateOutput(input, output);
            if (!AestheticModelStore.IsPresent(modelDirectory))
                throw new InvalidOperationException(LocalizationManager.Translate("请先下载模型或选择已有模型目录。"));
            await AestheticModelStore.VerifyAsync(modelDirectory, token);
            Status = "正在评分…";
            var summary = await Task.Run(() =>
            {
                var images = AestheticFileOrganizer.FindImages(input, recursive, output, token);
                if (images.Count == 0) throw new InvalidOperationException(LocalizationManager.Translate("没有找到支持的图片。"));
                string root = File.Exists(input) ? Path.GetDirectoryName(Path.GetFullPath(input))! : Path.GetFullPath(input);
                using var processor = new AestheticScoringProcessor(modelDirectory, useGpu, s => ((IProgress<string>)log).Report(s));
                foreach (string label in AestheticScoringProcessor.Labels)
                {
                    token.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.Combine(output, label));
                }
                int success = 0, failures = 0;
                foreach (string image in images)
                {
                    token.ThrowIfCancellationRequested();
                    AestheticPrediction? prediction = null;
                    try
                    {
                        prediction = processor.Score(image, token);
                        string destination = AestheticFileOrganizer.CopyClassified(image, root, output, prediction.Label, token);
                        ((IProgress<AestheticScoreRow>)result).Report(new(image, prediction.Label, prediction.Score,
                            prediction.Confidence, destination, "", prediction.Probabilities, prediction.Percentile));
                        success++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        failures++;
                        ((IProgress<AestheticScoreRow>)result).Report(new(image, prediction?.Label ?? "", prediction?.Score,
                            prediction?.Confidence, "", ex.Message, prediction?.Probabilities, prediction?.Percentile));
                        ((IProgress<string>)log).Report(Path.GetFileName(image) + ": " + ex.Message);
                    }
                    ((IProgress<double>)progress).Report((success + failures) * 100d / images.Count);
                }
                return (success, failures);
            }, token);
            if (summary.failures == 0) MaterialClassificationSession.MarkSuccessful();
            Progress = 100;
            Status = summary.failures == 0 ? "评分完成" : "评分完成，部分图片失败。";
            AppendLog(LocalizationManager.Format("成功 {0} 张，失败 {1} 张。", summary.success, summary.failures));
        }
        catch (OperationCanceledException) { Status = "已取消"; }
        catch (Exception ex) { Failed(ex); }
        finally { End(); }
    }

    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationManager.Translate("导出评分结果"), Filter = "JSON|*.json",
            FileName = "aesthetic-scores.json", DefaultExt = ".json"
        };
        if (dialog.ShowDialog() != true) return;
        string temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Begin("正在导出…");
        try
        {
            var report = new
            {
                model = "deepghs/anime_aesthetic", variant = AestheticModelStore.ModelName,
                revision = AestheticModelStore.Revision, score_range = new[] { 0, 6 },
                implementation = "deepghs/imgutils", implementation_revision = AestheticScoringProcessor.UpstreamRevision,
                percentile_range = new[] { 0, 1 }, label_method = "percentile_to_label",
                results = Results.ToArray()
            };
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                await JsonSerializer.SerializeAsync(file, report, new JsonSerializerOptions { WriteIndented = true }, _cancellation!.Token);
            _cancellation!.Token.ThrowIfCancellationRequested();
            File.Move(temporary, dialog.FileName, overwrite: true);
            Status = "导出完成";
        }
        catch (OperationCanceledException) { Status = "已取消"; }
        catch (Exception ex) { Failed(ex); }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            End();
        }
    }

    private void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Failed(ex); }
    }

    private void Begin(string status)
    {
        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        Status = status;
        _logLines.Clear();
        Log = "";
    }
    private void End()
    {
        _cancellation?.Dispose();
        _cancellation = null;
        IsBusy = false;
        CommandManager.InvalidateRequerySuggested();
    }
    private void Failed(Exception ex) { Status = "操作失败，请查看日志。"; AppendLog(ex.Message); }
    private void AppendLog(string message)
    {
        _logLines.Enqueue($"{DateTime.Now:HH:mm:ss} {message}");
        while (_logLines.Count > 300) _logLines.Dequeue();
        Log = string.Join(Environment.NewLine, _logLines);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Changed(property);
        CommandManager.InvalidateRequerySuggested();
        return true;
    }
}
