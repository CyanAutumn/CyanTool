using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class TaggerModel
    {
        public string DisplayName { get; set; }
        public string ModelUrl { get; set; }
        public string CsvUrl { get; set; }
        public int InputSize { get; set; }
    }

    public class TaggingViewModel : INotifyPropertyChanged
    {
        private string _inputDirectory;
        private float _generalThreshold = 0.35f;
        private float _characterThreshold = 0.85f;
        private string _logOutput;
        private bool _isIdle = true;
        private double _progressValue;
        private TaggerModel _selectedTaggerModel;
        private bool _isInitialized = false;

        private readonly TaggingProcessor _taggingProcessor;
        private const int MaxLogLines = 200;
        private readonly List<string> _logLines = new List<string>();

        public string InputDirectory { get => _inputDirectory; set => SetField(ref _inputDirectory, value); }
        public float GeneralThreshold { get => _generalThreshold; set => SetField(ref _generalThreshold, value); }
        public float CharacterThreshold { get => _characterThreshold; set => SetField(ref _characterThreshold, value); }
        public string LogOutput { get => _logOutput; set => SetField(ref _logOutput, value); }
        public bool IsIdle { get => _isIdle; set => SetField(ref _isIdle, value); }
        public double ProgressValue { get => _progressValue; set => SetField(ref _progressValue, value); }

        public ObservableCollection<TaggerModel> TaggerModels { get; }
        public TaggerModel SelectedTaggerModel { get => _selectedTaggerModel; set => SetField(ref _selectedTaggerModel, value); }

        public ICommand BrowseInputCommand { get; }
        public ICommand StartTaggingCommand { get; }

        public TaggingViewModel()
        {
            _taggingProcessor = new TaggingProcessor();

            TaggerModels = new ObservableCollection<TaggerModel>
            {
                new TaggerModel { DisplayName = "wd-v1-4-swinv2-tagger-v2", ModelUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-swinv2-tagger-v2/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-swinv2-tagger-v2/resolve/main/selected_tags.csv", InputSize = 448 },
                new TaggerModel { DisplayName = "wd-v1-4-convnext-tagger-v2", ModelUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-convnext-tagger-v2/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-convnext-tagger-v2/resolve/main/selected_tags.csv", InputSize = 448 },
                new TaggerModel { DisplayName = "wd-v1-4-moat-tagger-v2", ModelUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-moat-tagger-v2/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-moat-tagger-v2/resolve/main/selected_tags.csv", InputSize = 448 },
                new TaggerModel { DisplayName = "wd-v1-4-convnextv2-tagger-v2", ModelUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-convnextv2-tagger-v2/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-convnextv2-tagger-v2/resolve/main/selected_tags.csv", InputSize = 448 },
                new TaggerModel { DisplayName = "wd-v1-4-vit-tagger-v2", ModelUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-vit-tagger-v2/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/SmilingWolf/wd-v1-4-vit-tagger-v2/resolve/main/selected_tags.csv", InputSize = 448 },
                new TaggerModel { DisplayName = "wd-swinv2-tagger-v3", ModelUrl = "https://huggingface.co/SmilingWolf/wd-swinv2-tagger-v3/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/SmilingWolf/wd-swinv2-tagger-v3/resolve/main/selected_tags.csv", InputSize = 480 },
                new TaggerModel { DisplayName = "wd-convnext-tagger-v3", ModelUrl = "https://huggingface.co/SmilingWolf/wd-convnext-tagger-v3/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/SmilingWolf/wd-convnext-tagger-v3/resolve/main/selected_tags.csv", InputSize = 480 },
                new TaggerModel { DisplayName = "wd-vit-tagger-v3", ModelUrl = "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/SmilingWolf/wd-vit-tagger-v3/resolve/main/selected_tags.csv", InputSize = 480 },
                new TaggerModel { DisplayName = "wd-vit-large-tagger-v3", ModelUrl = "https://huggingface.co/SmilingWolf/wd-vit-large-tagger-v3/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/SmilingWolf/wd-vit-large-tagger-v3/resolve/main/selected_tags.csv", InputSize = 480 },
                new TaggerModel { DisplayName = "wd-eva02-large-tagger-v3", ModelUrl = "https://huggingface.co/SmilingWolf/wd-eva02-large-tagger-v3/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/SmilingWolf/wd-eva02-large-tagger-v3/resolve/main/selected_tags.csv", InputSize = 480 },
                new TaggerModel { DisplayName = "idolsankaku-swinv2-tagger-v1", ModelUrl = "https://huggingface.co/deepghs/idolsankaku-swinv2-tagger-v1/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/deepghs/idolsankaku-swinv2-tagger-v1/resolve/main/selected_tags.csv", InputSize = 448 },
                new TaggerModel { DisplayName = "idolsankaku-eva02-large-tagger-v1", ModelUrl = "https://huggingface.co/deepghs/idolsankaku-eva02-large-tagger-v1/resolve/main/model.onnx", CsvUrl = "https://huggingface.co/deepghs/idolsankaku-eva02-large-tagger-v1/resolve/main/selected_tags.csv", InputSize = 480 }
            };
            SelectedTaggerModel = TaggerModels.FirstOrDefault();

            BrowseInputCommand = new RelayCommand(_ => BrowseForFolder(path => InputDirectory = path));
            StartTaggingCommand = new RelayCommand(async _ => await StartProcessing(), _ => IsIdle && !string.IsNullOrWhiteSpace(InputDirectory));
        }

        private void AddLog(string message)
        {
            if (message.StartsWith("Downloading"))
            {
                int index = _logLines.FindIndex(line => line.StartsWith("Downloading"));
                if (index != -1)
                {
                    _logLines[index] = message;
                }
                else
                {
                    _logLines.Add(message);
                }
            }
            else
            {
                _logLines.Add(message);
            }

            while (_logLines.Count > MaxLogLines)
            {
                _logLines.RemoveAt(0);
            }

            LogOutput = string.Join("\n", _logLines);
        }

        private void BrowseForFolder(Action<string> setPathAction)
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                setPathAction(dialog.FileName);
            }
        }

        private async Task StartProcessing()
        {
            IsIdle = false;
            _logLines.Clear();
            LogOutput = "";
            ProgressValue = 0;

            var progress = new Progress<string>(update =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    AddLog(update);
                    if (update.StartsWith("Processed"))
                    {
                        try
                        {
                            var parts = update.Split('[', '/', ']');
                            if (parts.Length > 2)
                            {
                                double current = double.Parse(parts[1]);
                                double total = double.Parse(parts[2]);
                                ProgressValue = (current / total) * 100;
                            }
                        }
                        catch {                             }
                    }
                });
            });

            try
            {
                await Task.Run(async () =>
                {
                    await _taggingProcessor.InitializeAsync(SelectedTaggerModel.ModelUrl, SelectedTaggerModel.CsvUrl, SelectedTaggerModel.DisplayName, progress);
                    await _taggingProcessor.ProcessDirectoryAsync(InputDirectory, GeneralThreshold, CharacterThreshold, SelectedTaggerModel.InputSize, progress);
                });
            }
            catch (Exception ex)
            {
                AddLog("An error occurred: " + ex.Message);
            }
            finally
            {
                IsIdle = true;
                ProgressValue = 100;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            CommandManager.InvalidateRequerySuggested();
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