using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using CyanTools.Model;
using CyanTools.Utils;

namespace CyanTools.ViewModel
{
    public class ToolLLMViewModel : INotifyPropertyChanged
    {
        private readonly ConfigManager _configManager;
        private readonly ImageProcessor _imageProcessor;

        private string _mainInput = "";
        private string _path1 = "";
        private string _path2 = "";
        private string _path3 = "";
        private string _path4 = "";
        private string _path5 = "";
        private string _logOutput = "";
        private bool _isRunEnabled = true;
        private string _newConfigName;
        private ConfigurationModel _selectedConfig;
        private string _workingDirectory = "";
        private ModelData _selectedModel;

        public string MainInput { get => _mainInput; set => SetField(ref _mainInput, value); }
        public string WorkingDirectory { get => _workingDirectory; set => SetField(ref _workingDirectory, value); }
        public string Path1 { get => _path1; set => SetField(ref _path1, value); }
        public string Path2 { get => _path2; set => SetField(ref _path2, value); }
        public string Path3 { get => _path3; set => SetField(ref _path3, value); }
        public string Path4 { get => _path4; set => SetField(ref _path4, value); }
        public string Path5 { get => _path5; set => SetField(ref _path5, value); }
        public string LogOutput { get => _logOutput; set => SetField(ref _logOutput, value); }
        public bool IsRunEnabled { get => _isRunEnabled; set => SetField(ref _isRunEnabled, value); }

        public ObservableCollection<ConfigurationModel> Configs { get; set; }
        public ObservableCollection<ModelData> ModelsList { get; set; }

        public string NewConfigName { get => _newConfigName; set => SetField(ref _newConfigName, value); }

        public ConfigurationModel SelectedConfig
        {
            get => _selectedConfig;
            set
            {
                SetField(ref _selectedConfig, value);
                if (_selectedConfig != null) LoadConfiguration(_selectedConfig);
            }
        }

        public ModelData SelectedModel
        {
            get => _selectedModel;
            set => SetField(ref _selectedModel, value);
        }

        public ICommand RunCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand DeleteConfigCommand { get; }
        public ICommand RefreshModelsCommand { get; }
        public ICommand SelectFolderCommand { get; }

        public ToolLLMViewModel()
        {
            _configManager = new ConfigManager();
            _imageProcessor = new ImageProcessor();
            Configs = new ObservableCollection<ConfigurationModel>();
            ModelsList = new ObservableCollection<ModelData>();

            RunCommand = new RelayCommand(async _ => await ExecuteRunCommand(), _ => IsRunEnabled && !string.IsNullOrWhiteSpace(MainInput));
            SaveConfigCommand = new RelayCommand(_ => ExecuteSaveConfigCommand(), _ => !string.IsNullOrWhiteSpace(NewConfigName));
            DeleteConfigCommand = new RelayCommand(_ => ExecuteDeleteConfigCommand(), _ => SelectedConfig != null);
            RefreshModelsCommand = new RelayCommand(async _ => await ExecuteRefreshModelsCommand());
            SelectFolderCommand = new RelayCommand(param => ExecuteSelectFolder(param as string));

            LoadAllConfigs();
            ExecuteRefreshModelsCommand();
        }

        private void ExecuteSelectFolder(string propertyName)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    switch (propertyName)
                    {
                        case "WorkingDirectory":
                            WorkingDirectory = dialog.SelectedPath;
                            break;
                        case "Path1":
                            Path1 = dialog.SelectedPath;
                            break;
                        case "Path2":
                            Path2 = dialog.SelectedPath;
                            break;
                        case "Path3":
                            Path3 = dialog.SelectedPath;
                            break;
                        case "Path4":
                            Path4 = dialog.SelectedPath;
                            break;
                        case "Path5":
                            Path5 = dialog.SelectedPath;
                            break;
                    }
                }
            }
        }

        private async Task ExecuteRefreshModelsCommand()
        {
            LogOutput += "\nRefreshing models list...";
            ModelsList.Clear();
            var models = await _imageProcessor.GetLoadedModelsAsync();
            if (models.Any())
            {
                foreach (var model in models)
                {
                    ModelsList.Add(model);
                }
                SelectedModel = ModelsList.FirstOrDefault();
                LogOutput += $"\nFound {models.Count} models.";
            }
            else
            {
                LogOutput += "\nCould not find any loaded models. Make sure LM Studio is running.";
            }
        }

        private void LoadAllConfigs()
        {
            Configs.Clear();
            var loadedConfigs = _configManager.LoadAllConfigurations();
            foreach (var config in loadedConfigs)
            {
                Configs.Add(config);
            }
        }

        private void LoadConfiguration(ConfigurationModel config)
        {
            MainInput = config.Prompt;
            WorkingDirectory = config.WorkingDirectory;
            Path1 = config.Paths.Count > 0 ? config.Paths[0] : "";
            Path2 = config.Paths.Count > 1 ? config.Paths[1] : "";
            Path3 = config.Paths.Count > 2 ? config.Paths[2] : "";
            Path4 = config.Paths.Count > 3 ? config.Paths[3] : "";
            Path5 = config.Paths.Count > 4 ? config.Paths[4] : "";
            NewConfigName = config.Name;
        }

        private void ExecuteSaveConfigCommand()
        {
            var config = new ConfigurationModel
            {
                Name = NewConfigName,
                Prompt = MainInput,
                WorkingDirectory = WorkingDirectory,
                Paths = new List<string> { Path1, Path2, Path3, Path4, Path5 }
            };

            try
            {
                _configManager.SaveConfiguration(config);
                LoadAllConfigs();
                SelectedConfig = Configs.FirstOrDefault(c => c.Name == config.Name);
            }
            catch (Exception ex)
            {
                LogOutput += $"\nError saving configuration: {ex.Message}";
            }
        }

        private void ExecuteDeleteConfigCommand()
        {
            if (SelectedConfig == null) return;

            try
            {
                _configManager.DeleteConfiguration(SelectedConfig.Name);
                var oldSelection = SelectedConfig.Name;
                LoadAllConfigs();
                LogOutput += $"\nDeleted configuration: {oldSelection}";
            }
            catch (Exception ex)
            {
                LogOutput += $"\nError deleting configuration: {ex.Message}";
            }
        }

        private async Task ExecuteRunCommand()
        {
            IsRunEnabled = false;
            var logBuilder = new StringBuilder(LogOutput + "\n");
            Action<string> reportAction = update =>
            {
                logBuilder.AppendLine(update);
                LogOutput = logBuilder.ToString();
            };
            var progress = new Progress<string>(reportAction);

            try
            {
                var destPaths = new List<string> { Path1, Path2, Path3, Path4, Path5 };
                await _imageProcessor.ProcessImagesAsync(MainInput, WorkingDirectory, destPaths, progress);
            }
            catch (Exception ex)
            {
                reportAction($"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                IsRunEnabled = true;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
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
