using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.Win32;

namespace CyanTools.ViewModel
{
    public sealed class OptionsViewModel : INotifyPropertyChanged
    {
        private const int MaxLogLines = 300;
        private readonly TrainingBackendDeployer _trainingBackendDeployer = new();
        private bool _isBusy;
        private string _cudnnPackageStatus = "拖入 NVIDIA cuDNN 9 的 Windows zip 安装包，或点击选择文件。";
        private string _nvidiaAppStatus = "尚未检测。";
        private string _trainingDeploymentStatus = "尚未检测训练后端部署环境。";
        private string _wslStatus = "尚未检测 WSL2 / Ubuntu。";
        private string _sdScriptsStatus = "尚未检测 sd-scripts Windows。";
        private string _animaFastLoraStatus = "尚未检测 Anima Fast LoRA Windows。";
        private string _wslSdScriptsStatus = "尚未检测 wsl-sd-scripts。";
        private string _diffusionPipeStatus = "尚未检测 diffusion-pipe / WSL2 环境。";
        private string _trainingBackendProxyPort = "7890";
        private TrainingBackendMirrorOption _selectedTrainingBackendMirror = null!;

        public OptionsViewModel()
        {
            LogLines = new ObservableCollection<string>();
            TrainingBackendMirrorOptions = new ObservableCollection<TrainingBackendMirrorOption>
            {
                new("default", "官方源"),
                new("tsinghua", "清华 TUNA"),
                new("aliyun", "阿里云")
            };
            _selectedTrainingBackendMirror = TrainingBackendMirrorOptions[0];

            CheckCudaCommand = new RelayCommand(async _ => await CheckOrInstallCudaAsync(), _ => !IsBusy);
            InstallCudaToolkitCommand = new RelayCommand(_ => TryLaunchWingetCudaInstall(), _ => !IsBusy);
            OpenCudaCudnnPagesCommand = new RelayCommand(_ => OpenCudaInstallPages(), _ => !IsBusy);
            SelectCudnnPackageCommand = new RelayCommand(async _ => await SelectAndInstallCudnnPackageAsync(), _ => !IsBusy);
            RefreshNvidiaAppStatusCommand = new RelayCommand(_ => RefreshNvidiaAppStatus(), _ => !IsBusy);
            OpenNvidiaAppPageCommand = new RelayCommand(_ => OpenNvidiaAppPage(), _ => !IsBusy);
            CheckTrainingBackendCommand = new RelayCommand(async parameter => await RunTrainingBackendActionAsync(GetBackendId(parameter), TrainingBackendAction.Check), _ => !IsBusy);
            InstallTrainingBackendCommand = new RelayCommand(async parameter => await RunTrainingBackendActionAsync(GetBackendId(parameter), TrainingBackendAction.Install), _ => !IsBusy);
            ValidateTrainingBackendCommand = new RelayCommand(async parameter => await RunTrainingBackendActionAsync(GetBackendId(parameter), TrainingBackendAction.Validate), _ => !IsBusy);
            UpdateTrainingBackendCommand = new RelayCommand(async parameter => await RunTrainingBackendActionAsync(GetBackendId(parameter), TrainingBackendAction.Update), _ => !IsBusy);
            ReinstallTrainingBackendCommand = new RelayCommand(async parameter => await RunTrainingBackendActionAsync(GetBackendId(parameter), TrainingBackendAction.Reinstall), _ => !IsBusy);
            DeployAllTrainingBackendsCommand = new RelayCommand(async _ => await DeployAllTrainingBackendsAsync(), _ => !IsBusy);
            InstallWslCommand = new RelayCommand(async _ => await RunTrainingBackendActionAsync("wsl", TrainingBackendAction.InstallWsl), _ => !IsBusy);
            InstallUbuntuCommand = new RelayCommand(async _ => await RunTrainingBackendActionAsync("wsl", TrainingBackendAction.InstallUbuntu), _ => !IsBusy);
            DeleteUbuntuCommand = new RelayCommand(async _ => await RunTrainingBackendActionAsync("wsl", TrainingBackendAction.DeleteUbuntu), _ => !IsBusy);

            RefreshNvidiaAppStatus();
        }

