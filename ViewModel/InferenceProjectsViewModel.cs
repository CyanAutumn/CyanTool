using CyanTools.Model;
using CyanTools.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CyanTools.ViewModel
{
    public sealed class InferenceProjectsViewModel : INotifyPropertyChanged, IDisposable
    {
        private const int MaxLogLines = 800;
        private readonly TrainingBackendBindingStore _bindingStore;
        private readonly WslPathResolver _wslPathResolver;
        private readonly InferenceFrontendManager _manager;
        private readonly Queue<string> _logLines = new();
        private TrainingBackendBinding? _selectedProject;
        private string _logText = string.Empty;
        private string _statusText = "选择跑图工程后可启动或打开模型目录。";
        private bool _isBusy;
        private bool _disposed;

        public InferenceProjectsViewModel()
            : this(new TrainingBackendBindingStore(), new WslPathResolver(), new InferenceFrontendManager())
        {
        }

        internal InferenceProjectsViewModel(
            TrainingBackendBindingStore bindingStore,
            WslPathResolver wslPathResolver,
            InferenceFrontendManager manager)
        {
            _bindingStore = bindingStore ?? throw new ArgumentNullException(nameof(bindingStore));
            _wslPathResolver = wslPathResolver ?? throw new ArgumentNullException(nameof(wslPathResolver));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _manager.LogReceived += Manager_LogReceived;
            _manager.StateChanged += Manager_StateChanged;

            Projects = new ObservableCollection<TrainingBackendBinding>();
            ReloadProjectsCommand = new RelayCommand(ReloadProjects, () => !IsBusy);
            StartCommand = new RelayCommand(async _ => await StartSelectedAsync(), _ => CanStart);
            StopCommand = new RelayCommand(async _ => await StopSelectedAsync(), _ => CanStop);
            OpenFolderCommand = new RelayCommand(OpenFolder, _ => SelectedProject != null && !IsBusy);
            ClearLogCommand = new RelayCommand(ClearLog);

            ReloadProjects();
            AppendLog("跑图工程管理已就绪；路径来自“选项 → 运行环境路径绑定”。");
        }

        public ObservableCollection<TrainingBackendBinding> Projects { get; }

        public TrainingBackendBinding? SelectedProject
        {
            get => _selectedProject;
            set
            {
                if (SetField(ref _selectedProject, value))
                {
                    RefreshSelectedState();
                }
            }
        }

        public string LogText
        {
            get => _logText;
            private set => SetField(ref _logText, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetField(ref _statusText, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetField(ref _isBusy, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public bool IsRunning => SelectedProject != null && _manager.IsRunning(SelectedProject.Id);
        public bool CanStart => SelectedProject != null && !IsBusy && !IsRunning;
        public bool CanStop => SelectedProject != null && !IsBusy && IsRunning;
        public string SelectedRootPath => SelectedProject?.TrainerPath ?? string.Empty;
        public string SelectedRuntimeText
            => SelectedProject == null
                ? string.Empty
                : SelectedProject.IsWsl
                    ? $"WSL2 · {NormalizedDistribution(SelectedProject)} · conda {NormalizedCondaEnvironment(SelectedProject)}"
                    : "Windows 原生";

        public ICommand ReloadProjectsCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand ClearLogCommand { get; }

        public void ReloadProjects()
        {
            string? selectedId = SelectedProject?.Id;
            Projects.Clear();
            foreach (TrainingBackendBinding binding in _bindingStore.ListAll()
                         .Where(binding => RuntimePathBindingBackendIds.IsInferenceFrontend(binding.BackendId)))
            {
                Projects.Add(binding);
            }

            SelectedProject = Projects.FirstOrDefault(binding =>
                                  string.Equals(binding.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                              ?? Projects.FirstOrDefault();
            AppendLog($"已加载 {Projects.Count} 个跑图工程路径绑定。");
        }

        private async Task StartSelectedAsync()
        {
            TrainingBackendBinding? binding = SelectedProject;
            if (binding == null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                StatusText = $"正在启动 {binding.Name}…";
                await _manager.StartAsync(binding);
                StatusText = $"{binding.Name} 运行中。";
            }
            catch (Exception exception)
            {
                StatusText = $"启动失败：{exception.Message}";
                AppendLog(StatusText);
            }
            finally
            {
                IsBusy = false;
                RefreshSelectedState(keepFailureMessage: true);
            }
        }

        private async Task StopSelectedAsync()
        {
            TrainingBackendBinding? binding = SelectedProject;
            if (binding == null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                StatusText = $"正在停止 {binding.Name}…";
                await _manager.StopAsync(binding);
                StatusText = $"{binding.Name} 已停止。";
            }
            catch (Exception exception)
            {
                StatusText = $"停止失败：{exception.Message}";
                AppendLog(StatusText);
            }
            finally
            {
                IsBusy = false;
                RefreshSelectedState(keepFailureMessage: true);
            }
        }

        private void OpenFolder(object parameter)
        {
            TrainingBackendBinding? binding = SelectedProject;
            if (binding == null || !Enum.TryParse(parameter?.ToString(), ignoreCase: true, out InferenceFrontendFolder folder))
            {
                return;
            }

            try
            {
                string root = InferenceFrontendLaunchPlanner.ResolveFolder(
                    binding,
                    InferenceFrontendFolder.Root,
                    _wslPathResolver);
                if (!Directory.Exists(root))
                {
                    throw new DirectoryNotFoundException($"跑图工程根目录不存在：{root}");
                }

                string directory = InferenceFrontendLaunchPlanner.ResolveFolder(binding, folder, _wslPathResolver);
                if (folder != InferenceFrontendFolder.Root && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    AppendLog($"[{binding.Name}] 已创建模型目录：{directory}");
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = true
                });
                StatusText = $"已打开：{directory}";
            }
            catch (Exception exception)
            {
                StatusText = $"打开目录失败：{exception.Message}";
                AppendLog(StatusText);
            }
        }

        private void Manager_LogReceived(object? sender, InferenceFrontendLogEventArgs args)
            => RunOnUiThread(() => AppendLog($"[{args.BindingName}] {args.Message}"));

        private void Manager_StateChanged(object? sender, InferenceFrontendStateEventArgs args)
            => RunOnUiThread(() =>
            {
                if (string.Equals(SelectedProject?.Id, args.BindingId, StringComparison.OrdinalIgnoreCase))
                {
                    RefreshSelectedState();
                }
            });

        private void RefreshSelectedState(bool keepFailureMessage = false)
        {
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(SelectedRootPath));
            OnPropertyChanged(nameof(SelectedRuntimeText));
            if (!keepFailureMessage || !StatusText.Contains("失败", StringComparison.Ordinal))
            {
                StatusText = SelectedProject == null
                    ? "没有可用的跑图工程绑定。"
                    : IsRunning
                        ? $"{SelectedProject.Name} 运行中。"
                        : $"{SelectedProject.Name} 未运行。";
            }
            RaiseCommandStates();
        }

        private void AppendLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }
            _logLines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
            while (_logLines.Count > MaxLogLines)
            {
                _logLines.Dequeue();
            }
            LogText = string.Join(Environment.NewLine, _logLines);
        }

        private void ClearLog()
        {
            _logLines.Clear();
            LogText = string.Empty;
        }

        private void RaiseCommandStates()
        {
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            CommandManager.InvalidateRequerySuggested();
        }

        private static string NormalizedDistribution(TrainingBackendBinding binding)
            => string.IsNullOrWhiteSpace(binding.WslDistribution) ? "Ubuntu" : binding.WslDistribution.Trim();

        private static string NormalizedCondaEnvironment(TrainingBackendBinding binding)
            => string.IsNullOrWhiteSpace(binding.CondaEnvironment)
                ? InferenceFrontendLaunchPlanner.GetDefaultCondaEnvironment(binding.BackendId)
                : binding.CondaEnvironment.Trim();

        private static void RunOnUiThread(Action action)
        {
            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.BeginInvoke(action);
            }
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _manager.LogReceived -= Manager_LogReceived;
            _manager.StateChanged -= Manager_StateChanged;
            _manager.Dispose();
        }
    }
}
