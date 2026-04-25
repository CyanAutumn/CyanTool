using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class GpuInfo
    {
        public string Name { get; set; }
        public int Id { get; set; }
        public override string ToString() => Name;
    }

    public class RealEsrganViewModel : INotifyPropertyChanged, IPipelineDirectoryNode, IPipelineRunnableViewModel
    {
        private const string ToolDisplayName = "图片超分";

        private string _inputDirectory;
        private string _outputDirectory;
        private string _selectedRealEsrganModel;
        private int _selectedScale;
        private GpuInfo _selectedGpu;
        private int _tileSize;
        private bool _shouldOpenOutputDirectory = true;
        private bool _shouldClearInputDirectoryAfterCompletion;
        private bool _isIdle = true;
        private string _selectedTool;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;

        private int _selectedWaifu2xNoiseLevel;
        private bool _waifu2xUsePhotoModel;
        private int _waifu2xCropSize;

        private readonly RealEsrganProcessor _realEsrganProcessor;
        private readonly Waifu2xProcessor _waifu2xProcessor;
        
        private const int MaxLogLines = 1000;

        public string InputDirectory
        {
            get => _inputDirectory;
            set
            {
                var previousInput = _inputDirectory;
                if (SetField(ref _inputDirectory, value))
                {
                    OutputDirectory = ToolOutputDirectoryDefaults.RefreshForInputChange(OutputDirectory, previousInput, value, ToolDisplayName);
                }
            }
        }
        public string OutputDirectory { get => _outputDirectory; set => SetField(ref _outputDirectory, value); }
        public string SelectedRealEsrganModel { get => _selectedRealEsrganModel; set => SetField(ref _selectedRealEsrganModel, value); }
        public ObservableCollection<string> RealEsrganModels { get; }
        public int SelectedScale { get => _selectedScale; set => SetField(ref _selectedScale, value); }
        public ObservableCollection<int> Scales { get; }
        public GpuInfo SelectedGpu { get => _selectedGpu; set => SetField(ref _selectedGpu, value); }
        public ObservableCollection<GpuInfo> Gpus { get; }
        public int TileSize { get => _tileSize; set => SetField(ref _tileSize, value); }
        public bool ShouldOpenOutputDirectory { get => _shouldOpenOutputDirectory; set => SetField(ref _shouldOpenOutputDirectory, value); }
        public bool ShouldClearInputDirectoryAfterCompletion { get => _shouldClearInputDirectoryAfterCompletion; set => SetField(ref _shouldClearInputDirectoryAfterCompletion, value); }
        public bool IsIdle { get => _isIdle; set => SetField(ref _isIdle, value); }
        public bool IsWorkflowMode { get => _isWorkflowMode; set => SetField(ref _isWorkflowMode, value); }
        public bool HasInheritedInputPath { get => _hasInheritedInputPath; set => SetField(ref _hasInheritedInputPath, value); }
        
        public ObservableCollection<string> LogLines { get; }

        public ObservableCollection<string> AvailableTools { get; }
        public string SelectedTool
        {
            get => _selectedTool;
            set
            {
                if (SetField(ref _selectedTool, value))
                {
                    OnPropertyChanged(nameof(IsRealEsrganSelected));
                    OnPropertyChanged(nameof(IsWaifu2xSelected));
                }
            }
        }
        public bool IsRealEsrganSelected => SelectedTool == "Real-ESRGAN";
        public bool IsWaifu2xSelected => SelectedTool == "waifu2x";

        public ObservableCollection<int> Waifu2xNoiseLevels { get; }
        public int SelectedWaifu2xNoiseLevel { get => _selectedWaifu2xNoiseLevel; set => SetField(ref _selectedWaifu2xNoiseLevel, value); }
        public bool Waifu2xUsePhotoModel { get => _waifu2xUsePhotoModel; set => SetField(ref _waifu2xUsePhotoModel, value); }
        public int Waifu2xCropSize { get => _waifu2xCropSize; set => SetField(ref _waifu2xCropSize, value); }
        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.GeneratedOutput;
        public string PipelineInputPath { get => InputDirectory ?? string.Empty; set => InputDirectory = value ?? string.Empty; }
        public string PipelineOutputPath { get => OutputDirectory ?? string.Empty; set => OutputDirectory = value ?? string.Empty; }

        public ICommand BrowseInputCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }
        public ICommand ClearLogCommand { get; }

        public RealEsrganViewModel()
        {
            _realEsrganProcessor = new RealEsrganProcessor();
            _waifu2xProcessor = new Waifu2xProcessor();
            
            LogLines = new ObservableCollection<string>();
            AvailableTools = new ObservableCollection<string> { "Real-ESRGAN", "waifu2x" };
            RealEsrganModels = new ObservableCollection<string>();
            Scales = new ObservableCollection<int> { 2, 3, 4 };
            Gpus = new ObservableCollection<GpuInfo>
            {
                new GpuInfo { Id = 0, Name = "GPU 0" },
                new GpuInfo { Id = 1, Name = "GPU 1" }
            };
            Waifu2xNoiseLevels = new ObservableCollection<int> { 0, 1, 2, 3 };

            try
            {
                var models = _realEsrganProcessor.GetAvailableModels();
                foreach (var model in models) RealEsrganModels.Add(model);
            }
            catch (Exception ex)
            {
                AddLog("加载Real-ESRGAN模型失败: " + ex.Message);
            }
            
            RestoreDefaults();

            BrowseInputCommand = new RelayCommand(_ => BrowseForFolder(path => InputDirectory = path));
            BrowseOutputCommand = new RelayCommand(_ => BrowseForFolder(path => OutputDirectory = path));
            StartCommand = new RelayCommand(async _ => await StartProcessing(), _ => IsIdle);
            RestoreDefaultsCommand = new RelayCommand(_ => RestoreDefaults());
            ClearLogCommand = new RelayCommand(_ => LogLines.Clear(), _ => IsIdle);
        }
        
        private void AddLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogLines.Add(message);
                if (LogLines.Count > MaxLogLines)
                {
                    LogLines.RemoveAt(0);
                }
            });
        }

        private void RestoreDefaults()
        {
            SelectedTool = AvailableTools.FirstOrDefault();
            SelectedRealEsrganModel = RealEsrganModels.FirstOrDefault(m => m.Contains("anime")) ?? RealEsrganModels.FirstOrDefault();
            SelectedScale = Scales.LastOrDefault();
            SelectedGpu = Gpus.FirstOrDefault();
            TileSize = 0;
            SelectedWaifu2xNoiseLevel = Waifu2xNoiseLevels.FirstOrDefault();
            Waifu2xUsePhotoModel = false;
            Waifu2xCropSize = 0;
            AddLog("默认设置已恢复。");
        }

        private void BrowseForFolder(Action<string> setPathAction)
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                setPathAction(dialog.FileName);
            }
        }

        public Task RunFromPipelineAsync(PipelineContext context, CancellationToken token)
        {
            return ExecuteProcessingAsync(
                allowOpenOutputDirectory: false,
                rethrowErrors: true,
                pipelineLog: message => context.AppendLog($"[{ToolDisplayName}] {message}"),
                token);
        }

        private Task StartProcessing()
        {
            return ExecuteProcessingAsync(
                allowOpenOutputDirectory: !IsWorkflowMode && ShouldOpenOutputDirectory,
                rethrowErrors: false,
                pipelineLog: null,
                CancellationToken.None);
        }

        private async Task ExecuteProcessingAsync(bool allowOpenOutputDirectory, bool rethrowErrors, Action<string>? pipelineLog, CancellationToken token)
        {
            OutputDirectory = ToolOutputDirectoryDefaults.FillWhenEmpty(OutputDirectory, InputDirectory, ToolDisplayName);
            IsIdle = false;
            (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            LogLines.Clear();
            var progress = new Progress<string>(message =>
            {
                AddLog(message);
                pipelineLog?.Invoke(message);
            });

            bool success = false;
            try
            {
                token.ThrowIfCancellationRequested();
                if (SelectedTool == "Real-ESRGAN")
                {
                    await _realEsrganProcessor.RunAsync(InputDirectory, OutputDirectory, SelectedRealEsrganModel, SelectedScale, SelectedGpu.Id, TileSize, progress);
                }
                else if (SelectedTool == "waifu2x")
                {
                    await _waifu2xProcessor.RunAsync(InputDirectory, OutputDirectory, SelectedScale, SelectedWaifu2xNoiseLevel, Waifu2xUsePhotoModel, Waifu2xCropSize, progress);
                }
                success = true;
                AddLog("处理完成。");
                pipelineLog?.Invoke("处理完成。");
            }
            catch (Exception ex)
            {
                string errorMessage = "发生错误: " + ex.Message;
                AddLog(errorMessage);
                pipelineLog?.Invoke(errorMessage);
                if (rethrowErrors)
                {
                    throw;
                }
            }
            finally
            {
                IsIdle = true;
                (ClearLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
                if (success)
                {
                    TryClearInputDirectoryAfterCompletion(pipelineLog);
                }

                if (success && allowOpenOutputDirectory && !string.IsNullOrWhiteSpace(OutputDirectory) && Directory.Exists(OutputDirectory))
                {
                    Process.Start("explorer.exe", OutputDirectory);
                }
            }
        }

        private void TryClearInputDirectoryAfterCompletion(Action<string>? pipelineLog)
        {
            if (!ShouldClearInputDirectoryAfterCompletion)
            {
                return;
            }

            InputDirectoryCleanupResult result = InputDirectoryCleanupHelper.TryClearContents(InputDirectory, new[] { OutputDirectory });
            AddLog(result.Message);
            pipelineLog?.Invoke(result.Message);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
