using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CyanTools.Pipeline;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel
{
    public sealed class TrainingOutputOrganizerViewModel : INotifyPropertyChanged, IPipelineDirectoryNode, IPipelineRunnableViewModel
    {
        private const string ToolDisplayName = "\u8BAD\u7EC3\u8F93\u51FA\u6574\u7406";
        private const string AdapterFileName = "adapter_model.safetensors";
        private const int SequenceWidth = 6;
        private static readonly Regex NumberRegex = new Regex(@"\d+", RegexOptions.Compiled);

        private readonly StringBuilder _logBuilder = new StringBuilder();
        private string _inputFolder = string.Empty;
        private string _outputFolder = string.Empty;
        private string _logText = string.Empty;
        private bool _overwriteExisting;
        private bool _isProcessing;
        private bool _isWorkflowMode;
        private bool _hasInheritedInputPath;

        public TrainingOutputOrganizerViewModel()
        {
            BrowseInputFolderCommand = new RelayCommand(() => BrowseForFolder(path => InputFolder = path, "\u9009\u62E9\u8F93\u5165\u6587\u4EF6\u5939"), () => !IsProcessing);
            BrowseOutputFolderCommand = new RelayCommand(() => BrowseForFolder(path => OutputFolder = path, "\u9009\u62E9\u8F93\u51FA\u6587\u4EF6\u5939"), () => !IsProcessing);
            ConvertModelsCommand = new RelayCommand(async () => await ConvertModelsAsync(), () => !IsProcessing);
        }

        public string InputFolder
        {
            get => _inputFolder;
            set => SetField(ref _inputFolder, value ?? string.Empty, nameof(InputFolder));
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set => SetField(ref _outputFolder, value ?? string.Empty, nameof(OutputFolder));
        }

        public bool OverwriteExisting
        {
            get => _overwriteExisting;
            set => SetField(ref _overwriteExisting, value, nameof(OverwriteExisting));
        }

        public string LogText
        {
            get => _logText;
            private set => SetField(ref _logText, value, nameof(LogText));
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            private set
            {
                if (SetField(ref _isProcessing, value, nameof(IsProcessing)))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public PipelineDirectoryBehavior DirectoryBehavior => PipelineDirectoryBehavior.GeneratedOutput;

        public string PipelineInputPath
        {
            get => InputFolder;
            set => InputFolder = value ?? string.Empty;
        }

        public string PipelineOutputPath
        {
            get => OutputFolder;
            set => OutputFolder = value ?? string.Empty;
        }

        public bool IsWorkflowMode
        {
            get => _isWorkflowMode;
            set => SetField(ref _isWorkflowMode, value, nameof(IsWorkflowMode));
        }

        public bool HasInheritedInputPath
        {
            get => _hasInheritedInputPath;
            set => SetField(ref _hasInheritedInputPath, value, nameof(HasInheritedInputPath));
        }

        public ICommand BrowseInputFolderCommand { get; }

        public ICommand BrowseOutputFolderCommand { get; }

        public ICommand ConvertModelsCommand { get; }

        public Task RunFromPipelineAsync(PipelineContext context, CancellationToken token)
        {
            return ExecuteAsync(
                InputFolder,
                OutputFolder,
                message =>
                {
                    AppendLog(message);
                    context.AppendLog($"[{ToolDisplayName}] {message}");
                },
                token,
                showDialogOnError: false);
        }

        private Task ConvertModelsAsync()
        {
            return ExecuteAsync(InputFolder, OutputFolder, AppendLog, CancellationToken.None, showDialogOnError: true);
        }

        private async Task ExecuteAsync(string inputFolder, string outputFolder, Action<string> log, CancellationToken token, bool showDialogOnError)
        {
            if (IsProcessing)
            {
                return;
            }

            IsProcessing = true;
            ClearLog();

            try
            {
                await Task.Run(() => ConvertModelsCore(inputFolder, outputFolder, log, token), token).ConfigureAwait(false);
            }
            catch (Exception ex) when (showDialogOnError)
            {
                AppendLog($"\u5904\u7406\u5931\u8D25: {ex.Message}");
                MessageBox.Show(ex.Message, "\u9519\u8BEF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void ConvertModelsCore(string inputFolder, string outputFolder, Action<string> log, CancellationToken token)
        {
            var input = inputFolder.Trim();
            var output = outputFolder.Trim();

            if (string.IsNullOrWhiteSpace(input) || !Directory.Exists(input))
            {
                throw new DirectoryNotFoundException("\u8F93\u5165\u6587\u4EF6\u5939\u4E0D\u5B58\u5728\u3002");
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException("\u8BF7\u5148\u8BBE\u7F6E\u8F93\u51FA\u6587\u4EF6\u5939\u3002");
            }

            Directory.CreateDirectory(output);

            var candidates = Directory.EnumerateDirectories(input)
                .Select(path => new EpochFolder(path, Path.Combine(path, AdapterFileName)))
                .Where(item => File.Exists(item.SourceFile))
                .Select(item => TryGetEpochNumber(Path.GetFileName(item.FolderPath), out var epoch)
                    ? item with { Epoch = epoch }
                    : item)
                .ToList();

            if (candidates.Count == 0)
            {
                log($"\u672A\u5728\u4E00\u7EA7\u5B50\u6587\u4EF6\u5939\u4E2D\u627E\u5230 {AdapterFileName}\u3002");
                return;
            }

            var moved = 0;
            var skipped = 0;

            foreach (var item in candidates.OrderBy(x => x.Epoch ?? int.MaxValue).ThenBy(x => x.FolderPath, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();

                if (item.Epoch == null)
                {
                    skipped++;
                    log($"\u8DF3\u8FC7: {Path.GetFileName(item.FolderPath)} \u672A\u8BC6\u522B\u5230 epoch \u5E8F\u53F7\u3002");
                    continue;
                }

                var targetFileName = $"adapter_model-{item.Epoch.Value.ToString($"D{SequenceWidth}")}.safetensors";
                var targetPath = Path.Combine(output, targetFileName);

                if (File.Exists(targetPath) && !OverwriteExisting)
                {
                    skipped++;
                    log($"\u8DF3\u8FC7: {targetFileName} \u5DF2\u5B58\u5728\u3002");
                    continue;
                }

                MoveFile(item.SourceFile, targetPath, OverwriteExisting);
                moved++;
                log($"{Path.GetFileName(item.FolderPath)} -> {targetFileName}");
            }

            log($"\u5B8C\u6210: \u79FB\u52A8 {moved} \u4E2A\uFF0C\u8DF3\u8FC7 {skipped} \u4E2A\u3002");
        }

        private static bool TryGetEpochNumber(string folderName, out int epoch)
        {
            epoch = 0;
            var matches = NumberRegex.Matches(folderName ?? string.Empty);
            if (matches.Count == 0)
            {
                return false;
            }

            return int.TryParse(matches[^1].Value, out epoch) && epoch >= 0;
        }

        private static void MoveFile(string sourcePath, string targetPath, bool overwrite)
        {
            if (overwrite && File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            var sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
            var targetRoot = Path.GetPathRoot(Path.GetFullPath(targetPath));

            if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(sourcePath, targetPath);
                return;
            }

            File.Copy(sourcePath, targetPath);
            File.Delete(sourcePath);
        }

        private static void BrowseForFolder(Action<string> assignPath, string title)
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = title
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                assignPath(dialog.FileName);
            }
        }

        private void ClearLog()
        {
            _logBuilder.Clear();
            LogText = string.Empty;
        }

        private void AppendLog(string message)
        {
            void UpdateLog()
            {
                _logBuilder.AppendLine(message);
                LogText = _logBuilder.ToString();
            }

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                UpdateLog();
            }
            else
            {
                Application.Current?.Dispatcher?.Invoke(UpdateLog);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private sealed record EpochFolder(string FolderPath, string SourceFile)
        {
            public int? Epoch { get; init; }
        }
    }
}
