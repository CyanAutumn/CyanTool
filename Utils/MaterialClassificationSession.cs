using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.ViewModel;

namespace CyanTools.Utils;


public sealed class MaterialClassificationSession : INotifyPropertyChanged
{
    private static readonly ConditionalWeakTable<object, MaterialClassificationSession> Sessions = new();
    private static readonly AsyncLocal<MaterialClassificationSession?> Running = new();
    public static MaterialClassificationSession? Current => Running.Value;
    public MaterialIndexBuilder? Builder { get; private set; }
    private readonly object _model;
    private readonly string _root;
    private readonly MaterialClassificationTool _tool;
    private readonly Action<string> _openDirectory;
    private bool _successful;
    private bool _detached;
    private CancellationTokenSource? _cancellation;
    public CancellationToken Token => _cancellation?.Token ?? CancellationToken.None;
    private readonly Dictionary<System.Reflection.PropertyInfo, object?> _previous = new();
    public bool IsWorking { get; private set; }
    public string Status { get; private set; } = "素材索引模式：开始处理仅生成 JSON；查看时移动图片，还原时移回原图。";
    public ICommand ViewCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand ReturnCommand { get; }
    public ICommand CancelCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public MaterialClassificationSession(object model, string root, MaterialClassificationTool tool, Action goBack, Action<string>? openDirectory = null)
    {
        _model = model; _root = root; _tool = tool;
        _openDirectory = openDirectory ?? (path => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = path, UseShellExecute = true }));
        foreach (string name in new[] { "InputPath", "OutputPath", "InputFolder", "OutputFolder", "ShouldClearInputDirectoryAfterCompletion", "Recursive", "MaterialDirectory" })
            if (model.GetType().GetProperty(name) is { CanRead: true, CanWrite: true } property)
                _previous[property] = property.GetValue(model);
        ViewCommand = new RelayCommand(async () => await ViewAsync(), () => !IsWorking);
        RestoreCommand = new RelayCommand(async () => await RestoreAsync(), () => !IsWorking);
        ReturnCommand = new RelayCommand(goBack, () => !IsWorking);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsWorking);
    }
    public static bool IsAttached(object model) => Sessions.TryGetValue(model, out _);
    public static bool IsRunning(object model) => Sessions.TryGetValue(model, out var session) && session.IsWorking;
    public void Attach() { Sessions.Remove(_model); Sessions.Add(_model, this); Configure(); }
    public void Detach()
    {
        _detached = true;
        if (!IsWorking) { Sessions.Remove(_model); RestorePaths(); }
    }
    private void RestorePaths() { foreach (var (property, value) in _previous) property.SetValue(_model, value); }
    public static object? GetPersistedValue(object model, System.Reflection.PropertyInfo property) =>
        Sessions.TryGetValue(model, out var session) && session._previous.TryGetValue(property, out var value)
            ? value : property.GetValue(model);
    public static void MarkSuccessful() { if (Current != null) Current._successful = true; }

    private void Configure()
    {
        string input = System.IO.Path.Combine(_root, "原图");
        string output = System.IO.Path.Combine(_root, MaterialClassificationStore.FolderName(_tool));
        switch (_model)
        {
            case SameSourceImageGroupingViewModel m: m.InputPath = input; m.OutputPath = output; m.ShouldClearInputDirectoryAfterCompletion = false; break;
            case ImageBucketingViewModel m: m.InputFolder = input; m.OutputFolder = output; m.ShouldClearInputDirectoryAfterCompletion = false; break;
            case ResolutionFilterViewModel m: m.InputPath = input; break;
            case ImageClusteringViewModel m: m.InputPath = input; m.OutputPath = output; m.ShouldClearInputDirectoryAfterCompletion = false; break;
            case AestheticScoringViewModel m: m.InputPath = input; m.OutputPath = output; m.Recursive = true; break;
            case FarSceneDetectionViewModel m: m.MaterialDirectory = _root; break;
        }
    }
    public static async Task<bool> TryRunAsync(object model, Func<Task> run)
    {
        if (Current != null || !Sessions.TryGetValue(model, out var session)) return false;
        if (session.IsWorking) return true;
        await session.GenerateAsync(run);
        return true;
    }
    private async Task GenerateAsync(Func<Task> run)
    {
        SetWorking(true, "正在还原原图并生成索引…");
        string stage = "检查素材目录";
        try
        {
            using var lease = MaterialClassificationStore.Acquire(_root);
            stage = "还原原图并检查路径";
            Builder = await Task.Run(() => lease.CreateBuilder(_tool));
            Configure();
            _successful = false;
            Running.Value = this;
            stage = "运行检测或分类";
            await run();
            Token.ThrowIfCancellationRequested();
            if (!_successful) throw new InvalidOperationException("处理未成功完成，原有索引保持不变；请检查功能日志。");
            stage = "写入索引";
            await Task.Run(Builder.Publish);
            Status = "索引已生成，图片保留在原图目录。点击查看可按分类移动图片。";
        }
        catch (OperationCanceledException) { Status = "已取消，原有索引保持不变。"; }
        catch (Exception ex)
        {
            Status = $"生成索引失败（{stage}）：{ex.Message}";
            if (_model is FarSceneDetectionViewModel farScene) farScene.ReportFailure(Status, ex);
        }
        finally { Running.Value = null; Builder = null; SetWorking(false, Status); }
    }
    public Task ViewAsync() => MoveAsync(true);
    public Task RestoreAsync() => MoveAsync(false);
    private async Task MoveAsync(bool view)
    {
        if (IsWorking) return;
        SetWorking(true, view ? "正在还原上一结果并查看当前分类…" : "正在还原原图…");
        try
        {
            await Task.Run(() =>
            {
                using var lease = MaterialClassificationStore.Acquire(_root);
                if (view) lease.Show(_tool, Token); else lease.Restore();
            });
            Status = view ? "当前分类已展开，使用还原将图片移回原图。" : "图片已还原到原图目录。";

            if (view) _openDirectory(System.IO.Path.Combine(_root, MaterialClassificationStore.FolderName(_tool)));
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { SetWorking(false, Status); }
    }
    private void SetWorking(bool value, string status)
    {
        IsWorking = value; Status = status;
        if (value) _cancellation = new CancellationTokenSource();
        else { _cancellation?.Dispose(); _cancellation = null; }
        if (!value && _detached) { Sessions.Remove(_model); RestorePaths(); }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWorking)));
        CommandManager.InvalidateRequerySuggested();
    }
}
