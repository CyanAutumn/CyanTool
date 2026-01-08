using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CyanTools.Model;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives.SevenZip;

namespace CyanTools.Utils
{
    public sealed class TagDictionaryService : IDisposable
    {
        private const string DatabaseFileName = "tag_dictionary.sqlite";
        private const string TagArchiveFileName = "tag_by_category.7z";
        private static readonly Uri TagArchiveUri = new("https://huggingface.co/datasets/toyxyz/danbooru2024_wildcard/resolve/main/tag_by_category.7z");
        private static readonly string[] KnownCategories = { "general", "artist", "character", "copyright", "meta", "rating", "quality" };

        private readonly string _storageRoot;
        private readonly string _dbPath;
        private readonly HttpClient _httpClient;
        private readonly bool _enableTranslation;
        private readonly ConcurrentDictionary<string, Task<string?>> _translationTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _translationGate = new(1, 1);

        public TagDictionaryService(string? storageRoot = null, bool enableTranslation = false)
        {
            _storageRoot = storageRoot ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TagDictionary");
            Directory.CreateDirectory(_storageRoot);
            _dbPath = Path.Combine(_storageRoot, DatabaseFileName);
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CyanTools/1.0");
            _enableTranslation = enableTranslation;

            InitializeDatabase();
        }

        public async Task<int> GetCatalogCountAsync(CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM tag_catalog";
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result);
        }

        public async Task<IReadOnlyList<TagSuggestion>> GetSuggestionsAsync(string prefix, int limit, string? category, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return Array.Empty<Model.TagSuggestion>();
            }

            var normalized = NormalizeTag(prefix);
            var suggestions = new List<TagSuggestion>();

            await using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT t.tag, t.translation " +
                "FROM tag_catalog t " +
                "WHERE t.tag LIKE $prefix || '%' COLLATE NOCASE " +
                (string.IsNullOrWhiteSpace(category) ? string.Empty : "AND t.category = $category ") +
                "ORDER BY t.count DESC, t.tag " +
                "LIMIT $limit";

            command.Parameters.AddWithValue("$prefix", normalized);
            command.Parameters.AddWithValue("$limit", limit);
            if (!string.IsNullOrWhiteSpace(category))
            {
                command.Parameters.AddWithValue("$category", category);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                suggestions.Add(new TagSuggestion
                {
                    Tag = reader.GetString(0),
                    Translation = reader.IsDBNull(1) ? null : reader.GetString(1)
                });
            }

