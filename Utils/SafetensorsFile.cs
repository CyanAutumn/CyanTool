using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CyanTools.Utils
{
    public sealed class SafetensorsFile
    {
        public SafetensorsFile(IEnumerable<SafetensorsTensor> tensors, IReadOnlyDictionary<string, string>? metadata = null)
        {
            Tensors = tensors.ToDictionary(tensor => tensor.Name, StringComparer.Ordinal);
            Metadata = new Dictionary<string, string>(metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        }

        public IReadOnlyDictionary<string, SafetensorsTensor> Tensors { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }

        public SafetensorsTensor GetTensor(string name)
        {
            return Tensors[name];
        }

        public static SafetensorsFile Load(string path)
        {
            using var stream = File.OpenRead(path);
            Span<byte> headerLengthBytes = stackalloc byte[8];
            ReadExactly(stream, headerLengthBytes);
            ulong headerLength = BinaryPrimitives.ReadUInt64LittleEndian(headerLengthBytes);
            if (headerLength > int.MaxValue)
            {
                throw new InvalidDataException("safetensors header is too large.");
            }

            var headerBytes = new byte[(int)headerLength];
            ReadExactly(stream, headerBytes);
            long dataStart = stream.Position;

            using var document = JsonDocument.Parse(headerBytes);
            var root = document.RootElement;
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            var tensors = new List<SafetensorsTensor>();

            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals("__metadata__"))
                {
                    foreach (var item in property.Value.EnumerateObject())
                    {
                        metadata[item.Name] = item.Value.ValueKind == JsonValueKind.String
                            ? item.Value.GetString() ?? string.Empty
                            : item.Value.ToString();
                    }

                    continue;
                }

                string dtype = property.Value.GetProperty("dtype").GetString() ?? throw new InvalidDataException("Tensor dtype is missing.");
                var shape = property.Value.GetProperty("shape").EnumerateArray().Select(value => value.GetInt64()).ToArray();
                var offsets = property.Value.GetProperty("data_offsets").EnumerateArray().Select(value => value.GetInt64()).ToArray();
                if (offsets.Length != 2 || offsets[1] < offsets[0])
                {
                    throw new InvalidDataException($"Tensor {property.Name} has invalid data offsets.");
                }

                var data = new byte[offsets[1] - offsets[0]];
                stream.Position = dataStart + offsets[0];
                ReadExactly(stream, data);
                tensors.Add(new SafetensorsTensor(property.Name, dtype, shape, data));
            }

            return new SafetensorsFile(tensors, metadata);
        }

        public static IReadOnlyList<string> ReadTensorKeys(string path)
        {
            using var stream = File.OpenRead(path);
            Span<byte> headerLengthBytes = stackalloc byte[8];
            ReadExactly(stream, headerLengthBytes);
            ulong headerLength = BinaryPrimitives.ReadUInt64LittleEndian(headerLengthBytes);
            if (headerLength > int.MaxValue)
            {
                throw new InvalidDataException("safetensors header is too large.");
            }

            var headerBytes = new byte[(int)headerLength];
            ReadExactly(stream, headerBytes);

            using var document = JsonDocument.Parse(headerBytes);
            return document.RootElement
                .EnumerateObject()
                .Where(property => !property.NameEquals("__metadata__"))
                .Select(property => property.Name)
                .ToArray();
        }

        public static void Save(string path, IEnumerable<SafetensorsTensor> tensors, IReadOnlyDictionary<string, string>? metadata = null)
        {
            new SafetensorsFile(tensors, metadata).Save(path);
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            var header = new Dictionary<string, object>(StringComparer.Ordinal);
            if (Metadata.Count > 0)
            {
                header["__metadata__"] = Metadata;
            }

            long offset = 0;
            foreach (var tensor in Tensors.Values.OrderBy(tensor => tensor.Name, StringComparer.Ordinal))
            {
                long nextOffset = offset + tensor.Data.Length;
                header[tensor.Name] = new SafetensorsTensorHeader(tensor.DType, tensor.Shape, new[] { offset, nextOffset });
                offset = nextOffset;
            }

            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var headerBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header, jsonOptions));
            Span<byte> lengthBytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(lengthBytes, (ulong)headerBytes.Length);

            using var stream = File.Create(path);
            stream.Write(lengthBytes);
            stream.Write(headerBytes);
            foreach (var tensor in Tensors.Values.OrderBy(tensor => tensor.Name, StringComparer.Ordinal))
            {
                stream.Write(tensor.Data);
            }
        }

        private static void ReadExactly(Stream stream, Span<byte> buffer)
        {
            int readTotal = 0;
            while (readTotal < buffer.Length)
            {
                int read = stream.Read(buffer[readTotal..]);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                readTotal += read;
            }
        }

        private sealed record SafetensorsTensorHeader(string dtype, IReadOnlyList<long> shape, IReadOnlyList<long> data_offsets);
    }

    public sealed class SafetensorsTensor
    {
        public SafetensorsTensor(string name, string dtype, IReadOnlyList<long> shape, byte[] data)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DType = string.IsNullOrWhiteSpace(dtype) ? throw new ArgumentException("dtype is required.", nameof(dtype)) : dtype.ToUpperInvariant();
            Shape = shape.ToArray();
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public string Name { get; }
        public string DType { get; }
        public IReadOnlyList<long> Shape { get; }
        public byte[] Data { get; }
        public bool IsFloatingPoint => DType is "F32" or "F16" or "BF16";

        public static SafetensorsTensor FromSingleArray(string name, IReadOnlyList<long> shape, IReadOnlyList<float> values, string dtype = "F32")
        {
            dtype = dtype.ToUpperInvariant();
            long expected = shape.Aggregate(1L, (current, value) => current * value);
            if (expected != values.Count)
            {
                throw new ArgumentException($"Shape expects {expected} values but {values.Count} were provided.", nameof(values));
            }

            return new SafetensorsTensor(name, dtype, shape, EncodeFloatArray(values, dtype));
        }

        public float[] ToSingleArray()
        {
            return DecodeFloatArray(Data, DType);
        }

        public SafetensorsTensor WithSingleArray(IReadOnlyList<float> values, string? dtype = null)
        {
            return FromSingleArray(Name, Shape, values, dtype ?? DType);
        }

        public bool HasSameShape(SafetensorsTensor other)
        {
            return Shape.SequenceEqual(other.Shape);
        }

        private static byte[] EncodeFloatArray(IReadOnlyList<float> values, string dtype)
        {
            return dtype switch
            {
                "F32" => EncodeF32(values),
                "F16" => EncodeF16(values),
                "BF16" => EncodeBf16(values),
                _ => throw new NotSupportedException($"Unsupported floating dtype: {dtype}")
            };
        }

        private static float[] DecodeFloatArray(byte[] data, string dtype)
        {
            return dtype switch
            {
                "F32" => DecodeF32(data),
                "F16" => DecodeF16(data),
                "BF16" => DecodeBf16(data),
                _ => throw new NotSupportedException($"Unsupported floating dtype: {dtype}")
            };
        }

        private static byte[] EncodeF32(IReadOnlyList<float> values)
        {
            var data = new byte[values.Count * 4];
            for (int i = 0; i < values.Count; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 4, 4), values[i]);
            }

            return data;
        }

        private static float[] DecodeF32(byte[] data)
        {
            if (data.Length % 4 != 0)
            {
                throw new InvalidDataException("F32 tensor data length is not divisible by 4.");
            }

            var values = new float[data.Length / 4];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(i * 4, 4));
            }

            return values;
        }

        private static byte[] EncodeF16(IReadOnlyList<float> values)
        {
            var data = new byte[values.Count * 2];
            for (int i = 0; i < values.Count; i++)
            {
                ushort bits = BitConverter.HalfToUInt16Bits((Half)values[i]);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2, 2), bits);
            }

            return data;
        }

        private static float[] DecodeF16(byte[] data)
        {
            if (data.Length % 2 != 0)
            {
                throw new InvalidDataException("F16 tensor data length is not divisible by 2.");
            }

            var values = new float[data.Length / 2];
            for (int i = 0; i < values.Length; i++)
            {
                ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i * 2, 2));
                values[i] = (float)BitConverter.UInt16BitsToHalf(bits);
            }

            return values;
        }

        private static byte[] EncodeBf16(IReadOnlyList<float> values)
        {
            var data = new byte[values.Count * 2];
            for (int i = 0; i < values.Count; i++)
            {
                uint bits = BitConverter.SingleToUInt32Bits(values[i]);
                uint rounded = bits + 0x7FFFu + ((bits >> 16) & 1u);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2, 2), (ushort)(rounded >> 16));
            }

            return data;
        }

        private static float[] DecodeBf16(byte[] data)
        {
            if (data.Length % 2 != 0)
            {
                throw new InvalidDataException("BF16 tensor data length is not divisible by 2.");
            }

            var values = new float[data.Length / 2];
            for (int i = 0; i < values.Length; i++)
            {
                ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i * 2, 2));
                values[i] = BitConverter.UInt32BitsToSingle((uint)bits << 16);
            }

            return values;
        }

        public override string ToString()
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Name} {DType} [{string.Join(",", Shape)}]");
        }
    }
}
