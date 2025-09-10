using CyanTools.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace CyanTools
{
    public partial class ImageMetadataView : UserControl
    {
        private string _currentImagePath;

        public ImageMetadataView()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void UserControl_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void UserControl_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0)
                {
                    string imagePath = files[0];
                    if (File.Exists(imagePath) && IsImageFile(imagePath))
                    {
                        _currentImagePath = imagePath;
                        LoadImage(imagePath);
                        DisplayMetadata(imagePath);
                    }
                }
            }
            e.Handled = true;
        }

        private bool IsImageFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp" || extension == ".gif";
        }

        private void LoadImage(string imagePath) {
            try {
                BitmapImage bitmap = new BitmapImage();



                byte[] imageBytes = File.ReadAllBytes(imagePath);


                using (var stream = new System.IO.MemoryStream(imageBytes)) {

                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                }


                bitmap.Freeze();
                DroppedImage.Source = bitmap;
            }
            catch (Exception ex) {
                MessageBox.Show($"Error loading image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DisplayMetadata(string imagePath) {
            JsonMetadataDisplay.Text = string.Empty;
            var allMetadata = new Dictionary<string, object>();

            try {
                using (ImageSharpImage image = ImageSharpImage.Load(imagePath)) {
                    var metadata = image.Metadata;


                    var novelAIMetadata = new Dictionary<string, string>();
                    if (Path.GetExtension(imagePath).Equals(".png", StringComparison.OrdinalIgnoreCase)) {
                        PngMetadata pngMetadata = metadata.GetPngMetadata();
                        if (pngMetadata != null && pngMetadata.TextData.Any()) {
                            foreach (var textData in pngMetadata.TextData) {
                                novelAIMetadata[textData.Keyword] = textData.Value;
                            }
                        }
                    }
                    if (!novelAIMetadata.Any()) {
                        novelAIMetadata["Status"] = "No NovelAI (PNG tEXt) data found.";
                    }
                    allMetadata["novelAI"] = novelAIMetadata;


                    var exifData = new Dictionary<string, string>();
                    if (metadata.ExifProfile != null && metadata.ExifProfile.Values.Any()) {
                        foreach (var tag in metadata.ExifProfile.Values) {
                            string key;






                            if (tag.Tag == ExifTag.UserComment) key = "UserComment";
                            else if (tag.Tag == ExifTag.ImageDescription) key = "ImageDescription";
                            else if (tag.Tag == ExifTag.Software) key = "Software";
                            else if (tag.Tag == ExifTag.Make) key = "Make";
                            else if (tag.Tag == ExifTag.Model) key = "Model";
                            else if (tag.Tag == ExifTag.DateTime) key = "DateTime";
                            else {
                                key = tag.Tag.ToString();
                            }



                            string value = tag.GetValue()?.ToString();

                            if (value != null) {
                                exifData[key] = value;
                            }




                        }
                    }
                    else {
                        exifData["Status"] = "No EXIF data found.";
                    }
                    allMetadata["exif"] = exifData;
                }
            }
            catch (Exception ex) {
                var errorDict = new Dictionary<string, string> {
                    ["Message"] = ex.Message,
                    ["StackTrace"] = ex.StackTrace?.ToString()
                };
                allMetadata["Error"] = errorDict;
            }


            try {
                var options = new JsonSerializerOptions {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                JsonMetadataDisplay.Text = JsonSerializer.Serialize(allMetadata, options);
            }
            catch (Exception ex) {
                JsonMetadataDisplay.Text = $"{{\n  \"Error\": \"Failed to serialize metadata to JSON: {ex.Message}\"\n}}";
            }
        }

        private void UpdateMetadata_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(_currentImagePath)) {
                MessageBox.Show("Please drop an image first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try {

                string jsonContent = JsonMetadataDisplay.Text;
                var allMetadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);
                using (ImageSharpImage image = ImageSharpImage.Load(_currentImagePath)) {

                    image.Metadata.ExifProfile = new ExifProfile();
                    if (allMetadata.TryGetValue("exif", out var exifElement) && exifElement.ValueKind == JsonValueKind.Object) {
                        var exifDict = exifElement.Deserialize<Dictionary<string, string>>();
                        if (exifDict != null) {
                            if (exifDict.TryGetValue("ImageDescription", out var description))
                                image.Metadata.ExifProfile.SetValue(ExifTag.ImageDescription, description);
                            if (exifDict.TryGetValue("UserComment", out var userComment))
                                image.Metadata.ExifProfile.SetValue(ExifTag.UserComment, userComment);
                        }
                    }

                    if (Path.GetExtension(_currentImagePath).Equals(".png", StringComparison.OrdinalIgnoreCase)) {

                        PngMetadata pngMetadata = image.Metadata.GetPngMetadata();
                        pngMetadata.TextData.Clear();
                        if (allMetadata.TryGetValue("novelAI", out var novelAIElement) && novelAIElement.ValueKind == JsonValueKind.Object) {
                            var novelAIDict = novelAIElement.Deserialize<Dictionary<string, string>>();
                            if (novelAIDict != null) {
                                foreach (var kvp in novelAIDict)
                                    pngMetadata.TextData.Add(new PngTextData(kvp.Key, kvp.Value, null, null));
                            }
                        }
                    }
                    image.Save(_currentImagePath);
                }
                MessageBox.Show("JSON metadata updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                DisplayMetadata(_currentImagePath);
            }
            catch (JsonException)
            {
                try {
                    using (ImageSharpImage image = ImageSharpImage.Load(_currentImagePath)) {
                        string arbitraryText = JsonMetadataDisplay.Text;
                        image.Metadata.ExifProfile = new ExifProfile();

                        if (Path.GetExtension(_currentImagePath).Equals(".png", StringComparison.OrdinalIgnoreCase)) {

                            PngMetadata pngMetadata = image.Metadata.GetPngMetadata();
                            pngMetadata.TextData.Clear();
                            pngMetadata.TextData.Add(new PngTextData("Prompt", arbitraryText, null, null));
                            MessageBox.Show("Arbitrary string written to PNG 'Prompt' text chunk successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else {
                            image.Metadata.ExifProfile.SetValue(ExifTag.ImageDescription, arbitraryText);
                            MessageBox.Show("Arbitrary string written to EXIF 'ImageDescription' successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        }

                        image.Save(_currentImagePath);
                    }
                    DisplayMetadata(_currentImagePath);
                }
                catch (Exception ex) {
                    MessageBox.Show($"Error writing arbitrary string: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex) {
                MessageBox.Show($"Error updating metadata: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