            return suggestions;
        }

        public async Task<IReadOnlyList<TagSuggestion>> GetSuggestionsByTranslationAsync(string prefix, int limit, string? category, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return Array.Empty<Model.TagSuggestion>();
            }

            var normalized = NormalizeTag(prefix);
            var suggestions = new List<TagSuggestion>();

            await using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT t.tag, t.translation " +
                "FROM tag_catalog t " +
                "WHERE t.translation IS NOT NULL " +
                "AND t.translation LIKE '%' || $prefix || '%' COLLATE NOCASE " +
                (string.IsNullOrWhiteSpace(category) ? string.Empty : "AND t.category = $category ") +
                "ORDER BY t.count DESC, t.tag " +
                "LIMIT $limit";

            command.Parameters.AddWithValue("$prefix", normalized);
            command.Parameters.AddWithValue("$limit", limit);
            if (!string.IsNullOrWhiteSpace(category))
            {
                command.Parameters.AddWithValue("$category", category);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                suggestions.Add(new TagSuggestion
                {
                    Tag = reader.GetString(0),
                    Translation = reader.IsDBNull(1) ? null : reader.GetString(1)
                });
            }

            return suggestions;
        }

        public async Task<string?> GetTranslationAsync(string tag, CancellationToken cancellationToken)
        {
            var normalized = NormalizeTag(tag);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            await using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT translation FROM tag_catalog WHERE tag = $tag LIMIT 1";
            command.Parameters.AddWithValue("$tag", normalized);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result as string;
        }

        public async Task<IReadOnlyDictionary<string, string>> GetTranslationsAsync(IEnumerable<string> tags, CancellationToken cancellationToken)
        {
            if (tags == null)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var normalizedTags = tags
                .Select(NormalizeTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedTags.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const int batchSize = 400;
            for (int offset = 0; offset < normalizedTags.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = normalizedTags.Skip(offset).Take(batchSize).ToList();
                await using var command = connection.CreateCommand();
                var parameterNames = new List<string>(batch.Count);
                for (int i = 0; i < batch.Count; i++)
                {
                    var paramName = "$t" + i;
                    command.Parameters.AddWithValue(paramName, batch[i]);
                    parameterNames.Add(paramName);
                }

                command.CommandText =
                    $"SELECT tag, translation FROM tag_catalog WHERE tag IN ({string.Join(", ", parameterNames)}) " +
                    "AND translation IS NOT NULL AND translation <> ''";

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var tag = reader.GetString(0);
                    var translation = reader.GetString(1);
                    if (!string.IsNullOrWhiteSpace(translation))
                    {
                        result[tag] = translation;
                    }
                }
            }

            return result;
        }

        public async Task UpsertCatalogTagAsync(string tag, string? category, CancellationToken cancellationToken)
        {
            var normalized = NormalizeTag(tag);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            await using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO tag_catalog(tag, category, count) " +
                "VALUES ($tag, $category, 0) " +
                "ON CONFLICT(tag) DO UPDATE SET " +
                "category = COALESCE(tag_catalog.category, excluded.category);";
            command.Parameters.AddWithValue("$tag", normalized);
            command.Parameters.AddWithValue("$category", category ?? "custom");
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<string?> TranslateAndCacheAsync(string tag, CancellationToken cancellationToken)
        {
            if (!_enableTranslation)
            {
                return Task.FromResult<string?>(null);
            }

            var normalized = NormalizeTag(tag);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return Task.FromResult<string?>(null);
            }

            return _translationTasks.GetOrAdd(normalized, _ => TranslateAndCacheInternalAsync(normalized, cancellationToken));
        }

        public async Task<int> DownloadAndImportCatalogAsync(IProgress<string>? progress, CancellationToken cancellationToken)
        {
            var archivePath = Path.Combine(_storageRoot, TagArchiveFileName);

            if (!File.Exists(archivePath) || new FileInfo(archivePath).Length == 0)
            {
                progress?.Report("Downloading tag catalog...");
                await DownloadFileAsync(TagArchiveUri, archivePath, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report("Importing tag catalog...");
            return await ImportCatalogFromArchiveAsync(archivePath, progress, cancellationToken).ConfigureAwait(false);
        }

        public static string NormalizeTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return string.Empty;
            }

            var trimmed = tag.Trim();
            trimmed = trimmed.Replace('_', ' ');
            while (trimmed.Contains("  ", StringComparison.Ordinal))
            {
                trimmed = trimmed.Replace("  ", " ");
            }

            return trimmed;
        }

        public static bool ContainsCjk(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (var ch in value)
            {
                if (IsCjkChar(ch))
                {
                    return true;
                }
            }

            return false;
        }

        public static string ExtractCjkQuery(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var ch in value)
            {
                if (IsCjkChar(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private string GetConnectionString()
        {
            return $"Data Source={_dbPath}";
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(GetConnectionString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE IF NOT EXISTS tag_catalog (" +
                "  tag TEXT PRIMARY KEY," +
                "  category TEXT," +
                "  count INTEGER DEFAULT 0," +
                "  translation TEXT" +
                ");" +
                "CREATE INDEX IF NOT EXISTS idx_tag_catalog_tag ON tag_catalog(tag);" +
                "CREATE TABLE IF NOT EXISTS tag_translation (" +
                "  tag TEXT PRIMARY KEY," +
                "  translation TEXT NOT NULL," +
                "  updated_at TEXT NOT NULL" +
                ");";
            command.ExecuteNonQuery();

            EnsureTagCatalogTranslationColumn(connection);
            MigrateLegacyTranslations(connection);
        }

        private async Task DownloadFileAsync(Uri uri, string targetPath, CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await responseStream.CopyToAsync(fileStream, 81920, cancellationToken).ConfigureAwait(false);
        }

        private async Task<int> ImportCatalogFromArchiveAsync(string archivePath, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            int totalCount = 0;

            using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO tag_catalog(tag, category, count) " +
                "VALUES ($tag, $category, $count) " +
                "ON CONFLICT(tag) DO UPDATE SET " +
                "category = COALESCE(tag_catalog.category, excluded.category), " +
                "count = CASE WHEN excluded.count > tag_catalog.count THEN excluded.count ELSE tag_catalog.count END;";

            var tagParam = command.CreateParameter();
            tagParam.ParameterName = "$tag";
            command.Parameters.Add(tagParam);

            var categoryParam = command.CreateParameter();
            categoryParam.ParameterName = "$category";
            command.Parameters.Add(categoryParam);

            var countParam = command.CreateParameter();
            countParam.ParameterName = "$count";
            command.Parameters.Add(countParam);

            using var archive = SevenZipArchive.Open(archivePath);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsDirectory)
                {
                    continue;
                }

                var category = GetCategoryFromEntry(entry.Key);
                if (category == null)
                {
                    continue;
                }

                progress?.Report($"Importing {category} tags...");
                using var entryStream = entry.OpenEntryStream();
                using var streamReader = new StreamReader(entryStream);
                string? line;
                int batchCount = 0;

                while ((line = await streamReader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryParseTagLine(line, out var tag, out var count))
                    {
                        continue;
                    }

                    var normalized = NormalizeTag(tag);
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        continue;
                    }

                    tagParam.Value = normalized;
                    categoryParam.Value = category;
                    countParam.Value = count;
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    totalCount++;
                    batchCount++;
                    if (batchCount >= 5000)
                    {
                        progress?.Report($"Imported {totalCount} tags...");
                        batchCount = 0;
                    }
                }
            }

            transaction.Commit();
            return totalCount;
        }

        private async Task<string?> TranslateAndCacheInternalAsync(string normalizedTag, CancellationToken cancellationToken)
        {
            if (!_enableTranslation)
            {
                return null;
            }

            bool gateTaken = false;
            try
            {
                var existing = await GetTranslationAsync(normalizedTag, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    return existing;
                }

                await _translationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                gateTaken = true;

                existing = await GetTranslationAsync(normalizedTag, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    return existing;
                }

                var translated = await TranslateWithGoogleAsync(normalizedTag, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(translated))
                {
                    await SaveTranslationAsync(normalizedTag, translated, cancellationToken).ConfigureAwait(false);
                }

                return translated;
            }
            finally
            {
                if (gateTaken)
                {
                    _translationGate.Release();
                }

                _translationTasks.TryRemove(normalizedTag, out _);
            }
        }

        private async Task SaveTranslationAsync(string tag, string translation, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE tag_catalog SET translation = $translation WHERE tag = $tag;";
            command.Parameters.AddWithValue("$tag", tag);
            command.Parameters.AddWithValue("$translation", translation);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> GetTagsNeedingTranslationAsync(IEnumerable<string> tags, CancellationToken cancellationToken)
        {
            if (tags == null)
            {
                return Array.Empty<string>();
            }

            var normalizedTags = tags
                .Select(NormalizeTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedTags.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            await using var connection = new SqliteConnection(GetConnectionString());
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const int batchSize = 400;
            for (int offset = 0; offset < normalizedTags.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = normalizedTags.Skip(offset).Take(batchSize).ToList();
                await using var command = connection.CreateCommand();
                var parameterNames = new List<string>(batch.Count);
                for (int i = 0; i < batch.Count; i++)
                {
                    var paramName = "$t" + i;
                    command.Parameters.AddWithValue(paramName, batch[i]);
                    parameterNames.Add(paramName);
                }

                command.CommandText =
                    $"SELECT tag FROM tag_catalog WHERE tag IN ({string.Join(", ", parameterNames)}) " +
                    "AND (translation IS NULL OR translation = '')";

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    result.Add(reader.GetString(0));
                }
            }

            return result;
        }

        private async Task<string?> TranslateWithGoogleAsync(string tag, CancellationToken cancellationToken)
        {
            var query = Uri.EscapeDataString(tag);
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=zh-CN&dt=t&q={query}";
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseGoogleTranslation(payload);
        }

        private static string? ParseGoogleTranslation(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                var root = JArray.Parse(payload);
                if (root.Count == 0 || root[0] is not JArray sentences)
                {
                    return null;
                }

                var result = string.Empty;
                foreach (var sentence in sentences)
                {
                    if (sentence is JArray parts && parts.Count > 0)
                    {
                        var piece = parts[0]?.ToString();
                        if (!string.IsNullOrEmpty(piece))
                        {
                            result += piece;
                        }
                    }
                }

                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryParseTagLine(string line, out string tag, out int count)
        {
            tag = string.Empty;
            count = 0;

            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return false;
            }

            if (TrySplitWithSeparator(trimmed, '\t', out tag, out count))
            {
                return true;
            }

            if (TrySplitWithSeparator(trimmed, ',', out tag, out count))
            {
                return true;
            }

            var lastSpace = trimmed.LastIndexOf(' ');
            if (lastSpace > 0 && int.TryParse(trimmed[(lastSpace + 1)..], out count))
            {
                tag = trimmed[..lastSpace];
                return true;
            }

            tag = trimmed;
            count = 0;
            return true;
        }

        private static bool TrySplitWithSeparator(string value, char separator, out string tag, out int count)
        {
            tag = string.Empty;
            count = 0;
            var index = value.LastIndexOf(separator);
            if (index <= 0 || index + 1 >= value.Length)
            {
                return false;
            }

            var tail = value[(index + 1)..];
            if (!int.TryParse(tail, out count))
            {
                return false;
            }

            tag = value[..index];
            return true;
        }

        private static string? GetCategoryFromEntry(string entryKey)
        {
            if (string.IsNullOrWhiteSpace(entryKey))
            {
                return null;
            }

            var segments = entryKey.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                var normalized = Path.GetFileNameWithoutExtension(segment).Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                foreach (var category in KnownCategories)
                {
                    if (normalized == category)
                    {
                        return category;
                    }
                }

                if (int.TryParse(normalized, out var categoryCode))
                {
                    switch (categoryCode)
                    {
                        case 0:
                            return "general";
                        case 1:
                            return "artist";
                        case 3:
                            return "copyright";
                        case 4:
                            return "character";
                        case 5:
                            return "meta";
                        case 2:
                            return "general";
                    }
                }
            }

            return null;
        }

        private void EnsureTagCatalogTranslationColumn(SqliteConnection connection)
        {
            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "PRAGMA table_info(tag_catalog);";
            using var reader = checkCommand.ExecuteReader();
            while (reader.Read())
            {
                if (reader.FieldCount > 1 && string.Equals(reader.GetString(1), "translation", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE tag_catalog ADD COLUMN translation TEXT;";
            alterCommand.ExecuteNonQuery();
        }

        private void MigrateLegacyTranslations(SqliteConnection connection)
        {
            using var existsCommand = connection.CreateCommand();
            existsCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'tag_translation';";
            var exists = existsCommand.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(exists))
            {
                return;
            }

            using var migrateCommand = connection.CreateCommand();
            migrateCommand.CommandText =
                "UPDATE tag_catalog " +
                "SET translation = (" +
                "  SELECT tr.translation FROM tag_translation tr WHERE tr.tag = tag_catalog.tag" +
                ") " +
                "WHERE translation IS NULL OR translation = '';";
            migrateCommand.ExecuteNonQuery();
        }

        private static bool IsCjkChar(char ch)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF)
            {
                return true;
            }

            if (ch >= 0x3400 && ch <= 0x4DBF)
            {
                return true;
            }

            if (ch >= 0xF900 && ch <= 0xFAFF)
            {
                return true;
            }

            return false;
        }
    }
}
