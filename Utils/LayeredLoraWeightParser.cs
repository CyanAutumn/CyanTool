using CyanTools.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CyanTools.Utils
{
    public static class LayeredLoraWeightParser
    {
        private static readonly char[] Separators = { ',', ';', '\r', '\n', '\t', ' ' };

        public static LayeredLoraWeights ParseReportWeights(string text, double baseAlpha)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new FormatException($"请输入 {LayeredLoraWeights.ReportLayerCount} 个分层权重。");
            }

            var values = new List<double>();
            foreach (var token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    throw new FormatException($"无法解析分层权重 \"{token}\"。");
                }

                values.Add(value);
            }

            if (values.Count != LayeredLoraWeights.ReportLayerCount)
            {
                throw new FormatException($"分层权重数量必须为 {LayeredLoraWeights.ReportLayerCount}，当前为 {values.Count}。");
            }

            return new LayeredLoraWeights(baseAlpha, values);
        }

        public static string FormatReportWeights(IEnumerable<double> values)
        {
            return string.Join(",", values.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)));
        }
    }
}
