using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CyanTools.Utils
{
    public static class VideoFrameWorkflowProcessor
    {
        public static Task<int> ExportAllFramesAsync(
            IReadOnlyList<string> videoPaths,
            string outputFolder,
            int frameIntervalMilliseconds,
            Action<string>? log,
            CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(videoPaths);

            if (videoPaths.Count == 0)
            {
                throw new InvalidOperationException("没有可导出的视频文件。");
            }

            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                throw new InvalidOperationException("输出路径无效。");
            }

            return Task.Run(() => ExportAllFramesCore(videoPaths, Path.GetFullPath(outputFolder), frameIntervalMilliseconds, log, token), token);
        }

        private static int ExportAllFramesCore(
            IReadOnlyList<string> videoPaths,
            string outputFolder,
            int frameIntervalMilliseconds,
            Action<string>? log,
            CancellationToken token)
        {
            Directory.CreateDirectory(outputFolder);
            int totalExportedFrames = 0;
            bool multipleVideos = videoPaths.Count > 1;

            foreach (string videoPath in videoPaths)
            {
                token.ThrowIfCancellationRequested();
                log?.Invoke($"开始导出视频帧: {videoPath}");

                using var capture = new VideoCapture(videoPath);
                if (!capture.IsOpened())
                {
                    throw new InvalidOperationException($"无法打开视频文件: {videoPath}");
                }

                double fps = capture.Fps;
                if (fps <= 0)
                {
                    fps = 30;
                }

                int frameInterval = CalculateFrameInterval(frameIntervalMilliseconds, fps);
                double maxFrameIndex = Math.Max(0, capture.FrameCount - 1);
                string videoPrefix = Path.GetFileNameWithoutExtension(videoPath);

                for (int i = 0; i <= maxFrameIndex; i += frameInterval)
                {
                    token.ThrowIfCancellationRequested();
                    capture.PosFrames = i;
                    using Mat frame = capture.RetrieveMat();
                    if (frame == null || frame.Empty())
                    {
                        continue;
                    }

                    string fileName = multipleVideos
                        ? $"{videoPrefix}_frame_{i:D5}.png"
                        : $"frame_{i:D5}.png";
                    string fullPath = Path.Combine(outputFolder, fileName);
                    frame.SaveImage(fullPath);
                    totalExportedFrames++;
                }
            }

            if (totalExportedFrames == 0)
            {
                throw new InvalidOperationException("未能从视频中导出任何帧。");
            }

            log?.Invoke($"视频帧导出完成，共导出 {totalExportedFrames} 帧。");
            return totalExportedFrames;
        }

        private static int CalculateFrameInterval(int frameIntervalMilliseconds, double fps)
        {
            int normalizedMilliseconds = frameIntervalMilliseconds > 0 ? frameIntervalMilliseconds : 1;
            double intervalSeconds = normalizedMilliseconds / 1000.0;
            int framesToSkip = (int)Math.Round(intervalSeconds * fps);
            return Math.Max(1, framesToSkip);
        }
    }
}
