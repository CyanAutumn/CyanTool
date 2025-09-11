using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows.Input;
using CyanTools.Utils;

namespace CyanTools.ViewModel
{
    public class GaussianBlurViewModel : INotifyPropertyChanged
    {
        private string _inputPath;
        private string _outputPath;
        private int _blurRadius = 5;
        private string _statusText;

        public string InputPath
        {
            get => _inputPath;
            set { _inputPath = value; OnPropertyChanged(); }
        }

        public string OutputPath
        {
            get => _outputPath;
            set { _outputPath = value; OnPropertyChanged(); }
        }

        public int BlurRadius
        {
            get => _blurRadius;
            set { _blurRadius = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public ICommand SelectInputCommand { get; }
        public ICommand SelectOutputCommand { get; }
        public ICommand ProcessCommand { get; }

        public GaussianBlurViewModel()
        {
            SelectInputCommand = new RelayCommand(SelectInputFolder);
            SelectOutputCommand = new RelayCommand(SelectOutputFolder);
            ProcessCommand = new RelayCommand(async () => await ProcessImages());
        }

        private void SelectInputFolder()
        {
            var dialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "文件夹选择."
            };
            if (dialog.ShowDialog() == true)
            {
                InputPath = Path.GetDirectoryName(dialog.FileName);
            }
        }

        private void SelectOutputFolder()
        {
            var dialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "文件夹选择."
            };
            if (dialog.ShowDialog() == true)
            {
                OutputPath = Path.GetDirectoryName(dialog.FileName);
            }
        }

        private async Task ProcessImages()
        {
            if (string.IsNullOrEmpty(InputPath) || string.IsNullOrEmpty(OutputPath))
            {
                StatusText = "请选择输入和输出文件夹。";
                return;
            }

            if (!Directory.Exists(InputPath))
            {
                StatusText = "输入文件夹不存在。";
                return;
            }

            if (!Directory.Exists(OutputPath))
            {
                Directory.CreateDirectory(OutputPath);
            }

            StatusText = "正在处理...";
            try
            {
                await Task.Run(() =>
                {
                    var files = Directory.GetFiles(InputPath, "*.*", SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        var extension = Path.GetExtension(file).ToLower();
                        if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp")
                        {
                            var outputFileName = Path.Combine(OutputPath, Path.GetFileName(file));
                            ImageProcessor.ApplyGaussianBlur(file, outputFileName, BlurRadius);
                        }
                    }
                });
                StatusText = "处理完成。";
            }
            catch (Exception ex)
            {
                StatusText = $"处理失败: {ex.Message}";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}