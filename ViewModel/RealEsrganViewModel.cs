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
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Windows.Input;
using CyanTools.Utils;

namespace CyanTools.ViewModel
{
    public class GpuInfo
    {
        public string Name { get; set; }
        public int Id { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public class RealEsrganViewModel : INotifyPropertyChanged
    {
        private string _inputDirectory;
        private string _outputDirectory;
        private string _selectedModel;
        private int _selectedScale;
        private GpuInfo _selectedGpu;
        private int _tileSize;
        private bool _shouldOpenOutputDirectory = true;
        private string _logOutput;
        private bool _isIdle = true;
        private readonly RealEsrganProcessor _processor;

        public string InputDirectory { get => _inputDirectory; set => SetField(ref _inputDirectory, value); }
        public string OutputDirectory { get => _outputDirectory; set => SetField(ref _outputDirectory, value); }
        public string SelectedModel { get => _selectedModel; set => SetField(ref _selectedModel, value); }
        public int SelectedScale { get => _selectedScale; set => SetField(ref _selectedScale, value); }
        public GpuInfo SelectedGpu { get => _selectedGpu; set => SetField(ref _selectedGpu, value); }
        public int TileSize { get => _tileSize; set => SetField(ref _tileSize, value); }
        public bool ShouldOpenOutputDirectory { get => _shouldOpenOutputDirectory; set => SetField(ref _shouldOpenOutputDirectory, value); }
        public string LogOutput { get => _logOutput; set => SetField(ref _logOutput, value); }
        public bool IsIdle { get => _isIdle; set => SetField(ref _isIdle, value); }

        public ObservableCollection<string> Models { get; }
        public ObservableCollection<int> Scales { get; }
        public ObservableCollection<GpuInfo> Gpus { get; }

        public ICommand BrowseInputCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }

        public RealEsrganViewModel()
        {
            _processor = new RealEsrganProcessor();
            Models = new ObservableCollection<string>();
            Scales = new ObservableCollection<int> { 2, 3, 4 };
            Gpus = new ObservableCollection<GpuInfo>
            {
                new GpuInfo { Id = 0, Name = "GPU 0: NVIDIA" },
                new GpuInfo { Id = 1, Name = "GPU 1: AMD" }
            };

            try
            {
                var models = _processor.GetAvailableModels();
                foreach (var model in models) Models.Add(model);
            }
            catch (Exception ex)
            {
                LogOutput += "加载模型失败: " + ex.Message + "\n";
            }
            
            RestoreDefaults();

            BrowseInputCommand = new RelayCommand(_ => BrowseForFolder(path => InputDirectory = path));
            BrowseOutputCommand = new RelayCommand(_ => BrowseForFolder(path => OutputDirectory = path));
            StartCommand = new RelayCommand(async _ => await StartProcessing(), _ => IsIdle);
            RestoreDefaultsCommand = new RelayCommand(_ => RestoreDefaults());
        }

        private void RestoreDefaults()
        {
            SelectedModel = Models.FirstOrDefault(m => m.Contains("anime")) ?? Models.FirstOrDefault();
            SelectedScale = Scales.LastOrDefault();
            SelectedGpu = Gpus.FirstOrDefault();
            TileSize = 0;
        }

        private void BrowseForFolder(Action<string> setPathAction)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                setPathAction(dialog.FileName);
            }
        }

        private async Task StartProcessing()
        {
            IsIdle = false;
            var logBuilder = new StringBuilder();
            LogOutput = "";
            var progress = new Progress<string>(update =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    logBuilder.AppendLine(update);
                    LogOutput = logBuilder.ToString();
                });
            });

            bool success = false;
            try
            {
                await _processor.RunAsync(InputDirectory, OutputDirectory, SelectedModel, SelectedScale, SelectedGpu.Id, TileSize, progress);
                success = true;
            }
            catch (Exception ex)
            {
                ((IProgress<string>)progress).Report("发生错误: " + ex.Message);
            }
            finally
            {
                IsIdle = true;
                if (success && ShouldOpenOutputDirectory && !string.IsNullOrWhiteSpace(OutputDirectory) && Directory.Exists(OutputDirectory))
                {
                    Process.Start("explorer.exe", OutputDirectory);
                }
            }
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