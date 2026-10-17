// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 CyanTools contributors — https://github.com/CyanAutumn/CyanTools
// Upstream portions: Copyright (c) 2023 DeepGHS, MIT; retain its accompanying license.
// Direct port of deepghs/imgutils, imgutils/metrics/dbaesthetic.py.
// Upstream revision and license: ThirdParty/DeepGHSImgutils/NOTICE.md.
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace CyanTools.Utils;

public sealed class AestheticScoreCalibration
{
    private readonly double[] _scores;
    private readonly double[] _percentiles;

    public AestheticScoreCalibration(IReadOnlyList<double> scores, IReadOnlyList<double> percentiles)
    {
        if (scores.Count < 2 || scores.Count != percentiles.Count)
            throw new InvalidDataException("Expected paired aesthetic calibration samples.");
        _scores = scores.ToArray();
        _percentiles = percentiles.ToArray();
        for (int i = 0; i < _scores.Length; i++)
        {
            if (!double.IsFinite(_scores[i]) || !double.IsFinite(_percentiles[i])
                || _percentiles[i] < 0 || _percentiles[i] > 1
                || i > 0 && (_scores[i] < _scores[i - 1] || _percentiles[i] < _percentiles[i - 1]))
                throw new InvalidDataException("Invalid aesthetic calibration sample order or value.");
        }
    }

    public static AestheticScoreCalibration Load(string path)
    {

        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("arr_0.npy")
            ?? throw new InvalidDataException("samples.npz is missing arr_0.npy.");
        if (entry.Length > 64 * 1024 * 1024)
            throw new InvalidDataException("Aesthetic calibration exceeds the supported size.");
        using var stream = entry.Open();
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        if (!reader.ReadBytes(6).SequenceEqual(new byte[] { 0x93, 0x4e, 0x55, 0x4d, 0x50, 0x59 }))
            throw new InvalidDataException("Invalid NumPy calibration header.");
        int major = reader.ReadByte(), minor = reader.ReadByte();
        if (major is not (1 or 2 or 3) || minor != 0)
            throw new InvalidDataException("Unsupported NumPy calibration format.");
        long headerLength = major == 1 ? reader.ReadUInt16() : reader.ReadUInt32();
        if (headerLength is < 1 or > 65536)
            throw new InvalidDataException("Invalid NumPy calibration header length.");
        byte[] headerBytes = reader.ReadBytes((int)headerLength);
        if (headerBytes.Length != headerLength)
            throw new InvalidDataException("Truncated NumPy calibration header.");
        string header = Encoding.UTF8.GetString(headerBytes);
        var shape = Regex.Match(header, @"'shape'\s*:\s*\(\s*2\s*,\s*(\d+)\s*,?\s*\)", RegexOptions.CultureInvariant);
        if (!Regex.IsMatch(header, @"'descr'\s*:\s*'<f8'", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(header, @"'fortran_order'\s*:\s*False", RegexOptions.CultureInvariant)
            || !shape.Success || !int.TryParse(shape.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            || count is < 2 or > 4_000_000
            || entry.Length != (major == 1 ? 10L : 12L) + headerLength + 2L * count * sizeof(double))
            throw new InvalidDataException("Expected a little-endian float64 NumPy array with shape (2, N).");

        var scores = new double[count];
        var percentiles = new double[count];
        for (int i = 0; i < count; i++) scores[i] = reader.ReadDouble();
        for (int i = 0; i < count; i++) percentiles[i] = reader.ReadDouble();
        return new AestheticScoreCalibration(scores, percentiles);
    }

    public double ToPercentile(double score)
    {
        if (!double.IsFinite(score)) throw new InvalidDataException("Non-finite aesthetic score.");
        double clipped = Math.Clamp(score, _scores[0], _scores[^1]);
        int low = 0, high = _scores.Length;

        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_scores[middle] < clipped) low = middle + 1;
            else high = middle;
        }
        int index = low;
        if (index >= _scores.Length - 1) return _percentiles[index];
        double x0 = _scores[index], x1 = _scores[index + 1];
        double y0 = _percentiles[index], y1 = _percentiles[index + 1];

        if (Math.Abs(x1 - x0) <= 1e-8 + 1e-5 * Math.Abs(x0)) return y0;
        return Math.Clamp((score - x0) / (x1 - x0) * (y1 - y0) + y0, _percentiles[0], _percentiles[^1]);
    }

    public static string LabelForPercentile(double percentile)
    {
        if (!double.IsFinite(percentile) || percentile < 0 || percentile > 1)
            throw new InvalidDataException("Aesthetic percentile must be between 0 and 1.");
        return percentile switch
        {
            >= 0.95 => "masterpiece",
            >= 0.85 => "best",
            >= 0.75 => "great",
            >= 0.50 => "good",
            >= 0.25 => "normal",
            >= 0.10 => "low",
            _ => "worst"
        };
    }
}
