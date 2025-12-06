using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CyanTools.Model;
using CyanTools.Utils;
using Microsoft.Win32;

namespace CyanTools.ViewModel
{
    public class GeminiImageGenerationViewModel : INotifyPropertyChanged
    {
        private const int MaxInlineBytes = 12 * 1024 * 1024;
        private const int MaxLogEntries = 400;
        private const int MaxQueueSize = 50;
        private const int MaxInputImages = 10;

        private readonly Random _random = new();

        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        private readonly GeminiImageClient _geminiClient;

        private string _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty;
        private string _selectedModel = "gemini-2.5-flash-image";
        private string _prompt = string.Empty;
        private ImageSource? _resultImageSource;
        private byte[]? _resultImageBytes;
        private string _statusMessage = "等待输入";
        private bool _isBusy;
        private double _temperature = 0.5;
        private int _delayMinSeconds = 1;
        private int _delayMaxSeconds = 5;
        private int _batchCount = 1;
        private ImageSizeOption _selectedImageSizeOption;
        private AspectRatioOption _selectedAspectRatioOption;
        private readonly Queue<GeminiGenerationTask> _taskQueue = new();
        private bool _isProcessingQueue;
        private string _queueStatusText = "当前队列任务0/0";
        private int _queueCurrent;
        private int _queueTotal;
        private string? _lastOutputFolderPath;
        private readonly ObservableCollection<string> _availableConfigNames = new();
        private string _selectedConfigName = string.Empty;
        private bool _isApplyingConfig;
        private string? _imageSizeValidationError;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<string> AvailableModels { get; } = new()
        {
            "gemini-2.5-flash-image",
            "gemini-3-pro-image-preview"
        };

        public ObservableCollection<ImageSizeOption> ImageSizeOptions { get; } = new()
        {
            new("1K (默认)", "1K"),
            new("2K", "2K"),
            new("4K", "4K")
        };

        public ObservableCollection<AspectRatioOption> AspectRatioOptions { get; } = new()
        {
            new("不指定（按模型默认）", null),
            new("1:1", "1:1"),
            new("2:3", "2:3"),
            new("3:2", "3:2"),
            new("3:4", "3:4"),
            new("4:3", "4:3"),
            new("4:5", "4:5"),
            new("5:4", "5:4"),
            new("9:16", "9:16"),
            new("16:9", "16:9"),
            new("21:9", "21:9")
        };

        public ObservableCollection<string> LogLines { get; } = new();
        public ObservableCollection<InputImageItem> InputImages { get; } = new();
        public ObservableCollection<string> AvailableConfigNames => _availableConfigNames;

        public string ApiKey
        {
            get => _apiKey;
            set
            {
                if (_apiKey == value)
                {
                    return;
                }
                _apiKey = value;
                OnPropertyChanged(nameof(ApiKey));
                RaiseCanExecuteChanged();
            }
        }

