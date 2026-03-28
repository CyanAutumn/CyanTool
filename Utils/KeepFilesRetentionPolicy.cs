using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CyanTools.Utils
{
    public sealed class KeepFilesOptions
    {
        public bool KeepJpg { get; init; } = true;
        public bool KeepPng { get; init; } = true;
        public bool KeepWebp { get; init; } = true;
        public bool KeepBmp { get; init; } = true;
        public bool KeepJpeg { get; init; } = true;
        public bool KeepNpz { get; init; } = true;
        public bool KeepTxt { get; init; } = true;
        public bool KeepChineseTxt { get; init; } = true;
    }

    public enum KeepFilesDeleteReason
    {
        None = 0,
        ExtensionNotSelected = 1,
        ChineseContentNotAllowed = 2
    }

    public static class KeepFilesRetentionPolicy
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private static readonly Regex ChineseCharacterRegex = new Regex("[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static KeepFilesDeleteReason GetDeleteReason(string filePath, KeepFilesOptions options)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("文件路径不能为空。", nameof(filePath));
            }

            ArgumentNullException.ThrowIfNull(options);

            var extension = Path.GetExtension(filePath);
            if (!BuildExtensionsToKeep(options).Contains(extension))
            {
                return KeepFilesDeleteReason.ExtensionNotSelected;
            }

            if (options.KeepTxt &&
                !options.KeepChineseTxt &&
                extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                TextFileContainsChinese(filePath))
            {
                return KeepFilesDeleteReason.ChineseContentNotAllowed;
            }

            return KeepFilesDeleteReason.None;
        }

        public static HashSet<string> BuildExtensionsToKeep(KeepFilesOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (options.KeepJpg) set.Add(".jpg");
            if (options.KeepPng) set.Add(".png");
            if (options.KeepWebp) set.Add(".webp");
            if (options.KeepBmp) set.Add(".bmp");
            if (options.KeepJpeg) set.Add(".jpeg");
            if (options.KeepNpz) set.Add(".npz");
            if (options.KeepTxt) set.Add(".txt");
            return set;
        }

        private static bool TextFileContainsChinese(string filePath)
        {
            var content = TryReadText(filePath);
            return !string.IsNullOrEmpty(content) && ChineseCharacterRegex.IsMatch(content);
        }

        private static string? TryReadText(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var reader = new StreamReader(stream, StrictUtf8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (DecoderFallbackException)
            {
                return TryReadWithGb18030(filePath);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string? TryReadWithGb18030(string filePath)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var encoding = Encoding.GetEncoding("GB18030");
                return File.ReadAllText(filePath, encoding);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
