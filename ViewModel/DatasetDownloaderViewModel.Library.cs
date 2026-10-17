using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CyanTools.Utils;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace CyanTools.ViewModel;

public partial class DatasetDownloaderViewModel
{
    private string _managementDirectory = string.Empty;
    private string _materialFolderTemplate = "{name}_{id}";
    private string _outputFormat = "原格式";
    private bool _skipExisting = true;
    private bool _addingMaterial;
    private DatasetDownloadTask? _selectedMaterial;
    private readonly Dictionary<DatasetDownloadTask, LibraryConfiguration> _libraryConfigurations = new();
    private static readonly string[] LibraryFields = { nameof(InputText), nameof(SelectedMode), nameof(EnableDanbooru), nameof(EnablePixiv), nameof(EnableKemono), nameof(IncludeArtist), nameof(IncludeCopyright), nameof(IncludeCharacter), nameof(IncludeGeneral), nameof(IncludeMeta), nameof(DownloadIntervalSeconds), nameof(OutputFormat), nameof(SkipExisting) };
    public bool IsManualDownloader { get; internal set; }
    public string ManagementDirectory { get => _managementDirectory; set { _managementDirectory = value ?? ""; OnPropertyChanged(); RefreshLibrary(); _startStopCommand.RaiseCanExecuteChanged(); } }
    public string MaterialFolderTemplate { get => _materialFolderTemplate; set { _materialFolderTemplate = value ?? ""; OnPropertyChanged(); RefreshLibrary(); } }
    public string OutputFormat { get => _outputFormat; set { _outputFormat = value; OnPropertyChanged(); } }
    public bool SkipExisting { get => _skipExisting; set { _skipExisting = value; OnPropertyChanged(); } }
    public string[] OutputFormats { get; } = { "原格式", "PNG", "JPEG", "JPG", "BMP", "WEBP" };
    public string LibraryStatus { get; private set; } = "请选择管理目录";
    public DatasetDownloadTask? SelectedMaterial { get => _selectedMaterial; set { _selectedMaterial = value; OnPropertyChanged(); if (value != null && _libraryConfigurations.TryGetValue(value, out var config)) ApplyLibraryConfiguration(config); } }
    public ICommand BrowseManagementDirectoryCommand => new RelayCommand(_ => { var dialog = new CommonOpenFileDialog { IsFolderPicker = true }; if (dialog.ShowDialog() == CommonFileDialogResult.Ok) ManagementDirectory = dialog.FileName; });
    public ICommand RefreshLibraryCommand => new RelayCommand(_ => RefreshLibrary());
    public ICommand SaveMaterialCommand => new RelayCommand(_ => SaveSelectedMaterial(), _ => SelectedMaterial != null && !SelectedMaterial.IsRunning);

