using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CyanTools.Utils
{




    public static class SafetensorsMetadataService
    {
        private const string MetadataPropertyName = "__metadata__";
        private const int CopyBufferSize = 1024 * 1024;

        private static readonly JsonSerializerOptions CompactJsonOptions = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        public static SafetensorsMetadataDocument Read(string path)
        {
            string fullPath = NormalizeExistingPath(path);
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = ReadHeader(stream);
            return new SafetensorsMetadataDocument(
                ReadMetadata(header.Root),
                header.TensorCount,
                header.HeaderLength,
                stream.Length);
        }

        public static SafetensorsMetadataWriteResult Write(
            string sourcePath,
            string outputPath,
            IReadOnlyDictionary<string, string>? metadata)
        {
            string fullSourcePath = NormalizeExistingPath(sourcePath);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("请选择输出 Safetensors 文件。", nameof(outputPath));
            }

            string fullOutputPath = Path.GetFullPath(outputPath);
            string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException("无法确定输出目录。");
            }

            Directory.CreateDirectory(outputDirectory);
            bool replacesSource = string.Equals(fullSourcePath, fullOutputPath, StringComparison.OrdinalIgnoreCase);
            if (!replacesSource)
            {
                return WriteCore(fullSourcePath, fullOutputPath, metadata);
            }

            string temporaryPath = Path.Combine(
                outputDirectory,
                $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.metadata.tmp");

            try
            {
                var result = WriteCore(fullSourcePath, temporaryPath, metadata);
                File.Move(temporaryPath, fullOutputPath, overwrite: true);
                return result with { OutputPath = fullOutputPath };
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static SafetensorsMetadataWriteResult WriteCore(
            string sourcePath,
            string outputPath,
            IReadOnlyDictionary<string, string>? metadata)
        {
            using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.SequentialScan);
            var header = ReadHeader(source);
            ReplaceMetadata(header.Root, metadata);

            byte[] headerBytes = Encoding.UTF8.GetBytes(header.Root.ToJsonString(CompactJsonOptions));
            Span<byte> headerLengthBytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(headerLengthBytes, (ulong)headerBytes.Length);

            using var output = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.SequentialScan);
            output.Write(headerLengthBytes);
            output.Write(headerBytes);

            source.Position = header.DataOffset;
            long tensorDataLength = source.Length - header.DataOffset;
            CopyExactly(source, output, tensorDataLength);

            return new SafetensorsMetadataWriteResult(
                outputPath,
                metadata?.Count ?? 0,
                header.TensorCount,
                tensorDataLength);
        }

        private static ParsedHeader ReadHeader(Stream stream)
        {
            if (!stream.CanSeek || stream.Length < sizeof(ulong))
            {
                throw new InvalidDataException("文件不是有效的 Safetensors 文件。");
            }

            Span<byte> headerLengthBytes = stackalloc byte[8];
            ReadExactly(stream, headerLengthBytes);
            ulong rawHeaderLength = BinaryPrimitives.ReadUInt64LittleEndian(headerLengthBytes);
            if (rawHeaderLength > int.MaxValue)
            {
                throw new InvalidDataException("Safetensors 文件头过大。");
            }

            int headerLength = checked((int)rawHeaderLength);
            long dataOffset = checked(sizeof(ulong) + (long)headerLength);
            if (dataOffset > stream.Length)
            {
                throw new InvalidDataException("Safetensors 文件头长度超出文件范围。");
            }

            var headerBytes = new byte[headerLength];
            ReadExactly(stream, headerBytes);
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(headerBytes);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Safetensors 文件头不是有效 JSON。", ex);
            }

            if (node is not JsonObject root)
            {
                throw new InvalidDataException("Safetensors 文件头必须是 JSON 对象。");
            }

            int tensorCount = root.Count(property => !string.Equals(property.Key, MetadataPropertyName, StringComparison.Ordinal));
            return new ParsedHeader(root, headerLength, dataOffset, tensorCount);
        }

        private static IReadOnlyDictionary<string, string> ReadMetadata(JsonObject root)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root[MetadataPropertyName] is null)
            {
                return metadata;
            }

            if (root[MetadataPropertyName] is not JsonObject metadataObject)
            {
                throw new InvalidDataException("Safetensors __metadata__ 必须是 JSON 对象。");
            }

            foreach (var property in metadataObject)
            {
                if (property.Value is not JsonValue value ||
                    !value.TryGetValue<string>(out string? text) ||
                    text is null)
                {
                    throw new InvalidDataException($"元数据字段“{property.Key}”不是字符串。");
                }

                metadata[property.Key] = text;
            }

            return metadata;
        }

        private static void ReplaceMetadata(JsonObject root, IReadOnlyDictionary<string, string>? metadata)
        {
            if (metadata == null || metadata.Count == 0)
            {
                root.Remove(MetadataPropertyName);
                return;
            }

            var metadataObject = new JsonObject();
            foreach (var item in metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                {
                    throw new InvalidDataException("元数据字段名不能为空。");
                }

                metadataObject[item.Key] = item.Value
                    ?? throw new InvalidDataException($"元数据字段“{item.Key}”不能为 null。");
            }

            root[MetadataPropertyName] = metadataObject;
        }

        private static void CopyExactly(Stream source, Stream destination, long byteCount)
        {
            var buffer = new byte[CopyBufferSize];
            long remaining = byteCount;
            while (remaining > 0)
            {
                int requested = (int)Math.Min(buffer.Length, remaining);
                int read = source.Read(buffer, 0, requested);
                if (read == 0)
                {
                    throw new EndOfStreamException("Safetensors 张量数据不完整。");
                }

                destination.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static string NormalizeExistingPath(string path)
        {
            string fullPath = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("找不到 Safetensors 文件。", fullPath);
            }

            return fullPath;
        }

        private static void ReadExactly(Stream stream, Span<byte> buffer)
        {
            int readTotal = 0;
            while (readTotal < buffer.Length)
            {
                int read = stream.Read(buffer[readTotal..]);
                if (read == 0)
                {
                    throw new EndOfStreamException("Safetensors 文件头不完整。");
                }

                readTotal += read;
            }
        }

        private sealed record ParsedHeader(JsonObject Root, int HeaderLength, long DataOffset, int TensorCount);
    }

    public sealed record SafetensorsMetadataDocument(
        IReadOnlyDictionary<string, string> Metadata,
        int TensorCount,
        int HeaderLength,
        long FileSize);

    public sealed record SafetensorsMetadataWriteResult(
        string OutputPath,
        int MetadataCount,
        int TensorCount,
        long TensorDataLength);
}
