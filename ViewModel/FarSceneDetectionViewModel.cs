using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel;

public sealed class FarSceneDetectionViewModel : INotifyPropertyChanged
{
    private string _materialDirectory = "";
    private double _thresholdPercent = 25;
    private MaterialClassificationSession? _activeSession;
    private readonly string _diagnosticLogPath;
    public event Action<string>? OperationFailed;
    public string MaterialDirectory
    {
        get => _materialDirectory;
        set { _materialDirectory = value; Changed(); Changed(nameof(InputPath)); Changed(nameof(OutputPath)); }
    }
    public string InputPath => string.IsNullOrWhiteSpace(MaterialDirectory) ? "" : Path.Combine(MaterialDirectory, "原图");
    public string OutputPath => string.IsNullOrWhiteSpace(MaterialDirectory) ? "" : Path.Combine(MaterialDirectory, "远景");
    public double ThresholdPercent { get => _thresholdPercent; set { _thresholdPercent = value; Changed(); } }
    public bool IsBusy { get; private set; }
    public bool IsIdle => !IsBusy;
    public double Progress { get; private set; }
    public string Status { get; private set; } = "选择素材目录，生成近景 / 远景索引。";
    public string Log { get; private set; } = "";
    public ICommand BrowseMaterialCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand ViewCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand CancelCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public FarSceneDetectionViewModel() : this(null) { }
    public FarSceneDetectionViewModel(string? diagnosticLogPath)
    {
        _diagnosticLogPath = diagnosticLogPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyanTools", "logs", "far-scene-last-error.txt");
        BrowseMaterialCommand = new RelayCommand(Browse, () => IsIdle);
        StartCommand = new RelayCommand(async () => await RunAsync(), () => IsIdle);
        ViewCommand = new RelayCommand(async () => await RunStandaloneAsync(s => s.ViewAsync(), false), () => IsIdle);
        RestoreCommand = new RelayCommand(async () => await RunStandaloneAsync(s => s.RestoreAsync(), false), () => IsIdle);
        CancelCommand = new RelayCommand(() => _activeSession?.CancelCommand.Execute(null), () => IsBusy);
    }

    private void Browse()
    {
        using var dialog = new CommonOpenFileDialog { IsFolderPicker = true, Title = LocalizationManager.Translate("选择素材目录（包含原图文件夹）") };
        if (dialog.ShowDialog() == CommonFileDialogResult.Ok) MaterialDirectory = dialog.FileName;
    }

    public async Task RunAsync()
    {
        if (IsBusy) return;
        Log = $"开始生成索引。素材目录：{MaterialDirectory}{Environment.NewLine}原图路径：{InputPath}{Environment.NewLine}";
        Changed(nameof(Log));
        if (MaterialClassificationSession.IsAttached(this))
        {
            await MaterialClassificationSession.TryRunAsync(this, DetectAsync);
            return;
        }
        await RunStandaloneAsync(async _ => { await MaterialClassificationSession.TryRunAsync(this, DetectAsync); }, true);
    }

    private async Task RunStandaloneAsync(Func<MaterialClassificationSession, Task> operation, bool attach)
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(MaterialDirectory) || !Directory.Exists(MaterialDirectory))
        { ReportFailure("请选择有效的素材目录。", new DirectoryNotFoundException(MaterialDirectory)); return; }
        var session = new MaterialClassificationSession(this, Path.GetFullPath(MaterialDirectory), MaterialClassificationTool.FarScene, () => {});
        _activeSession = session;
        SetBusy(true);
        session.PropertyChanged += SessionChanged;
        try
        {
            if (attach) session.Attach();
            await operation(session);
            Status = session.Status; Changed(nameof(Status));
        }
        catch (Exception ex) { ReportFailure(ex.Message, ex); }
        finally
        {
            if (attach) session.Detach();
            session.PropertyChanged -= SessionChanged;
            _activeSession = null; SetBusy(false);
        }
        void SessionChanged(object? sender, PropertyChangedEventArgs args)
        {
            Status = session.Status; Changed(nameof(Status)); CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task DetectAsync()
    {
        var session = MaterialClassificationSession.Current ?? throw new InvalidOperationException("索引会话尚未初始化。");
        bool ownsBusy = !IsBusy;
        if (ownsBusy) SetBusy(true);
        double threshold = ThresholdPercent;
        Progress = 0; Changed(nameof(Progress));
        var progress = new Progress<double>(value => { Progress = value; Changed(nameof(Progress)); });
        var log = new Progress<string>(line => { Log += line + Environment.NewLine; Changed(nameof(Log)); });
        try
        {
            await Task.Run(() => FarSceneDetectionProcessor.Generate(session.Builder!, threshold, progress,
                line => ((IProgress<string>)log).Report(line), session.Token), session.Token);
            MaterialClassificationSession.MarkSuccessful();
        }
        finally { if (ownsBusy) SetBusy(false); }
    }
    private void SetBusy(bool value) { IsBusy = value; Changed(nameof(IsBusy)); Changed(nameof(IsIdle)); CommandManager.InvalidateRequerySuggested(); }
    public void ReportFailure(string message, Exception exception)
    {
        Status = message;
        string details = $"{DateTime.Now:O}{Environment.NewLine}素材：{MaterialDirectory}{Environment.NewLine}{message}{Environment.NewLine}{exception}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_diagnosticLogPath)!);
            File.WriteAllText(_diagnosticLogPath, details);
            Status += $"{Environment.NewLine}诊断日志：{_diagnosticLogPath}";
        }
        catch (Exception logError) { Status += $"{Environment.NewLine}诊断日志写入失败：{logError.Message}"; }
        Log += details + Environment.NewLine;
        Changed(nameof(Status)); Changed(nameof(Log));
        OperationFailed?.Invoke(Status);
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
