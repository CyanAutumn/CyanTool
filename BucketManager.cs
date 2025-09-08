using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace CyanTools
{
    public class BucketManager
    {
        private Dictionary<string, Size> resMap;
        private Size maxSize;
        private int f = 8;
        private double maxTokens;
        private int div;
        private int minDim;
        private int dimLimit;
        private Size baseRes;
        private double maxArError;
        private Random prng;
        private bool debug;

        private Size[] resolutions;
        private double[] aspects;
        private Dictionary<int, List<string>> buckets;
        private List<double> aspectErrors;

        public Dictionary<int, List<string>> Buckets => buckets;
        public Size[] Resolutions => resolutions;

        public BucketManager(Dictionary<string, Size> dicData, Size? maxSize = null, int divisible = 64, 
            int stepSize = 8, int minDim = 256, Size? baseRes = null, double maxArError = 4, 
            int seed = 42, int dimLimit = 1024, bool debug = false)
        {
            this.resMap = dicData;
            this.maxSize = maxSize ?? new Size(1024, 1024);
            this.maxTokens = (this.maxSize.Width / (double)f) * (this.maxSize.Height / (double)f);
            this.div = divisible;
            this.minDim = minDim;
            this.dimLimit = dimLimit;
            this.baseRes = baseRes ?? new Size(1024, 1024);
            this.maxArError = maxArError;
            this.prng = new Random(seed);
            this.debug = debug;
            this.aspectErrors = new List<double>();

            GenerateBuckets();
            AssignBuckets();
        }

        private void GenerateBuckets()
        {
            var resolutions = new List<Size>();
            var aspects = new List<double>();

            int w = minDim;
            while ((w / (double)f) * (minDim / (double)f) <= maxTokens && w <= dimLimit)
            {
                int h = minDim;
                bool gotBase = false;
                while ((w / (double)f) * ((h + div) / (double)f) <= maxTokens && (h + div) <= dimLimit)
                {
                    if (w == baseRes.Width && h == baseRes.Height)
                        gotBase = true;
                    h += div;
                }
                if ((w != baseRes.Width || h != baseRes.Height) && gotBase)
                {
                    resolutions.Add(baseRes);
                    aspects.Add(1.0);
                }
                resolutions.Add(new Size(w, h));
                aspects.Add((double)w / h);
                w += div;
            }

            int h2 = minDim;
            while ((h2 / (double)f) * (minDim / (double)f) <= maxTokens && h2 <= dimLimit)
            {
                int w2 = minDim;
                bool gotBase = false;
                while ((h2 / (double)f) * ((w2 + div) / (double)f) <= maxTokens && (w2 + div) <= dimLimit)
                {
                    if (w2 == baseRes.Width && h2 == baseRes.Height)
                        gotBase = true;
                    w2 += div;
                }
                resolutions.Add(new Size(w2, h2));
                aspects.Add((double)w2 / h2);
                h2 += div;
            }

            var resMap = new Dictionary<Size, double>();
            for (int i = 0; i < resolutions.Count; i++)
            {
                if (!resMap.ContainsKey(resolutions[i]))
                    resMap[resolutions[i]] = aspects[i];
            }

            this.resolutions = resMap.Keys.OrderBy(x => x.Width * 4096 - x.Height).ToArray();
            this.aspects = this.resolutions.Select(x => resMap[x]).ToArray();

            if (debug)
            {
                Console.WriteLine($"resolutions: {string.Join(", ", this.resolutions.Select(r => $"{r.Width}x{r.Height}"))}");
                Console.WriteLine($"aspects: {string.Join(", ", this.aspects)}");
            }
        }

        private void AssignBuckets()
        {
            buckets = new Dictionary<int, List<string>>();
            int skipped = 0;
            var skipList = new List<string>();

            foreach (var postId in resMap.Keys.ToList())
            {
                var size = resMap[postId];
                double aspect = (double)size.Width / size.Height;
                int bucketId = FindClosestAspectBucket(aspect);

                if (!buckets.ContainsKey(bucketId))
                    buckets[bucketId] = new List<string>();

                double error = Math.Abs(aspects[bucketId] - aspect);
                if (error < maxArError)
                {
                    buckets[bucketId].Add(postId);
                    if (debug)
                        aspectErrors.Add(error);
                }
                else
                {
                    skipped++;
                    skipList.Add(postId);
                }
            }

            foreach (var postId in skipList)
            {
                resMap.Remove(postId);
            }

            if (debug)
            {
                Console.WriteLine($"skipped images: {skipped}");
                if (aspectErrors.Count > 0)
                {
                    Console.WriteLine($"aspect error: mean {aspectErrors.Average():F5}, median {aspectErrors.OrderBy(x => x).Skip(aspectErrors.Count / 2).First():F5}, max {aspectErrors.Max():F5}");
                }

                foreach (var bucketId in buckets.Keys.OrderByDescending(b => buckets[b].Count))
                {
                    var res = resolutions[bucketId];
                    Console.WriteLine($"bucket {bucketId}: {res.Width}x{res.Height}, aspect {aspects[bucketId]:F5}, entries {buckets[bucketId].Count}");
                }
            }
        }

        private int FindClosestAspectBucket(double aspect)
        {
            int closestIndex = 0;
            double minDiff = Math.Abs(aspects[0] - aspect);

            for (int i = 1; i < aspects.Length; i++)
            {
                double diff = Math.Abs(aspects[i] - aspect);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }

        public static void ClassifyImages(string imageFolder, string outputFolder, int divisible = 64, 
            double maxArError = 1, int dimLimit = 2048, bool debug = false)
        {
            var imageData = new Dictionary<string, Size>();
            var dicFileName = new Dictionary<string, string>();
            int fileIndex = 10000;

            var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

            foreach (var filePath in Directory.GetFiles(imageFolder))
            {
                var extension = Path.GetExtension(filePath).ToLower();
                if (supportedExtensions.Contains(extension))
                {
                    try
                    {
                        using (var image = Image.FromFile(filePath))
                        {
                            var resolution = image.Size;
                            var fileName = Path.GetFileName(filePath);
                            imageData[fileIndex.ToString()] = resolution;
                            dicFileName[fileIndex.ToString()] = fileName;
                            fileIndex++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing {filePath}: {ex.Message}");
                    }
                }
            }

            var bm = new BucketManager(imageData, divisible: divisible, maxArError: maxArError, 
                dimLimit: dimLimit, debug: debug);

            foreach (var bucketId in bm.buckets.Keys.OrderByDescending(b => bm.buckets[b].Count))
            {
                foreach (var fileIndexStr in bm.buckets[bucketId])
                {
                    var fileName = dicFileName[fileIndexStr];
                    var fileSrcPath = Path.Combine(imageFolder, fileName);
                    var resolution = bm.resolutions[bucketId];
                    var outPath = Path.Combine(outputFolder, $"{resolution.Width}x{resolution.Height}");

                    if (!Directory.Exists(outPath))
                        Directory.CreateDirectory(outPath);

                    var destPath = Path.Combine(outPath, fileName);
                    File.Copy(fileSrcPath, destPath, true);

                    if (debug)
                    {
                        Console.WriteLine($"fileIndex: {fileIndexStr}, fileName: {fileName}, resolution: {resolution.Width}x{resolution.Height}");
                    }
                }
            }
        }
    }
}