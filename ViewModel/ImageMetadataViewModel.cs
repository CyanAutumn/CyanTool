using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CyanTools.Utils;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace CyanTools.ViewModel
{
    public class ImageMetadataViewModel : INotifyPropertyChanged
    {
        private string _currentImagePath;
        private BitmapSource _displayedImage;
        private string _metadataJson = "{}";

        public ImageMetadataViewModel()
        {
            UpdateMetadataCommand = new RelayCommand(UpdateMetadata);
        }

        public BitmapSource DisplayedImage
        {
            get => _displayedImage;
            private set
            {
                if (_displayedImage != value)
                {
                    _displayedImage = value;
                    OnPropertyChanged(nameof(DisplayedImage));
                }
            }
        }

        public string MetadataJson
        {
            get => _metadataJson;
            set
            {
                if (_metadataJson != value)
                {
                    _metadataJson = value;
                    OnPropertyChanged(nameof(MetadataJson));
                }
            }
        }

        public ICommand UpdateMetadataCommand { get; }

        public void LoadImage(string imagePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }

                bitmap.Freeze();
                DisplayedImage = bitmap;
                _currentImagePath = imagePath;
                MetadataJson = BuildMetadataJson(imagePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildMetadataJson(string imagePath)
        {
            var allMetadata = new Dictionary<string, object>();

            try
            {
                using var image = ImageSharpImage.Load(imagePath);
                var metadata = image.Metadata;

                allMetadata["novelAI"] = ReadNovelAiMetadata(imagePath, metadata);
                allMetadata["exif"] = ReadExifMetadata(metadata);
            }
            catch (Exception ex)
            {
                allMetadata["Error"] = new Dictionary<string, string>
                {
                    ["Message"] = ex.Message
                };
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            return JsonSerializer.Serialize(allMetadata, options);
        }

        private Dictionary<string, string> ReadNovelAiMetadata(string imagePath, ImageMetadata metadata)
        {
            var novelAiData = new Dictionary<string, string>();
            if (!Path.GetExtension(imagePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                novelAiData["Status"] = "仅 PNG 支持 NovelAI tEXt 数据。";
                return novelAiData;
            }

            var pngMetadata = metadata.GetPngMetadata();
            if (pngMetadata != null && pngMetadata.TextData.Any())
            {
                foreach (var textData in pngMetadata.TextData)
                {
                    novelAiData[textData.Keyword] = textData.Value;
                }
            }
            else
            {
                novelAiData["Status"] = "未找到 PNG tEXt 数据。";
            }

            return novelAiData;
        }

        private Dictionary<string, string> ReadExifMetadata(ImageMetadata metadata)
        {
            var exifData = new Dictionary<string, string>();
            if (metadata.ExifProfile != null && metadata.ExifProfile.Values.Any())
            {
                foreach (var tag in metadata.ExifProfile.Values)
                {
                    string key = tag.Tag.ToString();
                    string value = tag.GetValue()?.ToString();
                    if (!string.IsNullOrEmpty(key) && value != null)
                    {
                        exifData[key] = value;
                    }
                }
            }
            else
            {
                exifData["Status"] = "未找到 EXIF 数据。";
            }

            return exifData;
        }

        private void UpdateMetadata()
        {
            if (string.IsNullOrEmpty(_currentImagePath))
            {
                MessageBox.Show("请先拖入图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var metadataDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(MetadataJson);
                if (metadataDict == null)
                {
                    MessageBox.Show("无法解析 JSON 元数据。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                using var image = ImageSharpImage.Load(_currentImagePath);
                ApplyExifUpdates(image, metadataDict);
                ApplyNovelAiUpdates(image, metadataDict);
                image.Save(_currentImagePath);

                MessageBox.Show("元数据已更新。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                MetadataJson = BuildMetadataJson(_currentImagePath);
            }
            catch (JsonException)
            {
                WriteRawStringMetadata();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新元数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyExifUpdates(ImageSharpImage image, Dictionary<string, JsonElement> metadataDict)
        {
            if (!metadataDict.TryGetValue("exif", out var exifElement) || exifElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var exifDict = exifElement.Deserialize<Dictionary<string, string>>();
            if (exifDict == null)
            {
                return;
            }

            image.Metadata.ExifProfile = new ExifProfile();
            if (exifDict.TryGetValue("ImageDescription", out var description))
            {
                image.Metadata.ExifProfile.SetValue(ExifTag.ImageDescription, description);
            }
            if (exifDict.TryGetValue("UserComment", out var userComment))
            {
                image.Metadata.ExifProfile.SetValue(ExifTag.UserComment, userComment);
            }
        }

        private void ApplyNovelAiUpdates(ImageSharpImage image, Dictionary<string, JsonElement> metadataDict)
        {
            if (!Path.GetExtension(_currentImagePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!metadataDict.TryGetValue("novelAI", out var novelAiElement) || novelAiElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var novelAiDict = novelAiElement.Deserialize<Dictionary<string, string>>();
            if (novelAiDict == null)
            {
                return;
            }

            PngMetadata pngMetadata = image.Metadata.GetPngMetadata();
            pngMetadata.TextData.Clear();
            foreach (var kvp in novelAiDict)
            {
                pngMetadata.TextData.Add(new PngTextData(kvp.Key, kvp.Value, null, null));
            }
        }

        private void WriteRawStringMetadata()
        {
            try
            {
                using var image = ImageSharpImage.Load(_currentImagePath);
                string content = MetadataJson ?? string.Empty;

                if (Path.GetExtension(_currentImagePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    var pngMetadata = image.Metadata.GetPngMetadata();
                    pngMetadata.TextData.Clear();
                    pngMetadata.TextData.Add(new PngTextData("Prompt", content, null, null));
                    MessageBox.Show("已将文本写入 PNG tEXt (Prompt)。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    image.Metadata.ExifProfile = image.Metadata.ExifProfile ?? new ExifProfile();
                    image.Metadata.ExifProfile.SetValue(ExifTag.ImageDescription, content);
                    MessageBox.Show("已将文本写入 EXIF ImageDescription。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                image.Save(_currentImagePath);
                MetadataJson = BuildMetadataJson(_currentImagePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"写入纯文本失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