        public ObservableCollection<string> LogLines { get; }
        public ObservableCollection<TrainingBackendMirrorOption> TrainingBackendMirrorOptions { get; }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetField(ref _isBusy, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public string CudnnPackageStatus
        {
            get => _cudnnPackageStatus;
            set => SetField(ref _cudnnPackageStatus, value);
        }

        public string NvidiaAppStatus
        {
            get => _nvidiaAppStatus;
            set => SetField(ref _nvidiaAppStatus, value);
        }

        public string TrainingDeploymentStatus
        {
            get => _trainingDeploymentStatus;
            set => SetField(ref _trainingDeploymentStatus, value);
        }

        public string WslStatus
        {
            get => _wslStatus;
            set => SetField(ref _wslStatus, value);
        }

        public string SdScriptsStatus
        {
            get => _sdScriptsStatus;
            set => SetField(ref _sdScriptsStatus, value);
        }

        public string AnimaFastLoraStatus
        {
            get => _animaFastLoraStatus;
            set => SetField(ref _animaFastLoraStatus, value);
        }

        public string WslSdScriptsStatus
        {
            get => _wslSdScriptsStatus;
            set => SetField(ref _wslSdScriptsStatus, value);
        }

        public string DiffusionPipeStatus
        {
            get => _diffusionPipeStatus;
            set => SetField(ref _diffusionPipeStatus, value);
        }

        public string TrainingBackendProxyPort
        {
            get => _trainingBackendProxyPort;
            set => SetField(ref _trainingBackendProxyPort, value);
        }

        public TrainingBackendMirrorOption SelectedTrainingBackendMirror
        {
            get => _selectedTrainingBackendMirror;
            set => SetField(ref _selectedTrainingBackendMirror, value);
        }

        public ICommand CheckCudaCommand { get; }
        public ICommand InstallCudaToolkitCommand { get; }
        public ICommand OpenCudaCudnnPagesCommand { get; }
        public ICommand SelectCudnnPackageCommand { get; }
        public ICommand RefreshNvidiaAppStatusCommand { get; }
        public ICommand OpenNvidiaAppPageCommand { get; }
        public ICommand CheckTrainingBackendCommand { get; }
        public ICommand InstallTrainingBackendCommand { get; }
        public ICommand ValidateTrainingBackendCommand { get; }
        public ICommand UpdateTrainingBackendCommand { get; }
        public ICommand ReinstallTrainingBackendCommand { get; }
        public ICommand DeployAllTrainingBackendsCommand { get; }
        public ICommand InstallWslCommand { get; }
        public ICommand InstallUbuntuCommand { get; }
        public ICommand DeleteUbuntuCommand { get; }

        public async Task InstallCudnnPackageAsync(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                return;
            }

            if (!File.Exists(packagePath))
            {
                CudnnPackageStatus = "未找到拖入的 cuDNN 安装包。";
                AddLog($"cuDNN 安装包不存在: {packagePath}");
                return;
            }

            if (!packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                CudnnPackageStatus = "请拖入 NVIDIA cuDNN 的 Windows zip 安装包。";
                AddLog($"已拒绝非 zip 文件: {packagePath}");
                return;
            }

            IsBusy = true;
            try
            {
                CudnnPackageStatus = "正在安装 cuDNN...";
                var result = await Task.Run(() => InstallCudnnZip(packagePath));
                CudnnPackageStatus = result.StatusMessage;
                AddLog(result.LogMessage);

                var check = await Task.Run(TaggingProcessor.CheckCudaEnvironment);
                AddLog($"安装后 CUDA/cuDNN 检查结果: {check.Details}");

                MessageBox.Show(
                    $"{result.StatusMessage}\n\n{check.Details}\n\n如果仍提示缺少 DLL，请重启本软件后再检查一次。",
                    "cuDNN 安装",
                    MessageBoxButton.OK,
                    check.CudaExecutionProviderAvailable ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                CudnnPackageStatus = $"cuDNN 安装失败: {ex.Message}";
                AddLog(CudnnPackageStatus);
                MessageBox.Show(
                    $"{ex.Message}\n\n请确认拖入的是 NVIDIA cuDNN 9 Windows zip 包，并且程序目录或 CUDA 12 bin 目录可写。",
                    "cuDNN 安装失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task CheckOrInstallCudaAsync()
        {
            IsBusy = true;
            try
            {
                RefreshNvidiaAppStatus();
                var check = await Task.Run(TaggingProcessor.CheckCudaEnvironment);
                AddLog($"CUDA/cuDNN 检查结果: {check.Details}");

                if (check.NvidiaDriverDetected && check.CudaExecutionProviderAvailable)
                {
                    MessageBox.Show(
                        "CUDA 环境可用，使用 GPU 推理的工具可以正常启用。",
                        "CUDA/cuDNN 检查",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                if (!check.CudaAppendApiSupported)
                {
                    MessageBox.Show(
                        $"{check.Details}\n\n检测到当前程序的 onnxruntime.dll 不支持 CUDA 执行提供器。\n这不是安装 CUDA Toolkit 能解决的问题，请更新/重新发布带 `Microsoft.ML.OnnxRuntime.Gpu` 的版本。",
                        "CUDA/cuDNN 检查",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    $"{check.Details}\n\n是否尝试使用 winget 安装 CUDA Toolkit 12.x（推荐 12.4）？\n\n选择“否”会打开 NVIDIA CUDA 与 cuDNN 下载页面。cuDNN 可在本页拖入 zip 自动安装。",
                    "CUDA/cuDNN 检查/安装",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    TryLaunchWingetCudaInstall();
                }
                else if (result == MessageBoxResult.No)
                {
                    OpenCudaInstallPages(check.NvidiaDriverDetected);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void TryLaunchWingetCudaInstall()
        {
            const string wingetArgs =
                "/k winget install -e --id Nvidia.CUDA -s winget --version 12.4 --accept-source-agreements --accept-package-agreements || winget show --id Nvidia.CUDA -s winget --versions || winget search cuda -s winget";

            try
            {
                RefreshNvidiaAppStatus();
                AddLog("正在启动 winget 安装 CUDA Toolkit 12.4。");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = wingetArgs,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AddLog($"启动 winget 失败: {ex.Message}");
                MessageBox.Show(
                    $"启动 winget 失败：{ex.Message}\n\n将为你打开 CUDA 与 cuDNN 下载页面，请手动安装。",
                    "CUDA 安装",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                OpenCudaInstallPages(nvidiaDriverDetected: true);
            }
        }

        private void OpenCudaInstallPages()
        {
            bool nvidiaDriverDetected = false;
            try
            {
                nvidiaDriverDetected = TaggingProcessor.CheckCudaEnvironment().NvidiaDriverDetected;
            }
            catch (Exception ex)
            {
                AddLog($"检测 NVIDIA 驱动状态失败: {ex.Message}");
            }

            OpenCudaInstallPages(nvidiaDriverDetected);
        }

        private void OpenCudaInstallPages(bool nvidiaDriverDetected)
        {
            try
            {
                AddLog("正在打开 NVIDIA CUDA/cuDNN 下载页面。");
                if (!nvidiaDriverDetected)
                {
                    OpenNvidiaAppPage();
                    Process.Start(new ProcessStartInfo("https://www.nvidia.com/en-us/drivers/") { UseShellExecute = true });
                }

                Process.Start(new ProcessStartInfo("https://developer.nvidia.com/cuda-downloads") { UseShellExecute = true });
                Process.Start(new ProcessStartInfo("https://developer.nvidia.com/cudnn") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AddLog($"打开下载页面失败: {ex.Message}");
            }
        }

        private async Task SelectAndInstallCudnnPackageAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 cuDNN Windows zip 安装包",
                Filter = "cuDNN zip package (*.zip)|*.zip|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                await InstallCudnnPackageAsync(dialog.FileName);
            }
        }

        private CudnnInstallResult InstallCudnnZip(string packagePath)
        {
            string targetDirectory = ResolveCudnnInstallDirectory();

            using var archive = ZipFile.OpenRead(packagePath);
            var dllEntries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Where(entry => entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Where(entry => ShouldCopyCudnnDll(entry.Name))
                .ToList();

            if (dllEntries.Count == 0)
            {
                throw new InvalidOperationException("安装包中没有找到 cudnn*.dll。请确认下载的是 cuDNN 9 Windows zip 包。");
            }

            var selectedDllEntries = SelectCuda12CudnnDllEntries(dllEntries);

            bool hasRequiredCudnn9Dll = selectedDllEntries.Any(entry =>
                string.Equals(entry.Name, "cudnn64_9.dll", StringComparison.OrdinalIgnoreCase));
            if (!hasRequiredCudnn9Dll)
            {
                throw new InvalidOperationException("安装包中没有找到 CUDA 12.x 可用的 cudnn64_9.dll。当前 ONNX Runtime GPU 版本需要 CUDA 12.x + cuDNN 9。");
            }

            int copiedCount = 0;
            foreach (var entry in selectedDllEntries)
            {
                string destinationPath = Path.Combine(targetDirectory, entry.Name);
                entry.ExtractToFile(destinationPath, overwrite: true);
                copiedCount++;
            }

            return new CudnnInstallResult(
                $"已安装 {copiedCount} 个 cuDNN DLL 到 {targetDirectory}。",
                $"已从 {Path.GetFileName(packagePath)} 复制 {copiedCount} 个 cuDNN DLL 到 {targetDirectory}");
        }

        private static bool ShouldCopyCudnnDll(string fileName)
        {
            return fileName.StartsWith("cudnn", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "zlibwapi.dll", StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<ZipArchiveEntry> SelectCuda12CudnnDllEntries(IReadOnlyList<ZipArchiveEntry> dllEntries)
        {
            var versionedEntries = dllEntries
                .Select(entry => new
                {
                    Entry = entry,
                    HasVersion = TryGetCudaCompatibilityVersion(entry.FullName, out var version),
                    Version = version
                })
                .Where(item => item.HasVersion)
                .ToList();

            if (versionedEntries.Count == 0)
            {
                return DeduplicateByFileName(dllEntries);
            }

            var cuda12Version = versionedEntries
                .Where(item => item.Version.Major == 12)
                .Select(item => item.Version)
                .Distinct()
                .OrderByDescending(version => version)
                .FirstOrDefault();

            if (cuda12Version == null)
            {
                throw new InvalidOperationException("该 cuDNN 9 安装包没有包含 CUDA 12.x 的 DLL。请下载包含 bin/12.x/x64 的 Windows zip 包。");
            }

            var selectedEntries = versionedEntries
                .Where(item => item.Version.Equals(cuda12Version))
                .Select(item => item.Entry)
                .ToList();

            selectedEntries.AddRange(dllEntries.Where(entry =>
                string.Equals(entry.Name, "zlibwapi.dll", StringComparison.OrdinalIgnoreCase) &&
                !versionedEntries.Any(item => ReferenceEquals(item.Entry, entry))));

            return DeduplicateByFileName(selectedEntries);
        }

        private static IReadOnlyList<ZipArchiveEntry> DeduplicateByFileName(IEnumerable<ZipArchiveEntry> entries)
        {
            return entries
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static bool TryGetCudaCompatibilityVersion(string path, out Version version)
        {
            version = new Version();
            var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (Version.TryParse(segment, out var parsed) &&
                    (parsed.Major == 12 || parsed.Major == 13))
                {
                    version = parsed;
                    return true;
                }
            }

            return false;
        }

        private static void EnsureDirectoryWritable(string directory)
        {
            Directory.CreateDirectory(directory);
            string testFile = Path.Combine(directory, $".cyantools_write_test_{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(testFile, string.Empty);
            }
            finally
            {
                if (File.Exists(testFile))
                {
                    File.Delete(testFile);
                }
            }
        }

        private string ResolveCudnnInstallDirectory()
        {
            string appDirectory = AppContext.BaseDirectory;
            if (IsDirectoryWritable(appDirectory))
            {
                return appDirectory;
            }

            AddLog($"程序目录不可写，尝试安装到 CUDA 12 bin 目录: {appDirectory}");
            TaggingProcessor.TryConfigureCuda12ForCurrentProcess(
                AddLog,
                out string? cudaBinDirectory,
                out _,
                out _);

            if (!string.IsNullOrWhiteSpace(cudaBinDirectory) &&
                IsDirectoryWritable(cudaBinDirectory))
            {
                return cudaBinDirectory;
            }

            throw new UnauthorizedAccessException(
                "程序目录和 CUDA 12 bin 目录都不可写。请以管理员身份运行本软件，或手动把 cuDNN DLL 解压到程序目录。");
        }

        private static bool IsDirectoryWritable(string directory)
        {
            try
            {
                EnsureDirectoryWritable(directory);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RefreshNvidiaAppStatus()
        {
            var installedApps = EnumerateInstalledNvidiaDesktopApps();
            bool hasNvidiaApp = installedApps.Any(app =>
                app.IndexOf("NVIDIA App", StringComparison.OrdinalIgnoreCase) >= 0 ||
                app.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase));
            bool hasGeForceExperience = installedApps.Any(app =>
                app.IndexOf("GeForce Experience", StringComparison.OrdinalIgnoreCase) >= 0);

            if (hasNvidiaApp && hasGeForceExperience)
            {
                NvidiaAppStatus = "检测到 NVIDIA App 和 NVIDIA GeForce Experience 同时存在。建议卸载 GeForce Experience，然后修复或重装 NVIDIA App。";
                AddLog(NvidiaAppStatus);
                return;
            }

            if (hasNvidiaApp)
            {
                NvidiaAppStatus = "已检测到 NVIDIA App。请不要再安装 NVIDIA GeForce Experience，避免两者冲突。";
                AddLog(NvidiaAppStatus);
                return;
            }

            if (hasGeForceExperience)
            {
                NvidiaAppStatus = "检测到 NVIDIA GeForce Experience。NVIDIA 已改用 NVIDIA App，建议迁移到 NVIDIA App。";
                AddLog(NvidiaAppStatus);
                return;
            }

            NvidiaAppStatus = "未检测到 NVIDIA App 或 GeForce Experience。需要驱动管理工具时建议安装 NVIDIA App。";
            AddLog(NvidiaAppStatus);
        }

        private static string[] EnumerateInstalledNvidiaDesktopApps()
        {
            var names = new Collection<string>();
            ReadInstalledAppNames(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", names);
            ReadInstalledAppNames(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", names);
            ReadInstalledAppNames(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", names);

            return names
                .Where(name =>
                    name.IndexOf("NVIDIA App", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("GeForce Experience", StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void ReadInstalledAppNames(RegistryKey root, string subKeyPath, Collection<string> names)
        {
            try
            {
                using var uninstallKey = root.OpenSubKey(subKeyPath);
                if (uninstallKey == null)
                {
                    return;
                }

                foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using var appKey = uninstallKey.OpenSubKey(subKeyName);
                    if (appKey?.GetValue("DisplayName") is string displayName &&
                        displayName.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(displayName);
                    }
                }
            }
            catch
            {

            }
        }

        private void OpenNvidiaAppPage()
        {
            try
            {
                AddLog("正在打开 NVIDIA App 页面。");
                Process.Start(new ProcessStartInfo("https://www.nvidia.com/en-us/software/nvidia-app/") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AddLog($"打开 NVIDIA App 页面失败: {ex.Message}");
            }
        }

        private async Task<bool> RunTrainingBackendActionAsync(
            string backendId,
            TrainingBackendAction action,
            bool manageBusyState = true,
            bool showFailureDialog = true,
            bool confirmAction = true)
        {
            if (confirmAction && !ConfirmTrainingBackendAction(backendId, action))
            {
                return false;
            }

            int? proxyPort = ParseTrainingBackendProxyPort();
            if (proxyPort == -1)
            {
                return false;
            }

            if (manageBusyState)
            {
                IsBusy = true;
            }

            string backendLabel = GetBackendLabel(backendId);
            try
            {
                string runningStatus = GetTrainingBackendRunningStatus(backendLabel, action);
                SetBackendStatus(backendId, runningStatus);
                TrainingDeploymentStatus = runningStatus;
                AddLog(runningStatus);

                var options = new TrainingBackendOptions(
                    backendId,
                    SelectedTrainingBackendMirror?.Id ?? "default",
                    proxyPort);

                await _trainingBackendDeployer.RunAsync(
                    action,
                    options,
                    new Progress<string>(AddLog));

                string completedStatus = GetTrainingBackendCompletedStatus(backendLabel, action);
                SetBackendStatus(backendId, completedStatus);
                TrainingDeploymentStatus = completedStatus;
                AddLog(completedStatus);
                return true;
            }
            catch (OperationCanceledException)
            {
                string canceledStatus = $"{backendLabel} 操作已取消。";
                SetBackendStatus(backendId, canceledStatus);
                TrainingDeploymentStatus = canceledStatus;
                AddLog(canceledStatus);
                return false;
            }
            catch (Exception ex)
            {
                string failedStatus = $"{backendLabel} 操作失败: {ex.Message}";
                SetBackendStatus(backendId, failedStatus);
                TrainingDeploymentStatus = failedStatus;
                AddLog(failedStatus);
                if (showFailureDialog)
                {
                    MessageBox.Show(
                        $"{ex.Message}\n\n请查看选项页日志。WSL/Ubuntu 安装可能需要管理员权限或重启 Windows；Windows 后端首次部署会下载 Git、uv、Python 与 CUDA 依赖。",
                        $"{backendLabel} 部署",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return false;
            }
            finally
            {
                if (manageBusyState)
                {
                    IsBusy = false;
                }
            }
        }

        private async Task DeployAllTrainingBackendsAsync()
        {
            if (MessageBox.Show(
                    "确认一键部署全部训练后端？\n\n将依次安装 Windows sd-scripts、Anima Fast LoRA、WSL sd-scripts 和 diffusion-pipe。WSL 后端要求先准备好 WSL2 与 Ubuntu。",
                    "一键部署全部后端",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information) != MessageBoxResult.Yes)
            {
                return;
            }

            int? proxyPort = ParseTrainingBackendProxyPort();
            if (proxyPort == -1)
            {
                return;
            }

            string[] backendIds = ["sd-scripts", "anima-fast-lora", "wsl-sd-scripts", "diffusion-pipe"];
            int succeeded = 0;
            IsBusy = true;
            TrainingDeploymentStatus = "正在部署全部训练后端...";
            AddLog(TrainingDeploymentStatus);
            try
            {
                foreach (string backendId in backendIds)
                {
                    if (await RunTrainingBackendActionAsync(
                            backendId,
                            TrainingBackendAction.Install,
                            manageBusyState: false,
                            showFailureDialog: false,
                            confirmAction: false))
                    {
                        succeeded++;
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }

            TrainingDeploymentStatus = succeeded == backendIds.Length
                ? "全部训练后端部署完成。"
                : $"训练后端批量部署完成：{succeeded}/{backendIds.Length} 成功，请查看日志处理失败项。";
            AddLog(TrainingDeploymentStatus);
            MessageBox.Show(
                TrainingDeploymentStatus,
                "训练后端部署",
                MessageBoxButton.OK,
                succeeded == backendIds.Length ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private bool ConfirmTrainingBackendAction(string backendId, TrainingBackendAction action)
        {
            if (action == TrainingBackendAction.DeleteUbuntu)
            {
                return MessageBox.Show(
                    "确认删除 Ubuntu 子系统？\n\n注意：这会删除 Ubuntu 子系统内的全部数据，包括所有 WSL 训练后端。",
                    "删除 Ubuntu 子系统",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes;
            }

            if (action == TrainingBackendAction.Reinstall)
            {
                string backendLabel = GetBackendLabel(backendId);
                return MessageBox.Show(
                    $"确认重装 {backendLabel}？\n\n这会删除该后端的旧仓库和独立 Python/conda 环境，然后重新下载；不会删除训练集、模型或整个 Ubuntu 子系统。",
                    $"重装 {backendLabel}",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes;
            }

            return true;
        }

        private int? ParseTrainingBackendProxyPort()
        {
            if (string.IsNullOrWhiteSpace(TrainingBackendProxyPort))
            {
                return null;
            }

            if (!int.TryParse(TrainingBackendProxyPort.Trim(), out int port) ||
                port < 0 ||
                port > 65535)
            {
                MessageBox.Show(
                    "WSL 本地代理端口必须是 0-65535 的整数；填 0 或留空表示不使用代理。",
                    "训练后端部署",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return -1;
            }

            return port == 0 ? null : port;
        }

        private void SetBackendStatus(string backendId, string status)
        {
            switch (backendId)
            {
                case "wsl":
                    WslStatus = status;
                    break;
                case "sd-scripts":
                    SdScriptsStatus = status;
                    break;
                case "anima-fast-lora":
                    AnimaFastLoraStatus = status;
                    break;
                case "wsl-sd-scripts":
                    WslSdScriptsStatus = status;
                    break;
                case "diffusion-pipe":
                    DiffusionPipeStatus = status;
                    break;
            }
        }

        private static string GetBackendId(object? parameter)
        {
            return parameter as string ?? "diffusion-pipe";
        }

        private static string GetBackendLabel(string backendId)
        {
            return backendId switch
            {
                "wsl" => "WSL 基础环境",
                "sd-scripts" => "sd-scripts Windows",
                "anima-fast-lora" => "Anima Fast LoRA Windows",
                "wsl-sd-scripts" => "wsl-sd-scripts",
                "diffusion-pipe" => "diffusion-pipe",
                _ => backendId
            };
        }

        private static string GetTrainingBackendRunningStatus(string backendLabel, TrainingBackendAction action)
        {
            return action switch
            {
                TrainingBackendAction.Check => $"正在检测 {backendLabel}...",
                TrainingBackendAction.InstallWsl => "正在安装或启用 Windows WSL 功能...",
                TrainingBackendAction.InstallUbuntu => "正在安装或初始化 Ubuntu 子系统...",
                TrainingBackendAction.Install => $"正在安装 {backendLabel}...",
                TrainingBackendAction.Validate => $"正在校验 {backendLabel}...",
                TrainingBackendAction.Update => $"正在更新 {backendLabel}...",
                TrainingBackendAction.Reinstall => $"正在重装 {backendLabel}...",
                TrainingBackendAction.DeleteUbuntu => "正在删除 Ubuntu 子系统...",
                _ => $"正在执行 {backendLabel} 操作..."
            };
        }

        private static string GetTrainingBackendCompletedStatus(string backendLabel, TrainingBackendAction action)
        {
            return action switch
            {
                TrainingBackendAction.Check => $"{backendLabel} 环境检测完成。",
                TrainingBackendAction.InstallWsl => "WSL 功能安装命令已完成；如系统提示，请重启 Windows。",
                TrainingBackendAction.InstallUbuntu => "Ubuntu 子系统已就绪。",
                TrainingBackendAction.Install => $"{backendLabel} 安装完成。",
                TrainingBackendAction.Validate => $"{backendLabel} 校验完成。",
                TrainingBackendAction.Update => $"{backendLabel} 更新完成。",
                TrainingBackendAction.Reinstall => $"{backendLabel} 重装完成。",
                TrainingBackendAction.DeleteUbuntu => "Ubuntu 子系统删除流程完成。",
                _ => $"{backendLabel} 操作完成。"
            };
        }

        private void RaiseCommandStates()
        {
            (CheckCudaCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InstallCudaToolkitCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenCudaCudnnPagesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SelectCudnnPackageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RefreshNvidiaAppStatusCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenNvidiaAppPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CheckTrainingBackendCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InstallTrainingBackendCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ValidateTrainingBackendCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (UpdateTrainingBackendCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ReinstallTrainingBackendCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeployAllTrainingBackendsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InstallWslCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InstallUbuntuCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteUbuntuCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void AddLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                while (LogLines.Count > MaxLogLines)
                {
                    LogLines.RemoveAt(0);
                }
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            CommandManager.InvalidateRequerySuggested();
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private sealed record CudnnInstallResult(string StatusMessage, string LogMessage);

        public sealed class TrainingBackendMirrorOption
        {
            public TrainingBackendMirrorOption(string id, string label)
            {
                Id = id;
                Label = label;
            }

            public string Id { get; }
            public string Label { get; }
        }
    }
}
