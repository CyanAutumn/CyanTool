
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;

namespace CyanTools.Utils
{
    public static class NovelAIMetadataHelper
    {
        private const string PngMetadataKey = "parameters";

        public static Dictionary<string, string> ExtractAllTextChunks(string imagePath) {
            var metadataDictionary = new Dictionary<string, string>();

            try {
                using (var image = Image.Load(imagePath)) {
                    var pngMetadata = image.Metadata.GetPngMetadata();
                    if (pngMetadata == null || !pngMetadata.TextData.Any()) {
                        Console.WriteLine("No PNG text chunks found in the image.");
                        return metadataDictionary;
                    }


                    foreach (var textData in pngMetadata.TextData) {

                        if (!string.IsNullOrEmpty(textData.Keyword) && !string.IsNullOrEmpty(textData.Value)) {


                            metadataDictionary[textData.Keyword] = textData.Value;
                            Console.WriteLine($"Found Chunk -> Keyword: '{textData.Keyword}', Value Length: {textData.Value.Length}");
                        }
                    }
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error reading image metadata: {ex.Message}");

            }

            return metadataDictionary;
        }

        public static void InjectNovelAIMetadata(string imagePath, string metadataJson)
        {
            try
            {
                using (Image image = Image.Load(imagePath))
                {
                    var pngMetadata = image.Metadata.GetPngMetadata();
                    if (pngMetadata == null)
                    {
                        return;
                    }


                    for (int i = pngMetadata.TextData.Count - 1; i >= 0; i--)
                    {
                        if (pngMetadata.TextData[i].Keyword.Equals(PngMetadataKey, StringComparison.OrdinalIgnoreCase))
                        {
                            pngMetadata.TextData.RemoveAt(i);
                        }
                    }


                    pngMetadata.TextData.Add(new PngTextData(PngMetadataKey, metadataJson, null, null));

                    image.Save(imagePath);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error writing PNG metadata: {ex.Message}", ex);
            }
        }

        public static void RemoveAllTextChunks(string imagePath)
        {
            try
            {
                using (Image image = Image.Load(imagePath))
                {
                    var pngMetadata = image.Metadata.GetPngMetadata();
                    if (pngMetadata != null)
                    {
                        pngMetadata.TextData.Clear();
                        image.Save(imagePath);
                    }
                }
            }
            catch (Exception ex)
            {


                Console.WriteLine($"Error removing text chunks: {ex.Message}");
            }
        }
    }
}
