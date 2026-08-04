using CyanTools.Model;
using CyanTools.Utils;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CyanTools.ViewModel
{
    public abstract class TrainingConfigViewModelBase : INotifyPropertyChanged
    {
        protected static readonly IReadOnlyList<string> DefaultLrSchedulerOptions = new[]
        {
            "linear",
            "cosine",
            "cosine_with_restarts",
            "polynomial",
            "constant",
            "constant_with_warmup"
        };
        protected static readonly IReadOnlyList<string> DefaultOptimizerOptions = new[]
        {
            "AdamW",
            "AdamW8bit",
            "PagedAdamW8bit",
            "RAdamScheduleFree",
            "Lion",
            "Lion8bit",
            "PagedLion8bit",
            "SGDNesterov",
            "SGDNesterov8bit",
            "DAdaptation",
            "DAdaptAdam",
            "DAdaptAdaGrad",
            "DAdaptAdanIP",
            "DAdaptLion",
            "DAdaptSGD",
            "AdaFactor",
            "Prodigy",
            "prodigyplus.ProdigyPlusScheduleFree",
            "pytorch_optimizer.CAME"
        };

        private string _scriptsRootPath;
        private string _outputTomlPath;
        private string _scriptPath;
        private string _additionalArguments = string.Empty;
        private bool _isBusy;
        private bool _closeAppAfterStart;

        protected TrainingConfigViewModelBase()
        {
            Sections = new ObservableCollection<TrainingSection>();
            LogLines = new ObservableCollection<string>();

            BrowseScriptsRootCommand = new RelayCommand(_ => BrowseScriptsRoot(), _ => !IsBusy);
            BrowseTomlCommand = new RelayCommand(_ => BrowseTomlFile(), _ => !IsBusy);
            BrowseScriptCommand = new RelayCommand(_ => BrowseScriptFile(), _ => !IsBusy);
            ResetDefaultsCommand = new RelayCommand(ResetParameters, () => !IsBusy);
            GenerateTomlCommand = new RelayCommand(async _ => await GenerateTomlAsync(), _ => !IsBusy);
            RunTrainingCommand = new RelayCommand(async _ => await RunTrainingAsync(), _ => !IsBusy);

            InitializeDefaults();
        }

        public ObservableCollection<TrainingSection> Sections { get; }
        public ObservableCollection<string> LogLines { get; }

        public string ScriptsRootPath
        {
            get => _scriptsRootPath;
            set
            {
                if (SetField(ref _scriptsRootPath, value))
                {
                    RefreshPathsFromRoot();
                }
            }
        }

        public string OutputTomlPath
        {
            get => _outputTomlPath;
            set => SetField(ref _outputTomlPath, value);
        }

        public string ScriptPath
        {
            get => _scriptPath;
            set => SetField(ref _scriptPath, value);
        }

        public string AdditionalArguments
        {
            get => _additionalArguments;
            set => SetField(ref _additionalArguments, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetField(ref _isBusy, value))
                {
                    (ResetDefaultsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (BrowseScriptsRootCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (BrowseTomlCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (BrowseScriptCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (GenerateTomlCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (RunTrainingCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CloseAppAfterStart
        {
            get => _closeAppAfterStart;
            set => SetField(ref _closeAppAfterStart, value);
        }

        public ICommand BrowseScriptsRootCommand { get; }
        public ICommand BrowseTomlCommand { get; }
        public ICommand BrowseScriptCommand { get; }
        public ICommand ResetDefaultsCommand { get; }
        public ICommand GenerateTomlCommand { get; }
        public ICommand RunTrainingCommand { get; }

        protected abstract string DefaultConfigRelativePath { get; }
        protected virtual string DefaultScriptRelativePath => "train_by_toml.ps1";
        protected abstract string ViewDisplayName { get; }

        protected abstract ObservableCollection<TrainingSection> BuildDefaultSections();

        private void InitializeDefaults()
        {
            var defaultRoot = GuessScriptsRoot();
            _scriptsRootPath = defaultRoot;
            RefreshPathsFromRoot();
            ResetParameters();
        }

        private void RefreshPathsFromRoot()
        {
            if (string.IsNullOrWhiteSpace(ScriptsRootPath))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(DefaultConfigRelativePath))
            {
                var configPath = Path.Combine(ScriptsRootPath, DefaultConfigRelativePath);
                OutputTomlPath = configPath;
            }

            if (!string.IsNullOrWhiteSpace(DefaultScriptRelativePath))
            {
                var scriptPath = Path.Combine(ScriptsRootPath, DefaultScriptRelativePath);
                ScriptPath = scriptPath;
            }
        }

        protected void ResetParameters()
        {
            Sections.Clear();
            foreach (var section in BuildDefaultSections())
            {
                Sections.Add(section);
            }

            AppendLog($"{ViewDisplayName}: 已加载默认参数");
        }

        private void BrowseScriptsRoot()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = LocalizationManager.Translate("选择 lora-scripts 根目录")
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                ScriptsRootPath = dialog.FileName;
            }
        }

        private void BrowseTomlFile()
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationManager.Translate("生成的 TOML 路径"),
                Filter = LocalizationManager.Translate("TOML 文件 (*.toml)|*.toml|所有文件 (*.*)|*.*"),
                FileName = Path.GetFileName(OutputTomlPath)
            };

            if (!string.IsNullOrWhiteSpace(OutputTomlPath))
            {
                var dir = Path.GetDirectoryName(OutputTomlPath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    dialog.InitialDirectory = dir;
                }
            }

            if (dialog.ShowDialog() == true)
            {
                OutputTomlPath = dialog.FileName;
            }
        }

        private void BrowseScriptFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.Translate("选择训练脚本"),
                Filter = LocalizationManager.Translate("PowerShell 脚本 (*.ps1)|*.ps1|所有文件 (*.*)|*.*")
            };

            if (!string.IsNullOrWhiteSpace(ScriptPath))
            {
                var dir = Path.GetDirectoryName(ScriptPath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    dialog.InitialDirectory = dir;
                }
            }

            if (dialog.ShowDialog() == true)
            {
                ScriptPath = dialog.FileName;
            }
        }

        private string GuessScriptsRoot()
        {
            var probable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lora-scripts");
            if (Directory.Exists(probable))
            {
                return probable;
            }

            probable = Path.GetFullPath("lora-scripts");
            if (Directory.Exists(probable))
            {
                return probable;
            }

            return string.Empty;
        }

        private async Task GenerateTomlAsync()
        {
            if (!EnsureReady())
            {
                return;
            }

            IsBusy = true;
            try
            {
                AppendLog("正在生成 TOML 配置...");
                await Task.Run(() => WriteTomlFile());
                AppendLog($"配置已保存到 {OutputTomlPath}");
            }
            catch (Exception ex)
            {
                AppendLog($"生成 TOML 失败: {ex.Message}");
                MessageBox.Show($"生成 TOML 失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RunTrainingAsync()
        {
            if (!EnsureReady())
            {
                return;
            }

            IsBusy = true;
            try
            {
                AppendLog("正在生成 TOML 配置...");
                await Task.Run(() => WriteTomlFile());
                AppendLog("启动训练脚本...");
                LaunchExternalTerminal();
                AppendLog("终端已启动，可在新的 PowerShell 窗口中查看进度。");
                if (CloseAppAfterStart)
                {
                    AppendLog("根据设置将关闭本软件。");
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Application.Current?.Shutdown();
                    });
                    return;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"训练执行失败: {ex.Message}");
                MessageBox.Show($"训练执行失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool EnsureReady()
        {
            if (string.IsNullOrWhiteSpace(ScriptsRootPath) || !Directory.Exists(ScriptsRootPath))
            {
                MessageBox.Show("请先选择 lora-scripts 根目录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (string.IsNullOrWhiteSpace(OutputTomlPath))
            {
                MessageBox.Show("请先设置 TOML 输出路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (string.IsNullOrWhiteSpace(ScriptPath) || !File.Exists(ScriptPath))
            {
                MessageBox.Show("请先设置有效的训练脚本路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        private void WriteTomlFile()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutputTomlPath) ?? ".");

            var sb = new StringBuilder();
            foreach (var section in Sections)
            {
                sb.AppendLine($"[{section.Name}]");
                foreach (var parameter in section.Parameters)
                {
                    sb.AppendLine($"{parameter.Key} = {FormatParameterValue(parameter)}");
                }
                sb.AppendLine();
            }

            File.WriteAllText(OutputTomlPath, sb.ToString(), Encoding.UTF8);
        }

        private string FormatParameterValue(TrainingParameter parameter)
        {
            return parameter.ValueKind switch
            {
                ParameterValueKind.Boolean => parameter.BoolValue ? "true" : "false",
                ParameterValueKind.Number => string.IsNullOrWhiteSpace(parameter.Value) ? "0" : parameter.Value,
                ParameterValueKind.Raw => parameter.Value ?? string.Empty,
                _ => $"\"{EscapeTomlString(parameter.Value ?? string.Empty)}\""
            };
        }

        private static string EscapeTomlString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private void LaunchExternalTerminal()
        {
            var command = BuildPowerShellCommand();

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -Command \"& {{ {command} }}\"",
                WorkingDirectory = ScriptsRootPath,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            Process.Start(psi);
        }

        private string BuildPowerShellCommand()
        {
            string EscapeSingleQuotes(string path) => path.Replace("'", "''");

            var builder = new StringBuilder();
            var venvActivate = Path.Combine(ScriptsRootPath ?? string.Empty, "venv", "Scripts", "Activate.ps1");
            if (File.Exists(venvActivate))
            {
                AppendLog("检测到虚拟环境，正在激活 ./venv ...");
                builder.Append($"& '{EscapeSingleQuotes(venvActivate)}'; ");
            }
            else
            {
                AppendLog("未找到 ./venv 虚拟环境，将直接运行脚本。");
            }

            builder.Append($"& '{EscapeSingleQuotes(ScriptPath)}'");
            if (!string.IsNullOrWhiteSpace(AdditionalArguments))
            {
                builder.Append(' ');
                builder.Append(AdditionalArguments);
            }

            return builder.ToString();
        }

        protected void AppendLog(string message)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                if (LogLines.Count > 500)
                {
                    LogLines.RemoveAt(0);
                }
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
