using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace CyanTools.Model
{
    public static class LayeredLoraWeightText
    {
        private static readonly char[] ValueSeparators = { ',', '，', ';', '；', '\r', '\n', '\t', ' ' };

        public static IReadOnlyList<double> ParseValues(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var values = new List<double>();
            foreach (var part in text.Split(ValueSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParseValue(part, out var value))
                {
                    throw new FormatException($"Invalid LoRA layer weight: {part}");
                }

                values.Add(value);
            }

            return values;
        }

        public static bool TryParseValues(string text, out IReadOnlyList<double> values)
        {
            try
            {
                values = ParseValues(text ?? string.Empty);
                return true;
            }
            catch (FormatException)
            {
                values = Array.Empty<double>();
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                values = Array.Empty<double>();
                return false;
            }
        }

        public static bool TryParseValue(string? text, out double value)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                value = 0d;
                return false;
            }

            if (double.IsNaN(parsed) || double.IsInfinity(parsed))
            {
                value = 0d;
                return false;
            }

            value = NormalizeSliderValue(parsed);
            return true;
        }

        public static string ToCommaText(IEnumerable<double> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            return string.Join(",", values.Select(FormatCompactValue));
        }

        public static string FormatEditorValue(double value)
        {
            return NormalizeSliderValue(value).ToString("0.00", CultureInfo.InvariantCulture);
        }

        public static string FormatCompactValue(double value)
        {
            return NormalizeSliderValue(value).ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static double NormalizeSliderValue(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Layer slider values must be finite numbers.");
            }

            return Math.Max(0d, Math.Min(1d, value));
        }
    }

    public enum LayeredLoraArchitecture
    {
        Auto,
        Sdxl,
        Anima,
        AnimaSdxlCompatible
    }

    public enum LoraLayerSlot
    {
        Base,
        In00,
        In01,
        In02,
        In03,
        In04,
        In05,
        In06,
        In07,
        In08,
        In09,
        In10,
        In11,
        Mid00,
        Out00,
        Out01,
        Out02,
        Out03,
        Out04,
        Out05,
        Out06,
        Out07,
        Out08,
        Out09,
        Out10,
        Out11,
        Unknown
    }

    public sealed class LayeredLoraWeights
    {
        public const int ReportLayerCount = 25;
        private static readonly (double Position, double Value)[] AnimaStyleToCompositionAnchors =
        {
            (0.00d, 1.00d),
            (0.12d, 0.95d),
            (0.25d, 0.82d),
            (0.38d, 0.45d),
            (0.50d, 0.20d),
            (0.62d, 0.20d),
            (0.75d, 0.35d),
            (0.88d, 0.60d),
            (1.00d, 0.80d)
        };

        public static readonly IReadOnlyList<LoraLayerSlot> ReportLayerSlots = new[]
        {
            LoraLayerSlot.In00,
            LoraLayerSlot.In01,
            LoraLayerSlot.In02,
            LoraLayerSlot.In03,
            LoraLayerSlot.In04,
            LoraLayerSlot.In05,
            LoraLayerSlot.In06,
            LoraLayerSlot.In07,
            LoraLayerSlot.In08,
            LoraLayerSlot.In09,
            LoraLayerSlot.In10,
            LoraLayerSlot.In11,
            LoraLayerSlot.Mid00,
            LoraLayerSlot.Out00,
            LoraLayerSlot.Out01,
            LoraLayerSlot.Out02,
            LoraLayerSlot.Out03,
            LoraLayerSlot.Out04,
            LoraLayerSlot.Out05,
            LoraLayerSlot.Out06,
            LoraLayerSlot.Out07,
            LoraLayerSlot.Out08,
            LoraLayerSlot.Out09,
            LoraLayerSlot.Out10,
            LoraLayerSlot.Out11
        };

        private readonly double[] _layerWeights;
        private readonly Dictionary<int, double> _animaBlockWeights;

        public LayeredLoraWeights(
            double baseAlpha,
            IEnumerable<double> layerWeights,
            IEnumerable<KeyValuePair<int, double>>? animaBlockWeights = null)
        {
            BaseAlpha = ValidateFinite(baseAlpha, nameof(baseAlpha));
            _layerWeights = layerWeights.Select((value, index) => ValidateFinite(value, $"layerWeights[{index}]")).ToArray();
            if (_layerWeights.Length != ReportLayerCount)
            {
                throw new ArgumentException($"Layer weight count must be {ReportLayerCount}.", nameof(layerWeights));
            }

            _animaBlockWeights = new Dictionary<int, double>();
            if (animaBlockWeights != null)
            {
                foreach (var item in animaBlockWeights)
                {
                    if (item.Key < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(animaBlockWeights), "Anima block indices must be non-negative.");
                    }

                    _animaBlockWeights[item.Key] = ValidateFinite(item.Value, $"animaBlockWeights[{item.Key}]");
                }
            }
        }

        public double BaseAlpha { get; }

        public IReadOnlyList<double> LayerWeights => _layerWeights;

        public IReadOnlyDictionary<int, double> AnimaBlockWeights => _animaBlockWeights;

        public double GetLayerWeight(LoraLayerSlot slot)
        {
            if (slot == LoraLayerSlot.Base || slot == LoraLayerSlot.Unknown)
            {
                return BaseAlpha;
            }

            int index = GetReportLayerIndex(slot);
            return index >= 0 ? _layerWeights[index] : BaseAlpha;
        }

        public double GetAnimaBlockWeight(int blockIndex)
        {
            return _animaBlockWeights.TryGetValue(blockIndex, out var value)
                ? value
                : BaseAlpha;
        }

        public string ToReportText()
        {
            return string.Join(",", _layerWeights.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        public string ToAnimaBlockText()
        {
            return string.Join(
                ",",
                _animaBlockWeights
                    .OrderBy(item => item.Key)
                    .Select(item => $"{item.Key}:{item.Value.ToString("0.###", CultureInfo.InvariantCulture)}"));
        }

        public static int GetReportLayerIndex(LoraLayerSlot slot)
        {
            for (int i = 0; i < ReportLayerSlots.Count; i++)
            {
                if (ReportLayerSlots[i] == slot)
                {
                    return i;
                }
            }

            return -1;
        }

        public static LayeredLoraWeights CreateUniform(double baseAlpha, double layerWeight)
        {
            return new LayeredLoraWeights(baseAlpha, Enumerable.Repeat(layerWeight, ReportLayerCount));
        }

        public static IReadOnlyList<LayeredLoraLayerWeight> CreateReportLayerItems(IEnumerable<double> values)
        {
            var layerValues = values.Select((value, index) => ValidateFinite(value, $"values[{index}]")).ToArray();
            if (layerValues.Length != ReportLayerCount)
            {
                throw new ArgumentException($"Layer weight count must be {ReportLayerCount}.", nameof(values));
            }

            return ReportLayerSlots
                .Select((slot, index) => new LayeredLoraLayerWeight(slot, GetReportLayerDisplayName(slot), layerValues[index]))
                .ToArray();
        }

        public static IReadOnlyList<LayeredLoraBlockWeight> CreateAnimaBlockItems(IEnumerable<int> blockIndices, double defaultValue)
        {
            if (blockIndices == null)
            {
                throw new ArgumentNullException(nameof(blockIndices));
            }

            _ = ValidateFinite(defaultValue, nameof(defaultValue));
            return blockIndices
                .Where(index => index >= 0)
                .Distinct()
                .OrderBy(index => index)
                .Select(index => new LayeredLoraBlockWeight(index, $"Block {index:00}", defaultValue))
                .ToArray();
        }

        public static IReadOnlyList<LayeredLoraBlockWeight> CreateAnimaStyleToCompositionPresetItems(IEnumerable<int> blockIndices)
        {
            if (blockIndices == null)
            {
                throw new ArgumentNullException(nameof(blockIndices));
            }

            var indices = blockIndices
                .Where(index => index >= 0)
                .Distinct()
                .OrderBy(index => index)
                .ToArray();
            if (indices.Length == 0)
            {
                return Array.Empty<LayeredLoraBlockWeight>();
            }

            int maxIndex = indices[^1];
            return indices
                .Select(index =>
                {
                    double position = maxIndex > 0 ? index / (double)maxIndex : 0d;
                    return new LayeredLoraBlockWeight(index, $"Block {index:00}", InterpolateAnimaStyleToCompositionWeight(position));
                })
                .ToArray();
        }

        public static LayeredLoraWeights CreateFromLayerItems(double baseAlpha, IEnumerable<LayeredLoraLayerWeight> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var valuesBySlot = new Dictionary<LoraLayerSlot, double>();
            foreach (var item in items)
            {
                if (!ReportLayerSlots.Contains(item.Slot))
                {
                    continue;
                }

                valuesBySlot[item.Slot] = item.Value;
            }

            var values = new double[ReportLayerCount];
            for (int i = 0; i < ReportLayerSlots.Count; i++)
            {
                var slot = ReportLayerSlots[i];
                if (!valuesBySlot.TryGetValue(slot, out var value))
                {
                    throw new ArgumentException($"Missing layer weight for {GetReportLayerDisplayName(slot)}.", nameof(items));
                }

                values[i] = value;
            }

            return new LayeredLoraWeights(baseAlpha, values);
        }

        public static LayeredLoraWeights CreateFromAnimaBlockItems(double baseAlpha, IEnumerable<LayeredLoraBlockWeight> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var blockWeights = items
                .Where(item => item.BlockIndex >= 0)
                .GroupBy(item => item.BlockIndex)
                .Select(group => new KeyValuePair<int, double>(group.Key, group.Last().Value))
                .ToArray();
            return new LayeredLoraWeights(
                baseAlpha,
                Enumerable.Repeat(baseAlpha, ReportLayerCount),
                blockWeights);
        }

        public static string GetReportLayerDisplayName(LoraLayerSlot slot)
        {
            var index = GetReportLayerIndex(slot);
            if (index < 0)
            {
                return slot.ToString();
            }

            if (slot == LoraLayerSlot.Mid00)
            {
                return "M00";
            }

            if (slot >= LoraLayerSlot.In00 && slot <= LoraLayerSlot.In11)
            {
                return $"IN{index:00}";
            }

            return $"OUT{index - 13:00}";
        }

        private static double ValidateFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name, "Layered LoRA weights must be finite numbers.");
            }

            return value;
        }

        private static double InterpolateAnimaStyleToCompositionWeight(double position)
        {
            position = Math.Max(0d, Math.Min(1d, position));
            for (int i = 1; i < AnimaStyleToCompositionAnchors.Length; i++)
            {
                var previous = AnimaStyleToCompositionAnchors[i - 1];
                var current = AnimaStyleToCompositionAnchors[i];
                if (position <= current.Position)
                {
                    double range = current.Position - previous.Position;
                    if (range <= 0d)
                    {
                        return current.Value;
                    }

                    double t = (position - previous.Position) / range;
                    return previous.Value + ((current.Value - previous.Value) * t);
                }
            }

            return AnimaStyleToCompositionAnchors[^1].Value;
        }
    }

    public sealed class LayeredLoraLayerWeight : INotifyPropertyChanged
    {
        private double _value;

        public LayeredLoraLayerWeight(LoraLayerSlot slot, string displayName, double value)
        {
            Slot = slot;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            _value = ValidateSliderValue(value);
        }

        public LoraLayerSlot Slot { get; }

        public string DisplayName { get; }

        public double Value
        {
            get => _value;
            set => SetValue(value, notifyText: true);
        }

        public string ValueText
        {
            get => LayeredLoraWeightText.FormatEditorValue(_value);
            set
            {
                if (LayeredLoraWeightText.TryParseValue(value, out var parsed))
                {
                    SetValue(parsed, notifyText: false);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private static double ValidateSliderValue(double value)
        {
            return LayeredLoraWeightText.NormalizeSliderValue(value);
        }

        private void SetValue(double value, bool notifyText)
        {
            var normalized = ValidateSliderValue(value);
            if (Math.Abs(_value - normalized) < 0.0000001d)
            {
                return;
            }

            _value = normalized;
            OnPropertyChanged(nameof(Value));
            if (notifyText)
            {
                OnPropertyChanged(nameof(ValueText));
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class LayeredLoraBlockWeight : INotifyPropertyChanged
    {
        private double _value;

        public LayeredLoraBlockWeight(int blockIndex, string displayName, double value)
        {
            if (blockIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex), "Anima block index must be non-negative.");
            }

            BlockIndex = blockIndex;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            _value = ValidateSliderValue(value);
        }

        public int BlockIndex { get; }

        public string DisplayName { get; }

        public double Value
        {
            get => _value;
            set => SetValue(value, notifyText: true);
        }

        public string ValueText
        {
            get => LayeredLoraWeightText.FormatEditorValue(_value);
            set
            {
                if (LayeredLoraWeightText.TryParseValue(value, out var parsed))
                {
                    SetValue(parsed, notifyText: false);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private static double ValidateSliderValue(double value)
        {
            return LayeredLoraWeightText.NormalizeSliderValue(value);
        }

        private void SetValue(double value, bool notifyText)
        {
            var normalized = ValidateSliderValue(value);
            if (Math.Abs(_value - normalized) < 0.0000001d)
            {
                return;
            }

            _value = normalized;
            OnPropertyChanged(nameof(Value));
            if (notifyText)
            {
                OnPropertyChanged(nameof(ValueText));
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