    public void RefreshLibrary()
    {
        if (!_persistCredentials || IsManualDownloader) return;
        try
        {
            var matcher = DatasetLibraryStorage.FolderMatcher(MaterialFolderTemplate);
            foreach (var item in _downloadTasks.Where(t => !t.IsRunning).ToArray()) { _downloadTasks.Remove(item); _libraryConfigurations.Remove(item); }
            if (!Directory.Exists(ManagementDirectory)) { LibraryStatus = "管理目录不存在或尚未选择"; return; }
            int errors = 0;
            foreach (var directory in Directory.EnumerateDirectories(Path.GetFullPath(ManagementDirectory)))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                var match = matcher.Match(Path.GetFileName(directory));
                if (!match.Success || _downloadTasks.Any(t => string.Equals(t.MaterialDirectory, directory, StringComparison.OrdinalIgnoreCase))) continue;
                try
                {
                    var file = ConfigPath(directory);
                    var config = File.Exists(file) ? System.Text.Json.JsonSerializer.Deserialize<LibraryConfiguration>(File.ReadAllText(file), SettingsSerializerOptions) ?? throw new InvalidDataException("配置为空") : CaptureLibraryConfiguration();
                    if (!File.Exists(file)) { config.Values[nameof(InputText)] = System.Text.Json.JsonSerializer.SerializeToElement(match.Groups["id"].Value); config.Values[nameof(SelectedMode)] = System.Text.Json.JsonSerializer.SerializeToElement(DatasetDownloadMode.ArtistId); }
                    ValidateLibraryConfiguration(config);
                    AddLibraryItem(directory, config);
                }
                catch { errors++; }
            }
            LibraryStatus = $"已载入 {_downloadTasks.Count} 个素材目录" + (errors > 0 ? $"；{errors} 个目录配置无法读取" : "");
        }
        catch (Exception ex) { LibraryStatus = ex.Message; }
        finally { OnPropertyChanged(nameof(LibraryStatus)); }
    }

    private LibraryConfiguration CaptureLibraryConfiguration() => new()
    {
        Values = LibraryFields.ToDictionary(name => name, name => System.Text.Json.JsonSerializer.SerializeToElement(GetType().GetProperty(name)!.GetValue(this))),
        PixivTokenId = SelectedPixivToken?.Id,
        ProxyNodeId = SelectedProxyNode.Id,
        DanbooruCredentialId = DatasetLibraryCredentials.Store(DanbooruToken)
    };

    private void ApplyLibraryConfiguration(LibraryConfiguration config)
    {

        foreach (var name in LibraryFields.OrderBy(name => name == nameof(SelectedMode) ? 0 : 1))
            if (config.Values.TryGetValue(name, out var value))
            {
                var property = GetType().GetProperty(name)!;
                property.SetValue(this, value.Deserialize(property.PropertyType));
            }
        SelectedPixivToken = PixivTokens.FirstOrDefault(t => t.Id == config.PixivTokenId);
        SelectedProxyNode = ProxyNodes.FirstOrDefault(p => p.Id == config.ProxyNodeId) ?? ProxySettingsStore.Shared.LocalNode;
        DanbooruToken = DatasetLibraryCredentials.Read(config.DanbooruCredentialId) ?? string.Empty;
    }

    private void ValidateLibraryConfiguration(LibraryConfiguration config)
    {
        if (config.Version != 1 || config.Values == null) throw new InvalidDataException("不支持的素材配置版本");
        foreach (var name in LibraryFields)
        {
            if (!config.Values.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
                throw new InvalidDataException($"素材配置缺少 {name}");
            _ = value.Deserialize(GetType().GetProperty(name)!.PropertyType);
        }
        if (!OutputFormats.Contains(config.Values[nameof(OutputFormat)].GetString())
            || !Enum.IsDefined(typeof(DatasetDownloadMode), config.Values[nameof(SelectedMode)].GetInt32()))
            throw new InvalidDataException("素材配置包含无效选项");
    }

    private static string ConfigPath(string directory) => Path.Combine(directory, "config", "dataset-downloader.json");
    private static void WriteLibraryConfiguration(string directory, LibraryConfiguration config)
    {
        var path = ConfigPath(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(config, SettingsSerializerOptions));
        File.Move(temporary, path, true);
    }

    private DatasetDownloadTask AddLibraryItem(string directory, LibraryConfiguration config)
    {
        bool Flag(string name) => config.Values.TryGetValue(name, out var value) && value.GetBoolean();
        var item = new DatasetDownloadTask(Path.GetFileName(directory), Flag(nameof(EnableDanbooru)), Flag(nameof(EnablePixiv)), Flag(nameof(EnableKemono)), task => _downloadTasks.Remove(task))
        {
            MaterialDirectory = directory,
            PixivId = config.Values.TryGetValue(nameof(InputText), out var input) ? input.GetString() ?? "" : "",
            IsRunning = false,
            StatusText = "待检查"
        };
        item.CheckCommand = new RelayCommand(async _ => await CheckMaterialAsync(item), _ => !item.IsRunning);
        _libraryConfigurations[item] = config;
        item.RefreshFolders(config.SelectedFolder);
        item.FolderSelectionChanged = folder =>
        {
            var saved = _libraryConfigurations[item];
            saved.SelectedFolder = folder;
            try { WriteLibraryConfiguration(item.MaterialDirectory, saved); }
            catch (Exception ex) { item.StatusText = $"文件夹选择保存失败：{ex.Message}"; }
        };
        _downloadTasks.Add(item);
        return item;
    }

    private void SaveSelectedMaterial()
    {
        if (SelectedMaterial is not { IsRunning: false } item) return;
        try
        {
            var config = CaptureLibraryConfiguration();

            config.SelectedFolder = item.SelectedFolder;
            config.Values[nameof(InputText)] = _libraryConfigurations[item].Values[nameof(InputText)];
            config.Values[nameof(SelectedMode)] = _libraryConfigurations[item].Values[nameof(SelectedMode)];
            WriteLibraryConfiguration(item.MaterialDirectory, config);
            _libraryConfigurations[item] = config;
            item.EnableDanbooru = EnableDanbooru; item.EnablePixiv = EnablePixiv; item.EnableKemono = EnableKemono;
            item.StatusText = "配置已保存";
        }
        catch (Exception ex) { item.StatusText = ex.Message; }
    }

    private async Task AddMaterialAsync()
    {
        if (_addingMaterial) return;
        _addingMaterial = true;
        try
        {
            DatasetLibraryStorage.FolderMatcher(MaterialFolderTemplate);
            var config = CaptureLibraryConfiguration();
            var root = Path.GetFullPath(ManagementDirectory);
            var template = MaterialFolderTemplate;
            var id = InputText.Trim();
            if (SelectedMode == DatasetDownloadMode.ArtistId && !id.All(char.IsDigit)) throw new ArgumentException("画师 ID 必须为数字。");
            var duplicate = _downloadTasks.FirstOrDefault(t => t.PixivId == id
                && string.Equals(Path.GetDirectoryName(t.MaterialDirectory), root, StringComparison.OrdinalIgnoreCase)
                && _libraryConfigurations[t].Values[nameof(SelectedMode)].GetInt32() == (int)SelectedMode);
            if (duplicate != null) { SelectedMaterial = duplicate; LibraryStatus = "素材已在库中，可保存参数或检查下载"; return; }
            string name = "unknown_artist";
            if (SelectedMode == DatasetDownloadMode.ArtistId)
            {
                LibraryStatus = "正在解析画师名称…"; OnPropertyChanged(nameof(LibraryStatus));
                ManagedMihomoService.ManagedMihomoLease? lease = null;
                DatasetDownloaderViewModel? resolver = null;
                try
                {
                    var proxy = SelectedProxyNode;
                    if (proxy.UsesManagedCore) { lease = await ManagedMihomoService.Shared.AcquireAsync(proxy, CancellationToken.None); proxy = lease.TransportNode; }
                    resolver = new DatasetDownloaderViewModel(proxy, false) { DanbooruToken = DanbooruToken, DownloadIntervalSeconds = DownloadIntervalSeconds };
                    name = await resolver.ResolveDanbooruArtistTagByPixivUserIdAsync(id, CancellationToken.None) ?? name;
                }
                finally { resolver?.DisposeLibraryTransport(); if (lease != null) await lease.DisposeAsync(); }
            }
            var folder = template.Replace("{id}", SanitizePathSegment(id, "unknown"), StringComparison.OrdinalIgnoreCase)
                .Replace("{name}", SanitizePathSegment(name, "unknown_artist"), StringComparison.OrdinalIgnoreCase)
                .Replace("{artist}", SanitizePathSegment(name, "unknown_artist"), StringComparison.OrdinalIgnoreCase)
                .Replace("{time}", DateTime.Now.ToString("yyyy年M月d日"), StringComparison.OrdinalIgnoreCase);
            var directory = Path.Combine(root, folder);
            if (File.Exists(ConfigPath(directory))) throw new IOException("该素材已存在，请刷新素材库。");
            Directory.CreateDirectory(Path.Combine(directory, "原图"));
            WriteLibraryConfiguration(directory, config);
            if (string.Equals(root, Path.GetFullPath(ManagementDirectory), StringComparison.OrdinalIgnoreCase) && template == MaterialFolderTemplate)
                SelectedMaterial = AddLibraryItem(directory, config);
            LibraryStatus = "已添加到素材库，点击检查开始下载";
        }
        catch (Exception ex) { LibraryStatus = $"添加失败：{ex.Message}"; }
        finally { _addingMaterial = false; OnPropertyChanged(nameof(LibraryStatus)); _startStopCommand.RaiseCanExecuteChanged(); }
    }

    private async Task CheckMaterialAsync(DatasetDownloadTask item)
    {
        if (item.IsRunning) return;
        if (ReferenceEquals(SelectedMaterial, item)) SaveSelectedMaterial();
        SelectedMaterial = item;
        if (EnablePixiv && (SelectedPixivToken == null || SelectedPixivToken.IsInUse)) { item.StatusText = "请选择可用 Pixiv 令牌并保存参数"; return; }
        if (!EnableDanbooru && !EnablePixiv && !EnableKemono) { item.StatusText = "请勾选下载来源并保存参数"; return; }
        var snapshot = CreateTaskSnapshot() with { OutputDirectory = Path.Combine(item.MaterialDirectory, "原图") };
        try { WriteLibraryConfiguration(item.MaterialDirectory, _libraryConfigurations[item]); }
        catch (Exception ex) { item.StatusText = $"配置保存失败：{ex.Message}"; return; }
        MaterialClassificationStore.MaterialLease materialLease;
        try
        {
            materialLease = MaterialClassificationStore.Acquire(item.MaterialDirectory);
            try { await Task.Run(materialLease.Restore); }
            catch { materialLease.Dispose(); throw; }
        }
        catch (Exception ex) { item.StatusText = ex.Message; return; }
        using var heldMaterialLease = materialLease;
        item.IsRunning = true;
        item.SelectedProxyName = snapshot.ProxyNode.DisplayName;
        snapshot.ProxyNode.Acquire();
        if (snapshot.EnablePixiv) snapshot.PixivToken?.Acquire();
        IsDownloading = true;
        await RunDownloadTaskAsync(item, snapshot);
    }

    private void DisposeLibraryTransport() { PixivAuthClient.Dispose(); PixivApiClient.Dispose(); PixivImageClient.Dispose(); KemonoHttpClient.Dispose(); _danbooruClient.Dispose(); }
    public sealed class LibraryConfiguration
    {
        public int Version { get; set; } = 1;
        public string SelectedFolder { get; set; } = "原图";
        public Dictionary<string, JsonElement> Values { get; set; } = new();
        public string? PixivTokenId { get; set; }
        public string? ProxyNodeId { get; set; }
        public string? DanbooruCredentialId { get; set; }
    }
}