        public string SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (_selectedModel == value)
                {
                    return;
                }
                _selectedModel = value;
                OnPropertyChanged(nameof(SelectedModel));
            }
        }

        public string Prompt
        {
            get => _prompt;
            set
            {
                if (_prompt == value)
                {
                    return;
                }
                _prompt = value;
                OnPropertyChanged(nameof(Prompt));
                RaiseCanExecuteChanged();
            }
        }

        public ImageSource? ResultImageSource
        {
            get => _resultImageSource;
            private set
            {
                _resultImageSource = value;
                OnPropertyChanged(nameof(ResultImageSource));
                OnPropertyChanged(nameof(HasResultImage));
            }
        }

        public bool HasInputImages => InputImages.Count > 0;
        public bool HasResultImage => ResultImageSource != null;
        public bool HasImageSizeValidationError => !string.IsNullOrWhiteSpace(_imageSizeValidationError);
        public string? ImageSizeValidationError
        {
            get => _imageSizeValidationError;
            private set
            {
                if (_imageSizeValidationError == value)
                {
                    return;
                }
                _imageSizeValidationError = value;
                OnPropertyChanged(nameof(ImageSizeValidationError));
                OnPropertyChanged(nameof(HasImageSizeValidationError));
                RaiseCanExecuteChanged();
            }
        }
        public string QueueStatusText
        {
            get => _queueStatusText;
            private set
            {
                if (_queueStatusText == value)
                {
                    return;
                }
                _queueStatusText = value;
                OnPropertyChanged(nameof(QueueStatusText));
            }
        }

        public string SelectedConfigName
        {
            get => _selectedConfigName;
            set
            {
                string newValue = value ?? string.Empty;
                if (_selectedConfigName == newValue)
                {
                    return;
                }
                _selectedConfigName = newValue;
                OnPropertyChanged(nameof(SelectedConfigName));
                RaiseConfigCommandsCanExecute();

                if (!_isApplyingConfig)
                {
                    TryLoadConfigByName(newValue);
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
                RaiseCanExecuteChanged();
            }
        }

        public double Temperature
        {
            get => _temperature;
            set
            {
                if (Math.Abs(_temperature - value) < 0.0001)
                {
                    return;
                }
                _temperature = Math.Clamp(value, 0.0, 1.0);
                OnPropertyChanged(nameof(Temperature));
            }
        }

        public ImageSizeOption SelectedImageSizeOption
        {
            get => _selectedImageSizeOption;
            set
            {
                if (value == null || Equals(_selectedImageSizeOption, value))
                {
                    return;
                }
                _selectedImageSizeOption = value;
                OnPropertyChanged(nameof(SelectedImageSizeOption));

                ValidateImageSizeInput();
            }
        }

        public AspectRatioOption SelectedAspectRatioOption
        {
            get => _selectedAspectRatioOption;
            set
            {
                if (value == null || Equals(_selectedAspectRatioOption, value))
                {
                    return;
                }

                _selectedAspectRatioOption = value;
                OnPropertyChanged(nameof(SelectedAspectRatioOption));
            }
        }

        public int DelayMinSeconds
        {
            get => _delayMinSeconds;
            set
            {
                int newValue = Math.Max(0, value);
                if (_delayMinSeconds == newValue)
                {
                    return;
                }
                _delayMinSeconds = newValue;
                if (_delayMinSeconds > _delayMaxSeconds)
                {
                    _delayMaxSeconds = _delayMinSeconds;
                    OnPropertyChanged(nameof(DelayMaxSeconds));
                }
                OnPropertyChanged(nameof(DelayMinSeconds));
            }
        }

        public int DelayMaxSeconds
        {
            get => _delayMaxSeconds;
            set
            {
                int newValue = Math.Max(0, value);
                if (_delayMaxSeconds == newValue)
                {
                    return;
                }
                _delayMaxSeconds = newValue;
                if (_delayMaxSeconds < _delayMinSeconds)
                {
                    _delayMinSeconds = _delayMaxSeconds;
                    OnPropertyChanged(nameof(DelayMinSeconds));
                }
                OnPropertyChanged(nameof(DelayMaxSeconds));
            }
        }

        public int BatchCount
        {
            get => _batchCount;
            set
            {
                int newValue = Math.Clamp(value, 1, MaxInputImages);
                if (_batchCount == newValue)
                {
                    return;
                }
                _batchCount = newValue;
                OnPropertyChanged(nameof(BatchCount));
            }
        }

        public ICommand SelectImageCommand { get; }
        public ICommand ClearInputCommand { get; }
        public ICommand RemoveImageCommand { get; }
        public ICommand GenerateCommand { get; }
        public ICommand BatchGenerateCommand { get; }
        public ICommand ClearResultCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand DeleteConfigCommand { get; }

        public GeminiImageGenerationViewModel()
        {
            _geminiClient = new GeminiImageClient(_serializerOptions);

            SelectImageCommand = new RelayCommand(SelectInputImage);
            ClearInputCommand = new RelayCommand(ClearInputImages, () => InputImages.Count > 0);
            RemoveImageCommand = new RelayCommand(p => RemoveSingleImage(p as InputImageItem), p => p is InputImageItem);
            GenerateCommand = new RelayCommand(() => RunSafeAsync(EnqueueAndProcessAsync), CanGenerate);
            BatchGenerateCommand = new RelayCommand(() => RunSafeAsync(BatchGenerateAsync), CanGenerate);
            ClearResultCommand = new RelayCommand(ClearResult);
            SaveConfigCommand = new RelayCommand(SaveConfig, CanSaveConfig);
            DeleteConfigCommand = new RelayCommand(DeleteConfig, CanDeleteConfig);

            InputImages.CollectionChanged += OnInputImagesChanged;
            _selectedImageSizeOption = ImageSizeOptions.First();
            _selectedAspectRatioOption = AspectRatioOptions.First();
            LoadAvailableConfigs();
            ValidateImageSizeInput();
        }

        public void SetInputImage(string? path)
        {
            AddInputImages(new[] { path });
        }

        public void AddInputImages(IEnumerable<string?>? paths)
        {
            if (paths == null)
            {
                return;
            }

            int added = 0;
            foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                if (InputImages.Count >= MaxInputImages)
                {
                    AppendLog($"已达到最多 {MaxInputImages} 张素材的限制，忽略更多图片。");
                    break;
                }

                if (!File.Exists(path!))
                {
                    AppendLog($"文件不存在，已跳过: {path}");
                    continue;
                }

                if (InputImages.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!IsSupportedImage(path!))
                {
                    AppendLog($"不支持的图片格式，已跳过: {Path.GetFileName(path)}");
                    continue;
                }

                if (TryCreateInputImageItem(path!, out var item, out var error))
                {
                    InputImages.Add(item);
                    added++;
                }
                else if (!string.IsNullOrWhiteSpace(error))
                {
                    AppendLog($"加载 {Path.GetFileName(path)} 失败: {error}");
                }
            }

            if (added > 0)
            {
                StatusMessage = $"已添加 {added} 张素材图片。";
            }
            else
            {
                StatusMessage = "未添加新的素材图片。";
            }
        }

        private void SelectInputImage()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                AddInputImages(dialog.FileNames);
            }
        }

        private async Task EnqueueAndProcessAsync()
        {
            if (!TryValidateInputs(out var imageSize))
            {
                return;
            }

            var task = BuildTaskSnapshot(InputImages.ToList(), imageSize, null);
            if (task == null)
            {
                return;
            }

            if (!TryEnqueueTasks(new[] { task }))
            {
                return;
            }

            AppendLog($"已加入队列: 模型 {task.SelectedModel}，分辨率 {task.ImageSizeDisplay}，长宽比 {task.AspectRatioDisplay}，素材 {task.Images.Count} 张。");
            UpdateQueueStatus(_isProcessingQueue ? _queueCurrent : 0, _isProcessingQueue ? _queueCurrent + _taskQueue.Count : _taskQueue.Count);
            StatusMessage = $"已加入队列，当前队列 {_taskQueue.Count} 个任务。";
            await ProcessQueueAsync();
        }

        private bool TryValidateInputs(out GeminiImageSize? imageSize)
        {
            imageSize = null;
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                StatusMessage = "请先输入 API Key。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Prompt))
            {
                StatusMessage = "请填写提示词。";
                return false;
            }

            if (InputImages.Count == 0)
            {
                StatusMessage = "请先选择素材图片。";
                return false;
            }

            if (!TryResolveImageSize(out imageSize, out var error))
            {
                ImageSizeValidationError = error;
                StatusMessage = error ?? "图片大小设置无效。";
                return false;
            }

            ImageSizeValidationError = null;
            return true;
        }

        private void ClearInputImages()
        {
            InputImages.Clear();
            StatusMessage = "已清空素材图片。";
        }

        private void RemoveSingleImage(InputImageItem? item)
        {
            if (item == null)
            {
                return;
            }

            if (InputImages.Remove(item))
            {
                StatusMessage = $"已移除 {item.FileName}";
            }
        }

        private void ClearResult()
        {
            _resultImageBytes = null;
            ResultImageSource = null;
            StatusMessage = "已清空结果。";
            RaiseCanExecuteChanged();
        }

        private bool CanGenerate()
        {
            return !string.IsNullOrWhiteSpace(ApiKey) &&
                   !string.IsNullOrWhiteSpace(Prompt) &&
                   InputImages.Count > 0 &&
                   string.IsNullOrWhiteSpace(ImageSizeValidationError);
        }

        private static string GetMimeType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream"
            };
        }

        private ImageSource LoadImageFromBytes(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        private void RaiseConfigCommandsCanExecute()
        {
            (SaveConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void AppendLog(string message)
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (LogLines.Count > MaxLogEntries)
            {
                while (LogLines.Count > MaxLogEntries)
                {
                    LogLines.RemoveAt(0);
                }
            }
        }

        private string TruncateForLog(string text, int maxLength = 2000)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "...(truncated)";
        }

        private string? SaveResultToOutputFolder(byte[] bytes, string? suggestedName = null)
        {
            try
            {
                var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
                Directory.CreateDirectory(outputDir);
                string baseName = string.IsNullOrWhiteSpace(suggestedName)
                    ? $"gemini-result-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png"
                    : $"{Path.GetFileNameWithoutExtension(suggestedName)}-gemini-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png";
                var fileName = baseName;
                var fullPath = Path.Combine(outputDir, fileName);
                File.WriteAllBytes(fullPath, bytes);
                _lastOutputFolderPath = outputDir;
                return fullPath;
            }
            catch (Exception ex)
            {
                AppendLog($"保存结果失败: {ex.Message}");
                return null;
            }
        }

        private void TryOpenOutputFolder(string folderPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendLog($"打开输出目录失败: {ex.Message}");
            }
        }

        public void OpenLastOutputFolder()
        {
            if (string.IsNullOrWhiteSpace(_lastOutputFolderPath) || !Directory.Exists(_lastOutputFolderPath))
            {
                var message = "暂无可打开的输出目录，请先生成结果。";
                StatusMessage = message;
                AppendLog(message);
                return;
            }

            TryOpenOutputFolder(_lastOutputFolderPath);
        }

        private void ValidateImageSizeInput()
        {
            if (TryResolveImageSize(out _, out var error))
            {
                ImageSizeValidationError = null;
            }
            else
            {
                ImageSizeValidationError = error;
            }
        }

        private bool TryResolveImageSize(out GeminiImageSize? imageSize, out string? error)
        {
            imageSize = null;
            error = null;

            if (_selectedImageSizeOption == null)
            {
                error = "请先选择图片大小。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_selectedImageSizeOption.Value))
            {
                error = "图片大小不能为空。";
                return false;
            }

            if (GeminiImageSize.TryParse(_selectedImageSizeOption.Value, out var optionSize, out var optionError))
            {
                imageSize = optionSize;
                return true;
            }

            error = optionError ?? "图片大小配置无效。";
            return false;
        }

        private string GetImageSizeForLog(GeminiImageSize? imageSize)
        {
            if (imageSize.HasValue)
            {
                return imageSize.Value.Value;
            }

            return "未设置";
        }

        private string GetAspectRatioForLog(string? aspectRatio)
        {
            return string.IsNullOrWhiteSpace(aspectRatio) ? "未指定" : aspectRatio;
        }

        private bool IsSupportedImage(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".webp" || extension == ".bmp";
        }

        private bool TryCreateInputImageItem(string path, out InputImageItem item, out string? error)
        {
            item = null!;
            error = null;

            try
            {
                var info = new FileInfo(path);
                int width;
                int height;
                using (var metaStream = File.OpenRead(path))
                {
                    var decoder = BitmapDecoder.Create(metaStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames.FirstOrDefault();
                    if (frame == null)
                    {
                        error = "无法读取图片帧。";
                        return false;
                    }

                    width = frame.PixelWidth;
                    height = frame.PixelHeight;
                }

                ImageSource thumbnail;
                using (var stream = File.OpenRead(path))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.DecodePixelWidth = 256;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    thumbnail = bitmap;
                }

                item = new InputImageItem(path, thumbnail, info.Length, width, height);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string FormatBytes(long bytes)
        {
            const double scale = 1024.0;
            if (bytes < scale)
            {
                return $"{bytes} B";
            }

            double kb = bytes / scale;
            if (kb < scale)
            {
                return $"{kb:F1} KB";
            }

            double mb = kb / scale;
            return $"{mb:F1} MB";
        }

        private GeminiGenerationTask? BuildTaskSnapshot(IReadOnlyCollection<InputImageItem> items, GeminiImageSize? imageSize, DelayRangeSeconds? delayRange)
        {
            if (items.Count == 0)
            {
                StatusMessage = "请先选择素材图片。";
                return null;
            }

            var images = new List<GeminiTaskImage>();
            foreach (var image in items)
            {
                if (!File.Exists(image.Path))
                {
                    AppendLog($"文件不存在，已跳过: {image.FileName}");
                    continue;
                }

                images.Add(new GeminiTaskImage
                {
                    Path = image.Path,
                    FileName = image.FileName,
                    MimeType = GetMimeType(image.Path),
                    FileSizeBytes = image.FileSizeBytes
                });
            }

            if (images.Count == 0)
            {
                StatusMessage = "没有可用的素材文件。";
                return null;
            }

            return new GeminiGenerationTask
            {
                ApiKey = ApiKey,
                SelectedModel = SelectedModel,
                Prompt = Prompt,
                Temperature = Temperature,
                ImageSize = imageSize,
                AspectRatio = GetAspectRatioForRequest(),
                AspectRatioDisplay = GetAspectRatioForLog(GetAspectRatioForRequest()),
                ImageSizeDisplay = GetImageSizeForLog(imageSize),
                Images = images,
                DelayAfterRange = delayRange
            };
        }

        private bool TryEnqueueTasks(IEnumerable<GeminiGenerationTask?> tasks)
        {
            var validTasks = tasks.Where(t => t != null).Cast<GeminiGenerationTask>().ToList();
            if (validTasks.Count == 0)
            {
                StatusMessage = "没有可加入队列的任务。";
                return false;
            }

            lock (_taskQueue)
            {
                if (_taskQueue.Count + validTasks.Count > MaxQueueSize)
                {
                    StatusMessage = $"队列已满（最多 {MaxQueueSize} 个）。";
                    return false;
                }

                foreach (var task in validTasks)
                {
                    _taskQueue.Enqueue(task);
                }
            }

            return true;
        }

        private async Task<List<GeminiInlineImage>> LoadInlineImagesAsync(IEnumerable<GeminiTaskImage> images)
        {
            var result = new List<GeminiInlineImage>();
            foreach (var image in images)
            {
                if (!File.Exists(image.Path))
                {
                    AppendLog($"文件不存在，跳过: {image.FileName}");
                    continue;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(image.Path);
                }
                catch (Exception ex)
                {
                    AppendLog($"读取文件信息失败 {image.FileName}: {ex.Message}");
                    continue;
                }

                if (info.Length > MaxInlineBytes)
                {
                    AppendLog($"文件过大(>{FormatBytes(MaxInlineBytes)})，跳过: {image.FileName}");
                    continue;
                }

                byte[] bytes;
                try
                {
                    bytes = await File.ReadAllBytesAsync(image.Path);
                }
                catch (Exception ex)
                {
                    AppendLog($"读取 {image.FileName} 失败: {ex.Message}");
                    continue;
                }

                result.Add(new GeminiInlineImage
                {
                    MimeType = image.MimeType,
                    Data = bytes,
                    DisplayName = image.FileName
                });
            }

            return result;
        }

        private string BuildErrorMessage(GeminiImageResult result)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                parts.Add(result.Error);
            }

            if (result.StatusCode.HasValue)
            {
                parts.Add($"状态码 {(int)result.StatusCode.Value}");
            }

            if (!string.IsNullOrWhiteSpace(result.FinishReason))
            {
                parts.Add($"finishReason={result.FinishReason}");
            }

            if (!string.IsNullOrWhiteSpace(result.PromptFeedback))
            {
                parts.Add($"promptFeedback={result.PromptFeedback}");
            }

            if (result.SafetyRatings.Count > 0)
            {
                parts.Add($"safety={string.Join(",", result.SafetyRatings)}");
            }

            return parts.Count == 0 ? "未知错误" : string.Join("；", parts);
        }

        private async Task BatchGenerateAsync()
        {
            if (!TryValidateInputs(out var imageSize))
            {
                return;
            }

            int minDelay = Math.Max(0, DelayMinSeconds);
            int maxDelay = Math.Max(minDelay, DelayMaxSeconds);
            int takeCount = Math.Min(BatchCount, InputImages.Count);
            var delayRange = new DelayRangeSeconds(minDelay, maxDelay);
            var items = InputImages.Take(takeCount).ToList();

            var tasks = new List<GeminiGenerationTask?>();
            foreach (var image in items)
            {
                tasks.Add(BuildTaskSnapshot(new[] { image }, imageSize, delayRange));
            }

            if (!TryEnqueueTasks(tasks))
            {
                return;
            }

            AppendLog($"批量生成加入队列: {tasks.Count} 个任务，等待区间 {delayRange.MinSeconds}-{delayRange.MaxSeconds} 秒，分辨率 {GetImageSizeForLog(imageSize)}，长宽比 {GetAspectRatioForLog(GetAspectRatioForRequest())}。");
            UpdateQueueStatus(_isProcessingQueue ? _queueCurrent : 0, _isProcessingQueue ? _queueCurrent + _taskQueue.Count : _taskQueue.Count);
            StatusMessage = $"批量生成已加入队列，当前队列 {_taskQueue.Count} 个任务。";
            await ProcessQueueAsync();
        }

        private void OnInputImagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasInputImages));
            (ClearInputCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BatchGenerateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            RaiseCanExecuteChanged();
        }

        private async Task ProcessQueueAsync()
        {
            if (_isProcessingQueue)
            {
                int waiting;
                lock (_taskQueue)
                {
                    waiting = _taskQueue.Count;
                }
                UpdateQueueStatus(_queueCurrent, _queueCurrent + waiting);
                return;
            }

            _isProcessingQueue = true;
            IsBusy = true;
            RaiseCanExecuteChanged();

            try
            {
                int processed = 0;
                while (true)
                {
                    GeminiGenerationTask? task;
                    int queueCountBeforeDequeue;
                    lock (_taskQueue)
                    {
                        queueCountBeforeDequeue = _taskQueue.Count;
                        if (queueCountBeforeDequeue == 0)
                        {
                            break;
                        }

                        task = _taskQueue.Dequeue();
                    }

                    int total = processed + queueCountBeforeDequeue;
                    int current = processed + 1;
                    UpdateQueueStatus(current, total);

                    await ProcessSingleTaskAsync(task, current, total);
                    processed++;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"处理队列时发生异常: {ex}");
                StatusMessage = $"队列处理异常: {ex.Message}";
            }
            finally
            {
                _isProcessingQueue = false;
                IsBusy = false;
                UpdateQueueStatus(0, 0);
                RaiseCanExecuteChanged();
            }

            bool hasMore;
            lock (_taskQueue)
            {
                hasMore = _taskQueue.Count > 0;
            }

            if (hasMore)
            {
                await ProcessQueueAsync();
            }
        }

        private async Task ProcessSingleTaskAsync(GeminiGenerationTask task, int current, int total)
        {
            StatusMessage = $"正在处理队列任务 {current}/{total}";
            AppendLog($"执行队列任务 {current}/{total}: 模型 {task.SelectedModel}，分辨率 {task.ImageSizeDisplay}，长宽比 {task.AspectRatioDisplay}，素材 {task.Images.Count} 张。");

            var inlineImages = await LoadInlineImagesAsync(task.Images);
            if (inlineImages.Count == 0)
            {
                AppendLog("任务中没有可用的素材，已跳过。");
                return;
            }

            var request = new GeminiImageRequest
            {
                ApiKey = task.ApiKey,
                Model = task.SelectedModel,
                Prompt = task.Prompt,
                Temperature = task.Temperature,
                ImageSize = task.ImageSize,
                AspectRatio = task.AspectRatio,
                Images = inlineImages
            };

            var result = await _geminiClient.GenerateAsync(request, CancellationToken.None);
            if (!result.Success || result.ImageBytes == null)
            {
                string message = BuildErrorMessage(result);
                StatusMessage = $"任务 {current}/{total} 失败: {message}";
                AppendLog($"任务失败: {message}");
                if (!string.IsNullOrWhiteSpace(result.ErrorBody))
                {
                    AppendLog($"错误响应: {TruncateForLog(result.ErrorBody)}");
                }
                return;
            }

            _resultImageBytes = result.ImageBytes;
            ResultImageSource = LoadImageFromBytes(result.ImageBytes);

            var savedPath = SaveResultToOutputFolder(result.ImageBytes, task.Images.Count == 1 ? task.Images[0].FileName : null);

            var diagnostics = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.FinishReason))
            {
                diagnostics.Add($"finishReason={result.FinishReason}");
            }

            if (!string.IsNullOrWhiteSpace(result.PromptFeedback))
            {
                diagnostics.Add($"promptFeedback={result.PromptFeedback}");
            }

            if (result.SafetyRatings.Count > 0)
            {
                diagnostics.Add($"safety={string.Join(",", result.SafetyRatings)}");
            }

            if (diagnostics.Count > 0)
            {
                AppendLog(string.Join("；", diagnostics));
            }

            if (savedPath != null)
            {
                StatusMessage = $"任务 {current}/{total} 完成，已保存: {savedPath}";
                AppendLog($"任务 {current}/{total} 保存: {savedPath}（点击结果区域可打开输出目录）");
            }
            else
            {
                StatusMessage = $"任务 {current}/{total} 完成，但保存失败。";
            }

            RaiseCanExecuteChanged();
            await ApplyPostTaskDelayAsync(task, current, total);
        }

        private async Task ApplyPostTaskDelayAsync(GeminiGenerationTask task, int current, int total)
        {
            if (task.DelayAfterRange == null || current >= total)
            {
                return;
            }

            int delaySeconds = _random.Next(task.DelayAfterRange.Value.MinSeconds, task.DelayAfterRange.Value.MaxSeconds + 1);
            if (delaySeconds <= 0)
            {
                return;
            }

            AppendLog($"等待 {delaySeconds} 秒后继续处理...");
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }

        private void RunSafeAsync(Func<Task> action)
        {
            _ = RunSafeInternalAsync(action);
        }

        private async Task RunSafeInternalAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                StatusMessage = $"操作失败: {ex.Message}";
                AppendLog($"执行失败: {ex}");
                _isProcessingQueue = false;
                IsBusy = false;
                UpdateQueueStatus(0, 0);
                RaiseCanExecuteChanged();
            }
        }

        private sealed class GeminiConfigDto
        {
            public string? ApiKey { get; set; }
            public string? SelectedModel { get; set; }
            public string? Prompt { get; set; }
            public double Temperature { get; set; }
            public int DelayMinSeconds { get; set; }
            public int DelayMaxSeconds { get; set; }
            public int BatchCount { get; set; }
            public string? SelectedImageSizeValue { get; set; }
            public string? SelectedAspectRatioValue { get; set; }


            public string? SelectedResolutionValue { get; set; }
            public string? ManualResolution { get; set; }
        }

        private GeminiImageSize? GetImageSizeForRequest()
        {
            return TryResolveImageSize(out var size, out _) ? size : null;
        }

        private string? GetAspectRatioForRequest()
        {
            return string.IsNullOrWhiteSpace(_selectedAspectRatioOption?.Value) ? null : _selectedAspectRatioOption.Value;
        }

        private void LoadAvailableConfigs()
        {
            try
            {
                var dir = GetConfigDirectory();
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var files = Directory.GetFiles(dir, "*.json");
                var names = files.Select(f => Path.GetFileNameWithoutExtension(f))
                                 .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                 .ToList();

                _availableConfigNames.Clear();
                foreach (var name in names)
                {
                    _availableConfigNames.Add(name);
                }

                RaiseConfigCommandsCanExecute();
            }
            catch (Exception ex)
            {
                AppendLog($"加载配置列表失败: {ex.Message}");
            }
        }

        private void TryLoadConfigByName(string name)
        {
            string cleanName = SanitizeFileName(name);
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                return;
            }

            string path = GetConfigFilePath(cleanName);
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<GeminiConfigDto>(json);
                if (config == null)
                {
                    return;
                }

                _isApplyingConfig = true;
                ApiKey = config.ApiKey ?? string.Empty;
                SelectedModel = string.IsNullOrWhiteSpace(config.SelectedModel) ? SelectedModel : config.SelectedModel;
                Prompt = config.Prompt ?? string.Empty;
                Temperature = config.Temperature;
                DelayMinSeconds = config.DelayMinSeconds;
                DelayMaxSeconds = config.DelayMaxSeconds;
                BatchCount = config.BatchCount;
                ApplyImageSizeValue(config.SelectedImageSizeValue ?? config.SelectedResolutionValue);
                ApplyAspectRatioValue(config.SelectedAspectRatioValue);
                _isApplyingConfig = false;

                StatusMessage = $"已加载配置: {cleanName}";
                AppendLog(StatusMessage);
            }
            catch (Exception ex)
            {
                _isApplyingConfig = false;
                AppendLog($"加载配置失败: {ex.Message}");
            }
        }

        private void ApplyImageSizeValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var match = ImageSizeOptions.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                SelectedImageSizeOption = match;
                return;
            }

            ValidateImageSizeInput();
        }

        private void ApplyAspectRatioValue(string? value)
        {
            var target = AspectRatioOptions.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));
            SelectedAspectRatioOption = target ?? AspectRatioOptions.First();
        }

        private void SaveConfig()
        {
            string name = SanitizeFileName(SelectedConfigName);
            if (string.IsNullOrWhiteSpace(name))
            {
                StatusMessage = "配置名不能为空。";
                return;
            }

            var dir = GetConfigDirectory();
            Directory.CreateDirectory(dir);
            string path = GetConfigFilePath(name);

            var imageSizeValue = GetImageSizeForRequest()?.ToRequestValue();
            var aspectRatioValue = GetAspectRatioForRequest();
            var dto = new GeminiConfigDto
            {
                ApiKey = ApiKey,
                SelectedModel = SelectedModel,
                Prompt = Prompt,
                Temperature = Temperature,
                DelayMinSeconds = DelayMinSeconds,
                DelayMaxSeconds = DelayMaxSeconds,
                BatchCount = BatchCount,
                SelectedImageSizeValue = imageSizeValue,
                SelectedAspectRatioValue = aspectRatioValue,

                SelectedResolutionValue = imageSizeValue
            };

            try
            {
                if (!string.IsNullOrWhiteSpace(ApiKey))
                {
                    AppendLog("注意: API Key 将以明文保存在本地配置中，请谨慎使用共享环境。");
                }

                string json = JsonSerializer.Serialize(dto, _serializerOptions);
                File.WriteAllText(path, json);
                if (!_availableConfigNames.Contains(name))
                {
                    _availableConfigNames.Add(name);
                }
                SelectedConfigName = name;
                StatusMessage = $"配置已保存: {name}";
                AppendLog(StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存配置失败: {ex.Message}";
                AppendLog(StatusMessage);
            }
            finally
            {
                RaiseConfigCommandsCanExecute();
            }
        }

        private bool CanSaveConfig()
        {
            return !string.IsNullOrWhiteSpace(SelectedConfigName);
        }

        private void DeleteConfig()
        {
            string name = SanitizeFileName(SelectedConfigName);
            if (string.IsNullOrWhiteSpace(name))
            {
                StatusMessage = "配置名不能为空。";
                return;
            }

            string path = GetConfigFilePath(name);
            if (!File.Exists(path))
            {
                StatusMessage = "配置文件不存在。";
                RaiseConfigCommandsCanExecute();
                return;
            }

            try
            {
                File.Delete(path);
                _availableConfigNames.Remove(name);
                StatusMessage = $"配置已删除: {name}";
                AppendLog(StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = $"删除配置失败: {ex.Message}";
                AppendLog(StatusMessage);
            }
            finally
            {
                RaiseConfigCommandsCanExecute();
            }
        }

        private bool CanDeleteConfig()
        {
            string name = SanitizeFileName(SelectedConfigName);
            return !string.IsNullOrWhiteSpace(name) && File.Exists(GetConfigFilePath(name));
        }

        private string GetConfigDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "GeminiPic");
        }

        private string GetConfigFilePath(string name)
        {
            return Path.Combine(GetConfigDirectory(), $"{name}.json");
        }

        private string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string((name ?? string.Empty).Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return sanitized;
        }

        public sealed class ImageSizeOption
        {
            public ImageSizeOption(string display, string? value)
            {
                Display = display;
                Value = value;
            }

            public string Display { get; }
            public string? Value { get; }
        }

        public sealed class AspectRatioOption
        {
            public AspectRatioOption(string display, string? value)
            {
                Display = display;
                Value = value;
            }

            public string Display { get; }
            public string? Value { get; }
        }

        public sealed class InputImageItem
        {
            public InputImageItem(string path, ImageSource thumbnail, long fileSizeBytes, int pixelWidth, int pixelHeight)
            {
                Path = path;
                Thumbnail = thumbnail;
                FileName = System.IO.Path.GetFileName(path);
                FileSizeBytes = fileSizeBytes;
                PixelWidth = pixelWidth;
                PixelHeight = pixelHeight;
                DisplaySize = FormatBytes(fileSizeBytes);
                DisplayPixels = $"{pixelWidth}x{pixelHeight}";
            }

            public string Path { get; }
            public string FileName { get; }
            public ImageSource Thumbnail { get; }
            public long FileSizeBytes { get; }
            public int PixelWidth { get; }
            public int PixelHeight { get; }
            public string DisplaySize { get; }
            public string DisplayPixels { get; }
        }

        private sealed class GeminiGenerationTask
        {
            public string ApiKey { get; set; } = string.Empty;
            public string SelectedModel { get; set; } = string.Empty;
            public string Prompt { get; set; } = string.Empty;
            public double Temperature { get; set; }
            public GeminiImageSize? ImageSize { get; set; }
            public string ImageSizeDisplay { get; set; } = string.Empty;
            public string? AspectRatio { get; set; }
            public string AspectRatioDisplay { get; set; } = string.Empty;
            public List<GeminiTaskImage> Images { get; set; } = new();
            public DelayRangeSeconds? DelayAfterRange { get; set; }
        }

        private sealed class GeminiTaskImage
        {
            public string Path { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public string MimeType { get; set; } = string.Empty;
            public long FileSizeBytes { get; set; }
        }

        private readonly struct DelayRangeSeconds
        {
            public DelayRangeSeconds(int minSeconds, int maxSeconds)
            {
                MinSeconds = Math.Max(0, Math.Min(minSeconds, maxSeconds));
                MaxSeconds = Math.Max(MinSeconds, maxSeconds);
            }

            public int MinSeconds { get; }
            public int MaxSeconds { get; }
        }

        private void UpdateQueueStatus(int current, int total)
        {
            _queueCurrent = Math.Max(0, current);
            _queueTotal = Math.Max(0, total);
            QueueStatusText = $"当前队列任务{_queueCurrent}/{_queueTotal}";
        }
    }
}
