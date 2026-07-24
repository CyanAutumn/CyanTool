using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CyanTools.Utils;
using CyanTools.Model;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public class DatasetManagerViewModel : INotifyPropertyChanged
    {
        
        public event Action? OnCanvasResetRequired;
        
        private readonly Dictionary<string, List<Rect>> _cachedEdits = new Dictionary<string, List<Rect>>();
        private readonly Dictionary<string, string> _cachedTextEdits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string? _currentFilePath;
        private string _currentFolderPath = string.Empty;

        
        private BitmapImage? _originalBitmap;
        private bool _isPanning = false;
        private System.Windows.Point _panStartPoint;
        private bool _isDrawing = false;
        private System.Windows.Point _rectStartPoint;
        private System.Windows.Shapes.Rectangle? _currentDrawingRectangle;
        private readonly List<System.Windows.Shapes.Rectangle> _drawnRectangles = new List<System.Windows.Shapes.Rectangle>();
        private readonly List<Rect> _finalRects = new List<Rect>();

        
        private BitmapImage? _currentImageSource;
        private DatasetDataItem? _selectedDataItem;
        private string _currentTextContent = "";
        private bool _autoNext = true;
        private bool _isBrowseMode;
        private int _currentIndex = 0;
        private readonly Random _random = new Random();
        private readonly TagDictionaryService _tagDictionaryService;
        private readonly CancellationTokenSource _tagCancellation = new CancellationTokenSource();
        private CancellationTokenSource? _suggestionCancellation;
        private CancellationTokenSource? _translationCancellation;
        private int _suggestionRequestId;
        private CancellationTokenSource? _imageLoadCancellation;
        private int _imageLoadRequestId;
        private int _suppressSuggestionRefresh;
        private bool _suppressTagTokenChange;
        private string _pendingTagText = string.Empty;
        private string _manualTagInput = string.Empty;
        private string _manualTagTranslation = string.Empty;
        private int _translatedTagCount;
        private int _untranslatedTagCount;
        private bool _isTranslatingAllTags;
        private bool _isSuggestionOpen = false;
        private bool _isFolderPickerVisible = true;
        private static readonly char[] TagSeparators = { ',', '，', '\r', '\n' };

        
        private string _category1Path = "";
        private string _category2Path = "";
        private string _category3Path = "";
        private string _category4Path = "";
        private string _category5Path = "";
        private bool _isCategory1Selected = true;
        private bool _isCategory2Selected = true;
        private bool _isCategory3Selected = true;
        private bool _isCategory4Selected = true;
        private bool _isCategory5Selected = true;

        
        public ObservableCollection<DatasetDataItem> DataItemsWithOperations { get; } = new ObservableCollection<DatasetDataItem>();
        public ObservableCollection<TagTokenViewModel> TagTokens { get; } = new ObservableCollection<TagTokenViewModel>();
        public ObservableCollection<TagSuggestion> TagSuggestions { get; } = new ObservableCollection<TagSuggestion>();

        
        public DatasetDataItem? SelectedDataItem
        {
            get => _selectedDataItem;
            set
            {
                if (EqualityComparer<DatasetDataItem?>.Default.Equals(_selectedDataItem, value))
                {
                    return;
                }

                SaveCurrentSelectionEditsToCache();

                _selectedDataItem = value;
                OnPropertyChanged();

                LoadSelectedData();
                if (_selectedDataItem != null)
                {
                    _currentIndex = DataItemsWithOperations.IndexOf(_selectedDataItem);
                }
            }
        }

        public BitmapImage? CurrentImageSource
        {
            get => _currentImageSource;
            private set => SetField(ref _currentImageSource, value);
        }

        public string CurrentTextContent
        {
            get => _currentTextContent;
            set => SetField(ref _currentTextContent, value);
        }

        public bool AutoNext
        {
            get => _autoNext;
            set => SetField(ref _autoNext, value);
        }

        public bool IsBrowseMode
        {
            get => _isBrowseMode;
            set => SetField(ref _isBrowseMode, value);
        }

        public string PendingTagText
        {
            get => _pendingTagText;
            set => SetField(ref _pendingTagText, value);
        }

        public string ManualTagInput
        {
            get => _manualTagInput;
            set
            {
                if (SetField(ref _manualTagInput, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string ManualTagTranslation
        {
            get => _manualTagTranslation;
            set => SetField(ref _manualTagTranslation, value);
        }

        public int TranslatedTagCount
        {
            get => _translatedTagCount;
            private set
            {
                if (SetField(ref _translatedTagCount, value))
                {
                    OnPropertyChanged(nameof(TagTranslationCountDisplay));
                }
            }
        }

        public int UntranslatedTagCount
        {
            get => _untranslatedTagCount;
            private set
            {
                if (SetField(ref _untranslatedTagCount, value))
                {
                    OnPropertyChanged(nameof(TagTranslationCountDisplay));
                }
            }
        }

        public string TagTranslationCountDisplay => $"已翻译: {TranslatedTagCount} / 未翻译: {UntranslatedTagCount}";

        public bool IsTranslatingAllTags
        {
            get => _isTranslatingAllTags;
            set
            {
                if (SetField(ref _isTranslatingAllTags, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsSuggestionOpen
        {
            get => _isSuggestionOpen;
            set => SetField(ref _isSuggestionOpen, value);
        }

        public bool IsFolderPickerVisible
        {
            get => _isFolderPickerVisible;
            set => SetField(ref _isFolderPickerVisible, value);
        }

        public string CurrentFolderPath => _currentFolderPath;

        public string Category1Path
        {
            get => _category1Path;
            set => SetField(ref _category1Path, value);
        }

        public string Category2Path
        {
            get => _category2Path;
            set => SetField(ref _category2Path, value);
        }

        public string Category3Path
        {
            get => _category3Path;
            set => SetField(ref _category3Path, value);
        }

        public string Category4Path
        {
            get => _category4Path;
            set => SetField(ref _category4Path, value);
        }

        public string Category5Path
        {
            get => _category5Path;
            set => SetField(ref _category5Path, value);
        }

        public bool IsCategory1Selected
        {
            get => _isCategory1Selected;
            set => SetField(ref _isCategory1Selected, value);
        }

        public bool IsCategory2Selected
        {
            get => _isCategory2Selected;
            set => SetField(ref _isCategory2Selected, value);
        }

        public bool IsCategory3Selected
        {
            get => _isCategory3Selected;
            set => SetField(ref _isCategory3Selected, value);
        }

        public bool IsCategory4Selected
        {
            get => _isCategory4Selected;
            set => SetField(ref _isCategory4Selected, value);
        }

        public bool IsCategory5Selected
        {
            get => _isCategory5Selected;
            set => SetField(ref _isCategory5Selected, value);
        }

        
        public ICommand SelectFolderCommand { get; }
        public ICommand SaveAllCommand { get; }
        public ICommand SelectDataItemCommand { get; }
        
        public ICommand SetCategoryCommand { get; }
        public ICommand SelectCategoryFolderCommand { get; }
        public ICommand PreviousImageCommand { get; }
        public ICommand NextImageCommand { get; }
        public ICommand UndoOperationCommand { get; }
        public ICommand DeleteImageCommand { get; }
        public ICommand RandomCopyCommand { get; }
        public ICommand CopyNextDataCommand { get; }
        public ICommand SavePromptCommand { get; }
        public ICommand RemoveTagCommand { get; }
        public ICommand MoveTagToStartCommand { get; }
        public ICommand MoveTagToEndCommand { get; }
        public ICommand MoveTagToStartDatasetCommand { get; }
        public ICommand MoveTagToEndDatasetCommand { get; }
        public ICommand RemoveTagFromDatasetCommand { get; }
        public ICommand AddTagToDatasetCommand { get; }
        public ICommand ShufflePromptTagsDatasetCommand { get; }
        public ICommand UpsertTagTranslationCommand { get; }
        public ICommand TranslateAllMissingTagsCommand { get; }
        

        public DatasetManagerViewModel()
        {
            _tagDictionaryService = new TagDictionaryService(enableTranslation: true);

            SelectFolderCommand = new RelayCommand(_ => SelectFolder());
            SaveAllCommand = new RelayCommand(_ => SaveAllChanges());
            SelectDataItemCommand = new RelayCommand(param => SelectDataItem(param as DatasetDataItem));
            
            SetCategoryCommand = new RelayCommand(param => SetImageCategory(param?.ToString()));
            SelectCategoryFolderCommand = new RelayCommand(param => SelectCategoryFolder(param?.ToString()));
            PreviousImageCommand = new RelayCommand(_ => PreviousImage());
            NextImageCommand = new RelayCommand(_ => NextImage());
            UndoOperationCommand = new RelayCommand(_ => UndoOperation());
            DeleteImageCommand = new RelayCommand(_ => DeleteImage());
            RandomCopyCommand = new RelayCommand(_ => RandomCopy(), _ => DataItemsWithOperations.Any());
            CopyNextDataCommand = new RelayCommand(_ => CopyNextData(), _ => DataItemsWithOperations.Any());
            SavePromptCommand = new RelayCommand(_ => SavePrompt(), _ => SelectedDataItem != null);
            RemoveTagCommand = new RelayCommand(param => RemoveTag(param as TagTokenViewModel));
            MoveTagToStartCommand = new RelayCommand(param => MoveTagToStart(param as TagTokenViewModel),
                param => CanMoveTagToStart(param as TagTokenViewModel));
            MoveTagToEndCommand = new RelayCommand(param => MoveTagToEnd(param as TagTokenViewModel),
                param => CanMoveTagToEnd(param as TagTokenViewModel));
            MoveTagToStartDatasetCommand = new RelayCommand(param => MoveTagToStartDataset(param as TagTokenViewModel),
                param => CanApplyDatasetTagAction(param as TagTokenViewModel));
            MoveTagToEndDatasetCommand = new RelayCommand(param => MoveTagToEndDataset(param as TagTokenViewModel),
                param => CanApplyDatasetTagAction(param as TagTokenViewModel));
            RemoveTagFromDatasetCommand = new RelayCommand(param => RemoveTagFromDataset(param as TagTokenViewModel),
                param => CanApplyDatasetTagAction(param as TagTokenViewModel));
            AddTagToDatasetCommand = new RelayCommand(param => AddTagToDataset(param as TagTokenViewModel),
                param => CanApplyDatasetTagAction(param as TagTokenViewModel));
            ShufflePromptTagsDatasetCommand = new RelayCommand(_ => ShufflePromptTagsDataset(),
                _ => DataItemsWithOperations.Any());
            UpsertTagTranslationCommand = new RelayCommand(async _ => await UpsertTagTranslationAsync(),
                _ => !string.IsNullOrWhiteSpace(ManualTagInput));
            TranslateAllMissingTagsCommand = new RelayCommand(async _ => await TranslateAllMissingTagsAsync(),
                _ => !IsTranslatingAllTags);

            TagTokens.CollectionChanged += TagTokens_CollectionChanged;
            _ = RefreshTranslationCountsAsync();
            
        }

        private void SelectDataItem(DatasetDataItem? item)
        {
            if (item == null)
            {
                return;
            }

            SelectedDataItem = item;
        }

        #region 文件加载与保存

        

        private void SelectFolder()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "请选择一个文件夹"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                LoadFolder(dialog.FileName);
            }
        }

        public void LoadFolder(string folderPath, IEnumerable<string>? excludedTopLevelDirectories = null)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            try
            {
                _currentFolderPath = folderPath;
                OnPropertyChanged(nameof(CurrentFolderPath));
                _cachedEdits.Clear();
                _cachedTextEdits.Clear();
                CancelImageLoad();
                _currentFilePath = null;
                ResetState();
                CurrentImageSource = null;
                CurrentTextContent = "";
                _selectedDataItem = null;
                OnPropertyChanged(nameof(SelectedDataItem));
                ResetTagEditor();
                OnCanvasResetRequired?.Invoke();

                var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                var itemsByBaseName = new Dictionary<string, DatasetDataItem>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in EnumerateDatasetFiles(folderPath, excludedTopLevelDirectories))
                {
                    string extension = System.IO.Path.GetExtension(file);
                    string relativePath = System.IO.Path.GetRelativePath(folderPath, file)
                        .Replace(System.IO.Path.AltDirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar);
                    string baseKey = extension.Length > 0 && relativePath.Length > extension.Length
                        ? relativePath.Substring(0, relativePath.Length - extension.Length)
                        : System.IO.Path.GetFileNameWithoutExtension(relativePath);

                    if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!itemsByBaseName.TryGetValue(baseKey, out var item))
                        {
                            item = new DatasetDataItem { BaseName = baseKey };
                            itemsByBaseName.Add(baseKey, item);
                        }
                        item.TextPath = file;
                        continue;
                    }

                    if (imageExtensions.Contains(extension))
                    {
                        if (!itemsByBaseName.TryGetValue(baseKey, out var item))
                        {
                            item = new DatasetDataItem { BaseName = baseKey };
                            itemsByBaseName.Add(baseKey, item);
                        }

                        if (string.IsNullOrEmpty(item.ImagePath))
                        {
                            item.ImagePath = file;
                        }
                        else
                        {
                            string uniqueKey = relativePath;
                            if (!itemsByBaseName.ContainsKey(uniqueKey))
                            {
                                itemsByBaseName.Add(uniqueKey, new DatasetDataItem { BaseName = uniqueKey, ImagePath = file });
                            }
                        }
                    }
                }

                DataItemsWithOperations.Clear();
                foreach (var item in itemsByBaseName.Values.OrderBy(i => i.BaseName, NaturalStringComparer.Instance))
                {
                    DataItemsWithOperations.Add(item);
                }

                if (DataItemsWithOperations.Any())
                {
                    _currentIndex = 0;
                    SelectedDataItem = DataItemsWithOperations.First();
                }
                else
                {
                    _currentIndex = 0;
                    SelectedDataItem = null;
                }

                StartDatasetTagTranslation(DataItemsWithOperations.ToList());
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"加载文件夹时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private static IEnumerable<string> EnumerateDatasetFiles(
            string folderPath,
            IEnumerable<string>? excludedTopLevelDirectories)
        {
            var excluded = new HashSet<string>(
                excludedTopLevelDirectories ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(folderPath);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                IEnumerable<string> files;
                IEnumerable<string> directories;
                try
                {
                    files = Directory.EnumerateFiles(directory).ToArray();
                    directories = Directory.EnumerateDirectories(directory).ToArray();
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }

                foreach (var child in directories)
                {
                    var relative = System.IO.Path.GetRelativePath(folderPath, child);
                    var firstSegment = relative.Split(
                        new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar },
                        StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (firstSegment == null || !excluded.Contains(firstSegment))
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        private void SaveAllChanges()
        {
            SaveCurrentSelectionEditsToCache();

            if (!DataItemsWithOperations.Any())
            {
                System.Windows.MessageBox.Show("没有需要处理的数据。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            try
            {
                int processedCount = 0;
                int errorCount = 0;
                int skippedCount = 0;
                int uncheckedCategorySkippedCount = 0;

                foreach (var item in DataItemsWithOperations.ToList())
                {
                    bool hasPendingChanges = HasPendingChanges(item);
                    if (string.IsNullOrEmpty(item.Operation) && !hasPendingChanges)
                    {
                        continue;
                    }

                    try
                    {
                        if (item.Operation == "删")
                        {
                            DeleteDataItemFiles(item);
                        }
                        else if (string.IsNullOrEmpty(item.Operation))
                        {
                            SaveDataItemInPlace(item);
                        }
                        else
                        {
                            if (!IsCategorySelected(item.Operation))
                            {
                                uncheckedCategorySkippedCount++;
                                continue;
                            }

                            string targetFolder = GetCategoryFolder(item.Operation);
                            if (!string.IsNullOrEmpty(targetFolder) && Directory.Exists(targetFolder))
                            {
                                MoveDataItemFiles(item, targetFolder);
                            }
                            else
                            {
                                System.Windows.MessageBox.Show($"分类{item.Operation}的目标文件夹未设置或不存在。", "警告", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                                skippedCount++;
                                continue;
                            }
                        }

                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        System.Windows.MessageBox.Show($"处理文件 {item.DisplayName} 时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                }

                if (processedCount == 0 && errorCount == 0 && skippedCount == 0)
                {
                    System.Windows.MessageBox.Show("没有需要保存的更改。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                {
                    LoadFolder(_currentFolderPath);
                }

                string message = $"处理完成！\n\n成功处理: {processedCount} 条数据\n错误: {errorCount} 条数据";
                if (skippedCount > 0)
                {
                    message += $"\n跳过: {skippedCount} 条数据";
                }
                if (uncheckedCategorySkippedCount > 0)
                {
                    message += $"\n未勾选分类而跳过: {uncheckedCategorySkippedCount} 条数据";
                }

                System.Windows.MessageBox.Show(message, "完成", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"应用更改时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadSelectedData()
        {
            if (SelectedDataItem == null)
            {
                CancelImageLoad();
                ClearCurrentImagePreview();
                CurrentTextContent = "";
                ResetTagEditor();
                return;
            }

            var imagePath = SelectedDataItem.ImagePath;
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                _ = LoadImageFromFileAsync(imagePath);
            }
            else
            {
                CancelImageLoad();
            }

            ClearCurrentImagePreview();
            LoadTextForItem(SelectedDataItem);
        }

        private void ClearCurrentImagePreview()
        {
            CurrentImageSource = null;
            _currentFilePath = null;
            ResetState();
            OnCanvasResetRequired?.Invoke();
        }

        private void CancelImageLoad()
        {
            _imageLoadCancellation?.Cancel();
            _imageLoadCancellation?.Dispose();
            _imageLoadCancellation = null;
            Interlocked.Increment(ref _imageLoadRequestId);
        }

        private void LoadTextFromFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                CurrentTextContent = "";
                ResetTagEditor();
                return;
            }

            try
            {
                var content = File.ReadAllText(filePath);
                LoadTagsFromText(content);
            }
            catch (Exception ex)
            {
                CurrentTextContent = "";
                ResetTagEditor();
                System.Windows.MessageBox.Show($"读取文本失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadTextForItem(DatasetDataItem item)
        {
            string? filePath = GetDatasetTagTextPath(item);
            if (!string.IsNullOrWhiteSpace(filePath) && _cachedTextEdits.TryGetValue(filePath, out string cachedContent))
            {
                LoadTagsFromText(cachedContent);
                return;
            }

            LoadTextFromFile(filePath);
        }

        private void StartDatasetTagTranslation(IReadOnlyList<DatasetDataItem> items)
        {
            _translationCancellation?.Cancel();
            _translationCancellation?.Dispose();
            _translationCancellation = new CancellationTokenSource();
            var cancellationToken = _translationCancellation.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    var tags = CollectTagsFromItems(items, cancellationToken);
                    if (tags.Count == 0)
                    {
                        return;
                    }

                    var toTranslate = await _tagDictionaryService.GetTagsNeedingTranslationAsync(tags, cancellationToken).ConfigureAwait(false);
                    const int translateChunkSize = 100;
                    int currentChunkSize = translateChunkSize;
                    int offset = 0;
                    while (offset < toTranslate.Count)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int size = Math.Min(currentChunkSize, toTranslate.Count - offset);
                        var chunk = toTranslate.Skip(offset).Take(size).ToList();
                        var translatedMap = await _tagDictionaryService.TranslateAndCacheBatchAsync(
                            chunk,
                            cancellationToken,
                            allowSequentialFallback: false).ConfigureAwait(false);

                        if (translatedMap.Count == 0)
                        {
                            if (currentChunkSize != 1)
                            {
                                currentChunkSize = 1;
                                continue;
                            }

                            offset += 1;
                            continue;
                        }

                        foreach (var pair in translatedMap)
                        {
                            UpdateTokenTranslation(pair.Key, pair.Value);
                        }

                        offset += size;
                        if (currentChunkSize == 1)
                        {
                            currentChunkSize = translateChunkSize;
                        }
                    }
                    await RefreshTranslationCountsAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                }
            }, cancellationToken);
        }

        private static HashSet<string> CollectTagsFromItems(IEnumerable<DatasetDataItem> items, CancellationToken cancellationToken)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(item.TextPath) || !File.Exists(item.TextPath))
                {
                    continue;
                }

                try
                {
                    var content = File.ReadAllText(item.TextPath);
                    foreach (var tag in SplitTags(content))
                    {
                        tags.Add(tag);
                    }
                }
                catch
                {
                }
            }

            return tags;
        }

        private async Task RefreshTokenTranslationsAsync()
        {
            try
            {
                if (TagTokens.Count == 0)
                {
                    return;
                }

                var tags = TagTokens.Select(token => token.Text).Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
                if (tags.Count == 0)
                {
                    return;
                }

                var translations = await _tagDictionaryService.GetTranslationsAsync(tags, _tagCancellation.Token).ConfigureAwait(false);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var token in TagTokens)
                    {
                        if (translations.TryGetValue(token.Text, out var translation))
                        {
                            token.Translation = translation;
                        }
                        else
                        {
                            token.Translation = null;
                        }
                    }
                });
            }
            catch
            {
            }
        }

        private async Task LoadTokenTranslationAsync(TagTokenViewModel token)
        {
            if (token == null || string.IsNullOrWhiteSpace(token.Text))
            {
                return;
            }

            try
            {
                var translation = await _tagDictionaryService.GetTranslationAsync(token.Text, _tagCancellation.Token).ConfigureAwait(false);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    token.Translation = translation;
                });
            }
            catch
            {
            }
        }

        private void UpdateTokenTranslation(string tag, string translation)
        {
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(translation))
            {
                return;
            }

            var normalized = TagDictionaryService.NormalizeTag(tag);
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var token in TagTokens)
                {
                    if (string.Equals(token.Text, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        token.Translation = translation;
                    }
                }
            });
        }

        private void LoadTagsFromText(string text)
        {
            _suppressTagTokenChange = true;
            try
            {
                ResetTagEditor();

                foreach (var tag in SplitTags(text))
                {
                    AddTagToken(tag);
                }
            }
            finally
            {
                _suppressTagTokenChange = false;
            }

            UpdateCurrentTextContentFromTokens();
            _ = RefreshTokenTranslationsAsync();
        }

        private void CommitPromptEditorState()
        {
            CommitEditingTags();
            CommitPendingTags(true);
            UpdateCurrentTextContentFromTokens();
        }

        private void CommitEditingTags()
        {
            for (int index = TagTokens.Count - 1; index >= 0; index--)
            {
                var token = TagTokens[index];
                if (!token.IsEditing)
                {
                    continue;
                }

                var normalized = TagDictionaryService.NormalizeTag(token.EditingText);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    TagTokens.RemoveAt(index);
                    continue;
                }

                token.Text = normalized;
                token.EditingText = normalized;
                token.IsEditing = false;
                _ = _tagDictionaryService.UpsertCatalogTagAsync(normalized, "custom", _tagCancellation.Token);
                _ = LoadTokenTranslationAsync(token);
            }
        }

        private void ResetTagEditor()
        {
            TagTokens.Clear();
            PendingTagText = string.Empty;
            ClearSuggestions();
        }

        private void TagTokens_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_suppressTagTokenChange)
            {
                return;
            }

            UpdateCurrentTextContentFromTokens();
        }

        private void UpdateCurrentTextContentFromTokens()
        {
            CurrentTextContent = string.Join(", ", TagTokens.Select(token => token.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        private bool AddTagToken(string rawTag)
        {
            var normalized = TagDictionaryService.NormalizeTag(rawTag);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            var token = new TagTokenViewModel
            {
                Text = normalized,
                EditingText = normalized
            };

            TagTokens.Add(token);
            _ = _tagDictionaryService.UpsertCatalogTagAsync(normalized, "custom", _tagCancellation.Token);
            _ = LoadTokenTranslationAsync(token);
            return true;
        }

        private static IEnumerable<string> SplitTags(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                yield break;
            }

            var parts = text.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var normalized = TagDictionaryService.NormalizeTag(part);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    yield return normalized;
                }
            }
        }

        public void BeginEditTag(TagTokenViewModel? token)
        {
            if (token == null)
            {
                return;
            }

            foreach (var item in TagTokens)
            {
                if (item != token && item.IsEditing)
                {
                    item.IsEditing = false;
                    item.EditingText = item.Text;
                }
            }

            token.EditingText = token.Text;
            token.IsEditing = true;
        }

        public void CommitEditTag(TagTokenViewModel? token)
        {
            if (token == null)
            {
                return;
            }

            var normalized = TagDictionaryService.NormalizeTag(token.EditingText);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                TagTokens.Remove(token);
                return;
            }

            token.Text = normalized;
            token.EditingText = normalized;
            token.IsEditing = false;
            UpdateCurrentTextContentFromTokens();
            _ = _tagDictionaryService.UpsertCatalogTagAsync(normalized, "custom", _tagCancellation.Token);
            _ = LoadTokenTranslationAsync(token);
        }

        public void CancelEditTag(TagTokenViewModel? token)
        {
            if (token == null)
            {
                return;
            }

            token.EditingText = token.Text;
            token.IsEditing = false;
        }

        private void RemoveTag(TagTokenViewModel? token)
        {
            if (token == null)
            {
                return;
            }

            TagTokens.Remove(token);
        }

        private void MoveTagToStart(TagTokenViewModel? token)
        {
            MoveTagToken(token, 0);
        }

        private void MoveTagToEnd(TagTokenViewModel? token)
        {
            if (token == null)
            {
                return;
            }

            int lastIndex = TagTokens.Count - 1;
            MoveTagToken(token, lastIndex);
        }

        private bool CanMoveTagToStart(TagTokenViewModel? token)
        {
            if (token == null || TagTokens.Count <= 1)
            {
                return false;
            }

            int index = TagTokens.IndexOf(token);
            return index > 0;
        }

        private bool CanMoveTagToEnd(TagTokenViewModel? token)
        {
            if (token == null || TagTokens.Count <= 1)
            {
                return false;
            }

            int index = TagTokens.IndexOf(token);
            return index >= 0 && index < TagTokens.Count - 1;
        }

        private void MoveTagToken(TagTokenViewModel? token, int targetIndex)
        {
            if (token == null || TagTokens.Count == 0)
            {
                return;
            }

            int currentIndex = TagTokens.IndexOf(token);
            if (currentIndex < 0)
            {
                return;
            }

            if (targetIndex < 0)
            {
                targetIndex = 0;
            }

            if (targetIndex >= TagTokens.Count)
            {
                targetIndex = TagTokens.Count - 1;
            }

            if (currentIndex == targetIndex)
            {
                return;
            }

            TagTokens.Move(currentIndex, targetIndex);
        }

        private enum DatasetTagUpdateMode
        {
            MoveToStart,
            MoveToEnd,
            Remove,
            Add
        }

        private void MoveTagToStartDataset(TagTokenViewModel? token)
        {
            ApplyDatasetTagUpdate(token, DatasetTagUpdateMode.MoveToStart);
        }

        private void MoveTagToEndDataset(TagTokenViewModel? token)
        {
            ApplyDatasetTagUpdate(token, DatasetTagUpdateMode.MoveToEnd);
        }

        private void RemoveTagFromDataset(TagTokenViewModel? token)
        {
            ApplyDatasetTagUpdate(token, DatasetTagUpdateMode.Remove);
        }

        private void AddTagToDataset(TagTokenViewModel? token)
        {
            ApplyDatasetTagUpdate(token, DatasetTagUpdateMode.Add);
        }

        private void ShufflePromptTagsDataset()
        {
            if (!DataItemsWithOperations.Any())
            {
                return;
            }

            if (!TryPromptFixedTagCount(out int fixedTagCount))
            {
                return;
            }

            int updatedCount = 0;
            int errorCount = 0;
            bool selectedUpdated = false;
            string? selectedPath = null;

            var selectedItem = SelectedDataItem;
            string? selectedContent = null;
            if (selectedItem != null)
            {
                CommitPromptEditorState();
                selectedContent = CurrentTextContent ?? string.Empty;
            }

            foreach (var item in DataItemsWithOperations)
            {
                string? targetPath = GetDatasetTagTextPath(item);
                if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                {
                    continue;
                }

                string content;
                bool isSelectedItem = ReferenceEquals(item, selectedItem);
                if (isSelectedItem && selectedContent != null)
                {
                    content = selectedContent;
                }
                else
                {
                    try
                    {
                        content = File.ReadAllText(targetPath);
                    }
                    catch
                    {
                        errorCount++;
                        continue;
                    }
                }

                var tags = SplitTags(content).ToList();
                var shuffledTags = ShuffleDatasetTags(tags, fixedTagCount, out bool changed);
                if (!changed)
                {
                    continue;
                }

                try
                {
                    File.WriteAllText(targetPath, string.Join(", ", shuffledTags));
                }
                catch
                {
                    errorCount++;
                    continue;
                }

                updatedCount++;
                if (isSelectedItem)
                {
                    selectedUpdated = true;
                    selectedPath = targetPath;
                }
            }

            if (selectedUpdated && !string.IsNullOrWhiteSpace(selectedPath))
            {
                LoadTextFromFile(selectedPath);
            }

            ShowDatasetPromptShuffleResult(updatedCount, errorCount, fixedTagCount);
        }

        private bool TryPromptFixedTagCount(out int fixedTagCount)
        {
            fixedTagCount = 0;
            int parsedTagCount = 0;

            var owner = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive);

            var promptLabel = new TextBlock
            {
                Text = "固定前（",
                VerticalAlignment = VerticalAlignment.Center
            };
            var inputBox = new TextBox
            {
                Text = "0",
                MinWidth = 64,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var suffixLabel = new TextBlock
            {
                Text = "）个tag",
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var inputRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };
            inputRow.Children.Add(promptLabel);
            inputRow.Children.Add(inputBox);
            inputRow.Children.Add(suffixLabel);

            var okButton = new Button
            {
                Content = "确定",
                IsDefault = true,
                MinWidth = 72,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancelButton = new Button
            {
                Content = "取消",
                IsCancel = true,
                MinWidth = 72
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var panel = new StackPanel
            {
                Margin = new Thickness(16, 14, 16, 14)
            };
            panel.Children.Add(inputRow);
            panel.Children.Add(buttons);

            var dialog = new Window
            {
                Title = "打乱提示词（数据集）",
                Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = owner == null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner
            };

            if (owner != null && !ReferenceEquals(owner, dialog))
            {
                dialog.Owner = owner;
            }

            okButton.Click += (_, _) =>
            {
                if (!int.TryParse(inputBox.Text?.Trim(), out int parsed) || parsed < 0)
                {
                    System.Windows.MessageBox.Show(dialog, "请输入大于等于 0 的整数。", "提示",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    inputBox.Focus();
                    inputBox.SelectAll();
                    return;
                }

                parsedTagCount = parsed;
                dialog.DialogResult = true;
            };

            dialog.Loaded += (_, _) =>
            {
                inputBox.Focus();
                inputBox.SelectAll();
            };

            bool confirmed = dialog.ShowDialog() == true;
            fixedTagCount = confirmed ? parsedTagCount : 0;
            return confirmed;
        }

        private bool CanApplyDatasetTagAction(TagTokenViewModel? token)
        {
            return token != null && DataItemsWithOperations.Any();
        }

        private void ApplyDatasetTagUpdate(TagTokenViewModel? token, DatasetTagUpdateMode mode)
        {
            if (!CanApplyDatasetTagAction(token))
            {
                return;
            }

            var normalized = TagDictionaryService.NormalizeTag(token!.Text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            int updatedCount = 0;
            int createdCount = 0;
            int errorCount = 0;
            bool selectedUpdated = false;
            string? selectedPath = null;

            var selectedItem = SelectedDataItem;
            string? selectedContent = null;
            if (selectedItem != null)
            {
                CommitPromptEditorState();
                selectedContent = CurrentTextContent ?? string.Empty;
            }

            foreach (var item in DataItemsWithOperations)
            {
                string? targetPath = GetDatasetTagTextPath(item);
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    continue;
                }

                bool exists = File.Exists(targetPath);
                if (!exists && mode != DatasetTagUpdateMode.Add)
                {
                    continue;
                }

                string content = string.Empty;
                bool isSelectedItem = ReferenceEquals(item, selectedItem);
                if (isSelectedItem && selectedContent != null)
                {
                    content = selectedContent;
                }
                else if (exists)
                {
                    try
                    {
                        content = File.ReadAllText(targetPath);
                    }
                    catch
                    {
                        errorCount++;
                        continue;
                    }
                }

                var tags = SplitTags(content).ToList();
                var updatedTags = UpdateDatasetTags(tags, normalized, mode, out bool changed);
                if (!changed)
                {
                    continue;
                }

                try
                {
                    File.WriteAllText(targetPath, string.Join(", ", updatedTags));
                }
                catch
                {
                    errorCount++;
                    continue;
                }

                updatedCount++;
                if (!exists && string.IsNullOrWhiteSpace(item.TextPath))
                {
                    item.TextPath = targetPath;
                    createdCount++;
                }

                if (isSelectedItem)
                {
                    selectedUpdated = true;
                    selectedPath = targetPath;
                }
            }

            if (selectedUpdated && !string.IsNullOrWhiteSpace(selectedPath))
            {
                LoadTextFromFile(selectedPath);
            }

            ShowDatasetTagUpdateResult(mode, updatedCount, createdCount, errorCount);
        }

        private static string? GetDatasetTagTextPath(DatasetDataItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.TextPath))
            {
                return item.TextPath;
            }

            if (!string.IsNullOrWhiteSpace(item.ImagePath))
            {
                return System.IO.Path.ChangeExtension(item.ImagePath, ".txt");
            }

            return null;
        }

        private static List<string> UpdateDatasetTags(List<string> tags, string normalizedTag, DatasetTagUpdateMode mode, out bool changed)
        {
            changed = false;
            if (string.IsNullOrWhiteSpace(normalizedTag))
            {
                return tags;
            }

            bool IsMatch(string tag) => string.Equals(tag, normalizedTag, StringComparison.OrdinalIgnoreCase);

            if (mode == DatasetTagUpdateMode.Add)
            {
                if (tags.Any(IsMatch))
                {
                    return tags;
                }

                var added = new List<string>(tags) { normalizedTag };
                changed = true;
                return added;
            }

            if (tags.Count == 0)
            {
                return tags;
            }

            var matches = new List<string>();
            var others = new List<string>();
            foreach (var tag in tags)
            {
                if (IsMatch(tag))
                {
                    matches.Add(tag);
                }
                else
                {
                    others.Add(tag);
                }
            }

            if (matches.Count == 0)
            {
                return tags;
            }

            List<string> updated;
            switch (mode)
            {
                case DatasetTagUpdateMode.MoveToStart:
                    updated = matches.Concat(others).ToList();
                    break;
                case DatasetTagUpdateMode.MoveToEnd:
                    updated = others.Concat(matches).ToList();
                    break;
                case DatasetTagUpdateMode.Remove:
                    updated = others;
                    break;
                default:
                    updated = tags;
                    break;
            }

            changed = !AreTagListsEqual(tags, updated);
            return changed ? updated : tags;
        }

        private static bool AreTagListsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private List<string> ShuffleDatasetTags(List<string> tags, int fixedTagCount, out bool changed)
        {
            changed = false;
            if (tags.Count <= 1)
            {
                return tags;
            }

            int keepCount = Math.Clamp(fixedTagCount, 0, tags.Count);
            int shuffleCount = tags.Count - keepCount;
            if (shuffleCount <= 1)
            {
                return tags;
            }

            bool suffixAllSame = true;
            string firstValue = tags[keepCount];
            for (int i = keepCount + 1; i < tags.Count; i++)
            {
                if (!string.Equals(tags[i], firstValue, StringComparison.Ordinal))
                {
                    suffixAllSame = false;
                    break;
                }
            }

            if (suffixAllSame)
            {
                return tags;
            }

            var shuffled = new List<string>(tags);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                for (int i = shuffled.Count - 1; i > keepCount; i--)
                {
                    int swapIndex = _random.Next(keepCount, i + 1);
                    (shuffled[i], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[i]);
                }

                if (!AreTagListsEqual(tags, shuffled))
                {
                    changed = true;
                    return shuffled;
                }

                shuffled = new List<string>(tags);
            }

            string first = shuffled[keepCount];
            for (int i = keepCount; i < shuffled.Count - 1; i++)
            {
                shuffled[i] = shuffled[i + 1];
            }
            shuffled[^1] = first;

            changed = !AreTagListsEqual(tags, shuffled);
            return changed ? shuffled : tags;
        }

        private void ShowDatasetTagUpdateResult(DatasetTagUpdateMode mode, int updatedCount, int createdCount, int errorCount)
        {
            if (updatedCount == 0 && errorCount == 0)
            {
                System.Windows.MessageBox.Show("未找到需要处理的标签。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            string actionName = mode switch
            {
                DatasetTagUpdateMode.MoveToStart => "移到最前(数据集)",
                DatasetTagUpdateMode.MoveToEnd => "移到最后(数据集)",
                DatasetTagUpdateMode.Remove => "删除(数据集)",
                DatasetTagUpdateMode.Add => "添加(数据集)",
                _ => "操作"
            };

            string message = $"{actionName} 完成。\n更新: {updatedCount} 个文件";
            if (createdCount > 0)
            {
                message += $"\n新增文件: {createdCount} 个";
            }
            if (errorCount > 0)
            {
                message += $"\n失败: {errorCount} 个";
            }

            System.Windows.MessageBox.Show(message, "完成", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private void ShowDatasetPromptShuffleResult(int updatedCount, int errorCount, int fixedTagCount)
        {
            if (updatedCount == 0 && errorCount == 0)
            {
                System.Windows.MessageBox.Show("未找到可打乱的提示词。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            string message = $"打乱提示词（数据集）完成。\n固定前 {Math.Max(0, fixedTagCount)} 个tag\n更新: {updatedCount} 个文件";
            if (errorCount > 0)
            {
                message += $"\n失败: {errorCount} 个";
            }

            System.Windows.MessageBox.Show(message, "完成", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        public void CommitPendingTags(bool flushAll)
        {
            var text = PendingTagText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                PendingTagText = string.Empty;
                ClearSuggestions();
                return;
            }

            var parts = text.Split(TagSeparators, StringSplitOptions.None);
            var lastIndex = parts.Length - 1;
            var endIndex = flushAll ? parts.Length : Math.Max(0, lastIndex);

            for (int i = 0; i < endIndex; i++)
            {
                AddTagToken(parts[i]);
            }

            if (flushAll)
            {
                PendingTagText = string.Empty;
            }
            else
            {
                PendingTagText = lastIndex >= 0 ? TagDictionaryService.NormalizeTag(parts[lastIndex]) : string.Empty;
            }

            ClearSuggestions();
        }

        public (string Remainder, int AddedCount) CommitPendingTagsFromInput(string input, bool flushAll)
        {
            var text = input ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                PendingTagText = string.Empty;
                ClearSuggestions();
                return (PendingTagText, 0);
            }

            var parts = text.Split(TagSeparators, StringSplitOptions.None);
            var lastIndex = parts.Length - 1;
            var endIndex = flushAll ? parts.Length : Math.Max(0, lastIndex);
            int addedCount = 0;

            for (int i = 0; i < endIndex; i++)
            {
                if (AddTagToken(parts[i]))
                {
                    addedCount++;
                }
            }

            string remainder;
            if (flushAll)
            {
                remainder = string.Empty;
            }
            else
            {
                remainder = lastIndex >= 0 ? TagDictionaryService.NormalizeTag(parts[lastIndex]) : string.Empty;
            }

            if (addedCount == 0)
            {
                bool hasTokenChars = text.Any(ch => !char.IsWhiteSpace(ch) && Array.IndexOf(TagSeparators, ch) < 0);
                if (hasTokenChars)
                {
                    remainder = text;
                }
            }

            PendingTagText = remainder;
            ClearSuggestions();
            return (PendingTagText, addedCount);
        }

        public void ClearSuggestions()
        {
            _suggestionCancellation?.Cancel();
            _suggestionCancellation = null;
            Interlocked.Increment(ref _suggestionRequestId);
            TagSuggestions.Clear();
            IsSuggestionOpen = false;
        }

        public async Task RefreshSuggestionsAsync(string input)
        {
            if (Interlocked.Exchange(ref _suppressSuggestionRefresh, 0) != 0)
            {
                return;
            }

            var normalized = TagDictionaryService.NormalizeTag(input);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                ClearSuggestions();
                return;
            }

            _suggestionCancellation?.Cancel();
            _suggestionCancellation = new CancellationTokenSource();
            var cancellationToken = _suggestionCancellation.Token;
            var requestId = Interlocked.Increment(ref _suggestionRequestId);

            try
            {
                bool searchTranslation = TagDictionaryService.ContainsCjk(input);
                var translationQuery = searchTranslation ? TagDictionaryService.ExtractCjkQuery(input) : normalized;
                if (searchTranslation && string.IsNullOrWhiteSpace(translationQuery))
                {
                    translationQuery = normalized;
                }
                var results = searchTranslation
                    ? await _tagDictionaryService.GetSuggestionsByTranslationAsync(translationQuery, 20, null, cancellationToken)
                    : await _tagDictionaryService.GetSuggestionsAsync(normalized, 20, null, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (requestId != _suggestionRequestId)
                {
                    return;
                }

                TagSuggestions.Clear();
                foreach (var suggestion in results)
                {
                    TagSuggestions.Add(suggestion);
                }

                if (requestId != _suggestionRequestId)
                {
                    return;
                }

                IsSuggestionOpen = TagSuggestions.Count > 0;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                IsSuggestionOpen = false;
            }
        }

        public void ApplySuggestion(TagSuggestion suggestion, TagTokenViewModel? token)
        {
            if (suggestion == null)
            {
                return;
            }

            if (token == null)
            {
                PendingTagText = suggestion.Tag;
            }
            else
            {
                token.EditingText = suggestion.Tag;
            }
        }

        private async Task UpsertTagTranslationAsync()
        {
            var normalized = TagDictionaryService.NormalizeTag(ManualTagInput);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var translation = (ManualTagTranslation ?? string.Empty).Trim();

            try
            {
                await _tagDictionaryService.UpsertTagTranslationAsync(normalized, translation, "custom", _tagCancellation.Token);
                foreach (var token in TagTokens)
                {
                    if (string.Equals(token.Text, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        token.Translation = translation;
                    }
                }

                ManualTagInput = string.Empty;
                ManualTagTranslation = string.Empty;
                _ = RefreshTranslationCountsAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存标签翻译失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task TranslateAllMissingTagsAsync()
        {
            if (IsTranslatingAllTags)
            {
                return;
            }

            IsTranslatingAllTags = true;
            _translationCancellation?.Cancel();
            _translationCancellation?.Dispose();
            _translationCancellation = new CancellationTokenSource();
            var cancellationToken = _translationCancellation.Token;

            try
            {
                await Task.Run(async () =>
                {
                    const int batchSize = 200;
                    const int translateChunkSize = 100;
                    int translatedSinceRefresh = 0;
                    int emptyBatchCount = 0;
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var batch = await _tagDictionaryService.GetMissingTranslationBatchAsync(batchSize, cancellationToken).ConfigureAwait(false);
                        if (batch.Count == 0)
                        {
                            break;
                        }

                        int translatedInBatch = 0;
                        int currentChunkSize = translateChunkSize;
                        int offset = 0;
                        while (offset < batch.Count)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int size = Math.Min(currentChunkSize, batch.Count - offset);
                            var chunk = batch.Skip(offset).Take(size).ToList();
                            IReadOnlyDictionary<string, string> translatedMap;
                            try
                            {
                                translatedMap = await _tagDictionaryService.TranslateAndCacheBatchAsync(
                                    chunk,
                                    cancellationToken,
                                    allowSequentialFallback: false).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch
                            {
                                translatedMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            }

                            if (translatedMap.Count == 0)
                            {
                                if (currentChunkSize != 1)
                                {
                                    currentChunkSize = 1;
                                    continue;
                                }

                                offset += 1;
                                continue;
                            }

                            foreach (var pair in translatedMap)
                            {
                                UpdateTokenTranslation(pair.Key, pair.Value);
                                translatedInBatch++;
                                translatedSinceRefresh++;
                                if (translatedSinceRefresh >= 10)
                                {
                                    await RefreshTranslationCountsAsync().ConfigureAwait(false);
                                    translatedSinceRefresh = 0;
                                }
                            }

                            offset += size;
                            if (currentChunkSize == 1)
                            {
                                currentChunkSize = translateChunkSize;
                            }
                        }

                        if (translatedInBatch == 0)
                        {
                            emptyBatchCount++;
                            if (emptyBatchCount >= 3)
                            {
                                break;
                            }

                            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        emptyBatchCount = 0;

                        if (translatedSinceRefresh > 0)
                        {
                            await RefreshTranslationCountsAsync().ConfigureAwait(false);
                            translatedSinceRefresh = 0;
                        }
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"翻译未翻译标签失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsTranslatingAllTags = false;
                _ = RefreshTranslationCountsAsync();
            }
        }

        private async Task RefreshTranslationCountsAsync()
        {
            try
            {
                var counts = await _tagDictionaryService.GetTranslationCountsAsync(_tagCancellation.Token).ConfigureAwait(false);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TranslatedTagCount = counts.Translated;
                    UntranslatedTagCount = counts.Untranslated;
                });
            }
            catch
            {
            }
        }

        public void SuppressSuggestionRefresh()
        {
            Interlocked.Exchange(ref _suppressSuggestionRefresh, 1);
        }

        private async Task EnsureTranslationAsync(TagTokenViewModel token)
        {
            if (token == null || string.IsNullOrWhiteSpace(token.Text))
            {
                return;
            }

            try
            {
                token.IsTranslating = true;
                var translation = await _tagDictionaryService.TranslateAndCacheAsync(token.Text, _tagCancellation.Token);
                if (!string.IsNullOrWhiteSpace(translation))
                {
                    token.Translation = translation;
                }
            }
            catch
            {
            }
            finally
            {
                token.IsTranslating = false;
            }
        }

        private void DeleteDataItemFiles(DatasetDataItem item)
        {
            if (!string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
            {
                if (CurrentImageSource != null && _currentFilePath == item.ImagePath)
                {
                    CancelImageLoad();
                    ClearCurrentImagePreview();
                }
                File.Delete(item.ImagePath);
            }

            if (!string.IsNullOrEmpty(item.TextPath) && File.Exists(item.TextPath))
            {
                File.Delete(item.TextPath);
            }
            else if (!string.IsNullOrEmpty(item.ImagePath))
            {
                string associatedTxtPath = System.IO.Path.ChangeExtension(item.ImagePath, ".txt");
                if (File.Exists(associatedTxtPath))
                {
                    File.Delete(associatedTxtPath);
                }
            }

            ClearCachedState(item);
        }

        private void MoveDataItemFiles(DatasetDataItem item, string targetFolder)
        {
            bool hasRectEdits = TryGetCachedRects(item, out List<Rect> rectsToSave);
            bool hasTextEdits = TryGetCachedTextContent(item, out _);

            if (!string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
            {
                string sourceImagePath = item.ImagePath;
                string targetImagePath = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(sourceImagePath));

                if (hasRectEdits)
                {
                    if (!SaveImageWithRects(sourceImagePath, rectsToSave, targetImagePath))
                    {
                        throw new IOException($"保存修改后的图片失败: {System.IO.Path.GetFileName(sourceImagePath)}");
                    }

                    File.Delete(sourceImagePath);
                }
                else
                {
                    File.Move(sourceImagePath, targetImagePath);
                }

                string? sourceTxtPath = GetDatasetTagTextPath(item);
                string targetTxtPath = BuildTargetTextPath(item, targetFolder, targetImagePath);

                if (hasRectEdits || hasTextEdits)
                {
                    string finalPromptContent = hasRectEdits
                        ? DatasetPromptHelper.BuildPromptContent(GetPromptContentForPersistence(item), new[] { DatasetPromptHelper.BlackBoxTag })
                        : GetPromptContentForPersistence(item);

                    WritePromptFile(targetTxtPath, finalPromptContent);
                    DeleteSourceTextFile(sourceTxtPath, targetTxtPath);
                    if (string.IsNullOrWhiteSpace(item.TextPath))
                    {
                        item.TextPath = targetTxtPath;
                    }
                }
                else if (!string.IsNullOrEmpty(sourceTxtPath) && File.Exists(sourceTxtPath))
                {
                    File.Move(sourceTxtPath, targetTxtPath);
                }

                ClearCachedState(item);
                return;
            }

            if (!string.IsNullOrEmpty(item.TextPath) && (File.Exists(item.TextPath) || hasTextEdits))
            {
                string targetTxtPath = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(item.TextPath));

                if (hasTextEdits)
                {
                    WritePromptFile(targetTxtPath, GetPromptContentForPersistence(item));
                    DeleteSourceTextFile(item.TextPath, targetTxtPath);
                }
                else
                {
                    File.Move(item.TextPath, targetTxtPath);
                }

                ClearCachedState(item);
            }
        }

        private async void RandomCopy()
        {
            if (!DataItemsWithOperations.Any()) return;

            int index = _random.Next(DataItemsWithOperations.Count);
            SelectedDataItem = DataItemsWithOperations[index];

            await ClipboardHelper.TrySetTextAsync(CurrentTextContent ?? string.Empty);
        }

        private async void CopyNextData()
        {
            if (!DataItemsWithOperations.Any()) return;

            int currentIndex = SelectedDataItem != null ? DataItemsWithOperations.IndexOf(SelectedDataItem) : -1;
            int nextIndex = (currentIndex + 1) % DataItemsWithOperations.Count;
            SelectedDataItem = DataItemsWithOperations[nextIndex];

            await ClipboardHelper.TrySetTextAsync(CurrentTextContent ?? string.Empty);
        }

        private void SavePrompt()
        {
            if (SelectedDataItem == null)
            {
                return;
            }

            CommitPromptEditorState();

            string? targetPath = SelectedDataItem.TextPath;
            if (string.IsNullOrEmpty(targetPath))
            {
                if (!string.IsNullOrEmpty(SelectedDataItem.ImagePath))
                {
                    targetPath = System.IO.Path.ChangeExtension(SelectedDataItem.ImagePath, ".txt");
                }
                else
                {
                    System.Windows.MessageBox.Show("当前数据没有可保存的 Prompt 文件。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }
            }

            try
            {
                File.WriteAllText(targetPath, CurrentTextContent ?? string.Empty);
                _cachedTextEdits.Remove(targetPath);

                if (string.IsNullOrEmpty(SelectedDataItem.TextPath))
                {
                    SelectedDataItem.TextPath = targetPath;
                }

                System.Windows.MessageBox.Show("Prompt 已保存！", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存 Prompt 失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        #endregion

        #region 分类功能

        private void SetImageCategory(string category)
        {
            if (SelectedDataItem != null && !string.IsNullOrEmpty(category))
            {
                SelectedDataItem.Operation = category;
                
                if (AutoNext)
                {
                    NextImage();
                }
            }
        }

        private void SelectCategoryFolder(string categoryNumber)
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = $"请选择分类{categoryNumber}的目标文件夹"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                switch (categoryNumber)
                {
                    case "1": Category1Path = dialog.FileName; break;
                    case "2": Category2Path = dialog.FileName; break;
                    case "3": Category3Path = dialog.FileName; break;
                    case "4": Category4Path = dialog.FileName; break;
                    case "5": Category5Path = dialog.FileName; break;
                }
            }
        }

        private void PreviousImage()
        {
            if (DataItemsWithOperations.Any() && _currentIndex > 0)
            {
                _currentIndex--;
                SelectedDataItem = DataItemsWithOperations[_currentIndex];
            }
        }

        private void NextImage()
        {
            if (DataItemsWithOperations.Any() && _currentIndex < DataItemsWithOperations.Count - 1)
            {
                _currentIndex++;
                SelectedDataItem = DataItemsWithOperations[_currentIndex];
            }
        }

        private void UndoOperation()
        {
            if (SelectedDataItem != null)
            {
                SelectedDataItem.Operation = "";
                
                if (AutoNext)
                {
                    NextImage();
                }
            }
        }

        private void DeleteImage()
        {
            if (SelectedDataItem != null)
            {
                SelectedDataItem.Operation = "删";
                
                if (AutoNext)
                {
                    NextImage();
                }
            }
        }

        

        private string GetCategoryFolder(string category)
        {
            return category switch
            {
                "1" => Category1Path,
                "2" => Category2Path,
                "3" => Category3Path,
                "4" => Category4Path,
                "5" => Category5Path,
                _ => ""
            };
        }

        private bool IsCategorySelected(string? category)
        {
            return category switch
            {
                "1" => IsCategory1Selected,
                "2" => IsCategory2Selected,
                "3" => IsCategory3Selected,
                "4" => IsCategory4Selected,
                "5" => IsCategory5Selected,
                _ => true
            };
        }

        #endregion

        #region 键盘处理

        public void HandleKeyDown(Key key)
        {
            switch (key)
            {
                case Key.D1:
                    SetImageCategory("1");
                    break;
                case Key.D2:
                    SetImageCategory("2");
                    break;
                case Key.D3:
                    SetImageCategory("3");
                    break;
                case Key.D4:
                    SetImageCategory("4");
                    break;
                case Key.D5:
                    SetImageCategory("5");
                    break;
                case Key.Q:
                    RandomCopy();
                    break;
                case Key.E:
                    CopyNextData();
                    break;
                case Key.Z:
                    UndoOperation();
                    break;
                case Key.X:
                    SavePrompt();
                    break;
                case Key.C:
                    DeleteImage();
                    break;
            }
        }

        #endregion

        #region 核心逻辑 (加载、缓存、重置)

        private async Task LoadImageFromFileAsync(string filePath)
        {
            _imageLoadCancellation?.Cancel();
            _imageLoadCancellation?.Dispose();
            _imageLoadCancellation = new CancellationTokenSource();
            var cancellationToken = _imageLoadCancellation.Token;
            int requestId = Interlocked.Increment(ref _imageLoadRequestId);

            try
            {
                var bitmap = await Task.Run(() => LoadBitmapFromFile(filePath, cancellationToken), cancellationToken);
                if (cancellationToken.IsCancellationRequested || requestId != _imageLoadRequestId)
                {
                    return;
                }

                _originalBitmap = bitmap;
                CurrentImageSource = bitmap;
                _currentFilePath = filePath; 

                ResetState(); 
                LoadEditsFromCache(); 

                
                OnCanvasResetRequired?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"加载图像失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                CurrentImageSource = null;
                _currentFilePath = null;
                ResetState();
            }
        }

        private static BitmapImage LoadBitmapFromFile(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            cancellationToken.ThrowIfCancellationRequested();
            return bitmap;
        }

        private void SaveCurrentSelectionEditsToCache()
        {
            SaveChangesToCache();
            SaveTextChangesToCache(_selectedDataItem);
        }

        private void SaveChangesToCache()
        {
            if (string.IsNullOrEmpty(_currentFilePath)) return;

            if (_finalRects.Any())
            {
                _cachedEdits[_currentFilePath] = new List<Rect>(_finalRects);
            }
            else if (_cachedEdits.ContainsKey(_currentFilePath))
            {
                _cachedEdits.Remove(_currentFilePath);
            }
        }

        private void SaveTextChangesToCache(DatasetDataItem? item)
        {
            if (item == null)
            {
                return;
            }

            string? cacheKey = GetDatasetTagTextPath(item);
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return;
            }

            CommitPromptEditorState();

            string currentContent = CurrentTextContent ?? string.Empty;
            bool fileExists = File.Exists(cacheKey);
            if (!fileExists && string.IsNullOrWhiteSpace(currentContent))
            {
                _cachedTextEdits.Remove(cacheKey);
                return;
            }

            if (fileExists)
            {
                try
                {
                    string existingContent = File.ReadAllText(cacheKey);
                    if (DatasetPromptHelper.AreEquivalent(existingContent, currentContent))
                    {
                        _cachedTextEdits.Remove(cacheKey);
                        return;
                    }
                }
                catch
                {
                    _cachedTextEdits[cacheKey] = currentContent;
                    return;
                }
            }

            _cachedTextEdits[cacheKey] = currentContent;
        }

        private bool HasPendingChanges(DatasetDataItem item)
        {
            return TryGetCachedRects(item, out _) || TryGetCachedTextContent(item, out _);
        }

        private bool TryGetCachedRects(DatasetDataItem item, out List<Rect> rects)
        {
            rects = new List<Rect>();
            if (string.IsNullOrWhiteSpace(item.ImagePath) ||
                !_cachedEdits.TryGetValue(item.ImagePath, out List<Rect>? cachedRects) ||
                cachedRects.Count == 0)
            {
                return false;
            }

            rects = new List<Rect>(cachedRects);
            return true;
        }

        private bool TryGetCachedTextContent(DatasetDataItem item, out string content)
        {
            content = string.Empty;
            string? textPath = GetDatasetTagTextPath(item);
            return !string.IsNullOrWhiteSpace(textPath) && _cachedTextEdits.TryGetValue(textPath, out content);
        }

        private string GetPromptContentForPersistence(DatasetDataItem item)
        {
            if (TryGetCachedTextContent(item, out string cachedContent))
            {
                return cachedContent;
            }

            string? textPath = GetDatasetTagTextPath(item);
            if (!string.IsNullOrWhiteSpace(textPath) && File.Exists(textPath))
            {
                return File.ReadAllText(textPath);
            }

            return string.Empty;
        }

        private void ClearCachedState(DatasetDataItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.ImagePath))
            {
                _cachedEdits.Remove(item.ImagePath);
            }

            string? textPath = GetDatasetTagTextPath(item);
            if (!string.IsNullOrWhiteSpace(textPath))
            {
                _cachedTextEdits.Remove(textPath);
            }
        }

        private static void WritePromptFile(string targetPath, string content)
        {
            string? directory = System.IO.Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(targetPath, content);
        }

        private static string BuildTargetTextPath(DatasetDataItem item, string targetFolder, string? targetImagePath = null)
        {
            if (!string.IsNullOrWhiteSpace(item.TextPath))
            {
                return System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(item.TextPath));
            }

            if (!string.IsNullOrWhiteSpace(targetImagePath))
            {
                return System.IO.Path.ChangeExtension(targetImagePath, ".txt");
            }

            if (!string.IsNullOrWhiteSpace(item.ImagePath))
            {
                return System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileNameWithoutExtension(item.ImagePath) + ".txt");
            }

            throw new InvalidOperationException("当前数据没有可用的文本路径。");
        }

        private static void DeleteSourceTextFile(string? sourceTextPath, string targetTextPath)
        {
            if (string.IsNullOrWhiteSpace(sourceTextPath) || !File.Exists(sourceTextPath))
            {
                return;
            }

            string sourceFullPath = System.IO.Path.GetFullPath(sourceTextPath);
            string targetFullPath = System.IO.Path.GetFullPath(targetTextPath);
            if (string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            File.Delete(sourceTextPath);
        }

        private void SaveDataItemInPlace(DatasetDataItem item)
        {
            bool hasRectEdits = TryGetCachedRects(item, out List<Rect> rectsToSave);
            bool hasTextEdits = TryGetCachedTextContent(item, out _);

            if (!hasRectEdits && !hasTextEdits)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
            {
                if (hasRectEdits && !SaveImageWithRects(item.ImagePath, rectsToSave, item.ImagePath))
                {
                    throw new IOException($"保存修改后的图片失败: {System.IO.Path.GetFileName(item.ImagePath)}");
                }

                string targetTextPath = GetDatasetTagTextPath(item) ?? System.IO.Path.ChangeExtension(item.ImagePath, ".txt");
                string finalPromptContent = hasRectEdits
                    ? DatasetPromptHelper.BuildPromptContent(GetPromptContentForPersistence(item), new[] { DatasetPromptHelper.BlackBoxTag })
                    : GetPromptContentForPersistence(item);

                WritePromptFile(targetTextPath, finalPromptContent);
                if (string.IsNullOrWhiteSpace(item.TextPath))
                {
                    item.TextPath = targetTextPath;
                }

                ClearCachedState(item);
                return;
            }

            string? textPath = GetDatasetTagTextPath(item);
            if (string.IsNullOrWhiteSpace(textPath))
            {
                throw new InvalidOperationException("当前数据没有可保存的文本路径。");
            }

            WritePromptFile(textPath, GetPromptContentForPersistence(item));
            if (string.IsNullOrWhiteSpace(item.TextPath))
            {
                item.TextPath = textPath;
            }

            ClearCachedState(item);
        }

        private void LoadEditsFromCache()
        {
            if (!string.IsNullOrEmpty(_currentFilePath) && _cachedEdits.TryGetValue(_currentFilePath, out List<Rect> cachedRects))
            {
                _finalRects.AddRange(cachedRects);
            }
        }
        
        
        public List<Rect> GetCurrentRects()
        {
            return new List<Rect>(_finalRects);
        }
        
        
        public void AddDrawnRectangle(System.Windows.Shapes.Rectangle rectangle)
        {
            if (!_drawnRectangles.Contains(rectangle))
            {
                _drawnRectangles.Add(rectangle);
            }
        }
        
        
        public void ClearDrawnRectangles()
        {
            _drawnRectangles.Clear();
        }

        private void ResetState()
        {
            _finalRects.Clear();
            _drawnRectangles.Clear();
            _currentDrawingRectangle = null;
            _isPanning = false;
            _isDrawing = false;
        }

        private bool SaveImageWithRects(string imagePath, List<Rect> rects, string savePath)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze(); 

                DrawingVisual drawingVisual = new DrawingVisual();
                using (DrawingContext dc = drawingVisual.RenderOpen())
                {
                    dc.DrawImage(bmp, new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
                    foreach (var rect in rects)
                    {
                        dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, rect);
                    }
                }

                RenderTargetBitmap rtb = new RenderTargetBitmap(bmp.PixelWidth, bmp.PixelHeight, bmp.DpiX, bmp.DpiY, PixelFormats.Pbgra32);
                rtb.Render(drawingVisual);
                rtb.Freeze();

                BitmapEncoder encoder = CreateBitmapEncoder(savePath);
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (FileStream fs = new FileStream(savePath, FileMode.Create))
                {
                    encoder.Save(fs);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存文件 {imagePath} 失败: {ex.Message}");
                return false;
            }
        }

        private static BitmapEncoder CreateBitmapEncoder(string savePath)
        {
            string extension = System.IO.Path.GetExtension(savePath).ToLowerInvariant();
            return extension switch
            {
                ".jpg" => new JpegBitmapEncoder { QualityLevel = 95 },
                ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                ".bmp" => new BmpBitmapEncoder(),
                ".gif" => new GifBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };
        }

        #endregion

        #region 事件处理 (鼠标与键盘) - 需要从View调用

        public void HandleMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e, Canvas mainCanvas, Canvas transformGroupCanvas, System.Windows.Controls.Image myImage)
        {
            if (myImage.Source == null) return;

            if (e.OriginalSource is System.Windows.Shapes.Rectangle clickedRectangle && _drawnRectangles.Contains(clickedRectangle))
            {
                int index = _drawnRectangles.IndexOf(clickedRectangle);
                if (index >= 0 && index < _finalRects.Count)
                {
                    _drawnRectangles.RemoveAt(index);
                    _finalRects.RemoveAt(index);
                    transformGroupCanvas.Children.Remove(clickedRectangle);
                }
                return;
            }

            _isDrawing = true;
            var transform = transformGroupCanvas.RenderTransform;
            _rectStartPoint = (transform?.Inverse != null)
                ? transform.Inverse.Transform(e.GetPosition(mainCanvas))
                : e.GetPosition(mainCanvas);

            _currentDrawingRectangle = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.Black,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 0, 0, 0)),
            };

            Canvas.SetLeft(_currentDrawingRectangle, _rectStartPoint.X);
            Canvas.SetTop(_currentDrawingRectangle, _rectStartPoint.Y);
            transformGroupCanvas.Children.Add(_currentDrawingRectangle);
            _drawnRectangles.Add(_currentDrawingRectangle);
            mainCanvas.CaptureMouse();
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Cross;
        }

        public void HandleMouseMove(System.Windows.Input.MouseEventArgs e, Canvas mainCanvas, Border imageContainer, TranslateTransform imagePanTransform, double imageScale)
        {
            if (_isPanning)
            {
                System.Windows.Point currentPoint = e.GetPosition(imageContainer);
                double deltaX = currentPoint.X - _panStartPoint.X;
                double deltaY = currentPoint.Y - _panStartPoint.Y;
                double newX = imagePanTransform.X + deltaX;
                double newY = imagePanTransform.Y + deltaY;
                double minX = 0, minY = 0, maxX = 0, maxY = 0;

                if (CurrentImageSource != null)
                {
                    double scaledWidth = CurrentImageSource.Width * imageScale;
                    double scaledHeight = CurrentImageSource.Height * imageScale;
                    if (scaledWidth > imageContainer.ActualWidth)
                        minX = imageContainer.ActualWidth - scaledWidth;
                    if (scaledHeight > imageContainer.ActualHeight)
                        minY = imageContainer.ActualHeight - scaledHeight;
                }

                imagePanTransform.X = Math.Max(minX, Math.Min(maxX, newX));
                imagePanTransform.Y = Math.Max(minY, Math.Min(maxY, newY));
                _panStartPoint = currentPoint;
            }

            if (_isDrawing && _currentDrawingRectangle != null)
            {
                var transformGroupCanvas = _currentDrawingRectangle.Parent as Canvas;
                var transform = transformGroupCanvas?.RenderTransform;
                System.Windows.Point currentPoint = (transform?.Inverse != null)
                    ? transform.Inverse.Transform(e.GetPosition(mainCanvas))
                    : e.GetPosition(mainCanvas);

                double x = Math.Min(_rectStartPoint.X, currentPoint.X);
                double y = Math.Min(_rectStartPoint.Y, currentPoint.Y);
                double width = Math.Abs(_rectStartPoint.X - currentPoint.X);
                double height = Math.Abs(_rectStartPoint.Y - currentPoint.Y);

                Canvas.SetLeft(_currentDrawingRectangle, x);
                Canvas.SetTop(_currentDrawingRectangle, y);
                _currentDrawingRectangle.Width = width;
                _currentDrawingRectangle.Height = height;
            }
        }

        public void HandleMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e, Canvas mainCanvas, Canvas transformGroupCanvas)
        {
            if (_isDrawing)
            {
                _isDrawing = false;
                if (_currentDrawingRectangle != null && _currentDrawingRectangle.Width > 0 && _currentDrawingRectangle.Height > 0)
                {
                    _finalRects.Add(new Rect(
                        Canvas.GetLeft(_currentDrawingRectangle), Canvas.GetTop(_currentDrawingRectangle),
                        _currentDrawingRectangle.Width, _currentDrawingRectangle.Height));
                    _currentDrawingRectangle.Fill = System.Windows.Media.Brushes.Black;
                    _currentDrawingRectangle.Stroke = null;
                }
                else if (_currentDrawingRectangle != null)
                {
                    _drawnRectangles.Remove(_currentDrawingRectangle);
                    transformGroupCanvas.Children.Remove(_currentDrawingRectangle);
                }
                _currentDrawingRectangle = null;
                mainCanvas.ReleaseMouseCapture();
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        public void HandleMouseRightButtonDown(System.Windows.Input.MouseButtonEventArgs e, Canvas mainCanvas, Border imageContainer)
        {
            if (CurrentImageSource == null) return;
            _isPanning = true;
            _panStartPoint = e.GetPosition(imageContainer);
            mainCanvas.CaptureMouse();
            Mouse.OverrideCursor = System.Windows.Input.Cursors.ScrollAll;
        }

        public void HandleMouseRightButtonUp(Canvas mainCanvas)
        {
            _isPanning = false;
            mainCanvas.ReleaseMouseCapture();
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow;
        }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed class NaturalStringComparer : IComparer<string>
        {
            public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

            [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
            private static extern int StrCmpLogicalW(string? x, string? y);

            public int Compare(string? x, string? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x is null)
                {
                    return -1;
                }

                if (y is null)
                {
                    return 1;
                }

                return StrCmpLogicalW(x, y);
            }
        }
    }
}
