using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using CyanTools.Utils;

namespace CyanTools.ViewModel
{
    public sealed class XyzToolViewModel : INotifyPropertyChanged
    {
        private const int SequenceWidth = 6;
        private const int MaxPromptItems = 10000;

        private string _xyzStartText = "1";
        private string _xyzEndText = "100";
        private string _xyzIntervalText = "1";
        private string _xyzPromptText = string.Empty;

        public XyzToolViewModel()
        {
            CopyXyzPromptCommand = new RelayCommand(CopyXyzPrompt, () => !string.IsNullOrWhiteSpace(XyzPromptText));
            RefreshXyzPrompt();
        }

        public string XyzStartText
        {
            get => _xyzStartText;
            set
            {
                if (SetField(ref _xyzStartText, value ?? string.Empty, nameof(XyzStartText)))
                {
                    RefreshXyzPrompt();
                }
            }
        }

        public string XyzEndText
        {
            get => _xyzEndText;
            set
            {
                if (SetField(ref _xyzEndText, value ?? string.Empty, nameof(XyzEndText)))
                {
                    RefreshXyzPrompt();
                }
            }
        }

        public string XyzIntervalText
        {
            get => _xyzIntervalText;
            set
            {
                if (SetField(ref _xyzIntervalText, value ?? string.Empty, nameof(XyzIntervalText)))
                {
                    RefreshXyzPrompt();
                }
            }
        }

        public string XyzPromptText
        {
            get => _xyzPromptText;
            private set
            {
                if (SetField(ref _xyzPromptText, value, nameof(XyzPromptText)))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ICommand CopyXyzPromptCommand { get; }

        private void RefreshXyzPrompt()
        {
            XyzPromptText = BuildXyzPrompt(XyzStartText, XyzEndText, XyzIntervalText);
        }

        private static string BuildXyzPrompt(string startText, string endText, string intervalText)
        {
            if (!int.TryParse(startText, out var start) ||
                !int.TryParse(endText, out var end) ||
                !int.TryParse(intervalText, out var interval) ||
                interval <= 0)
            {
                return string.Empty;
            }

            var values = new StringBuilder();
            var count = 0;

            if (start <= end)
            {
                for (var value = start; value <= end; value += interval)
                {
                    if (!AppendSequenceValue(values, value, ref count))
                    {
                        break;
                    }
                }
            }
            else
            {
                for (var value = start; value >= end; value -= interval)
                {
                    if (!AppendSequenceValue(values, value, ref count))
                    {
                        break;
                    }
                }
            }

            return values.ToString();
        }

        private static bool AppendSequenceValue(StringBuilder builder, int value, ref int count)
        {
            if (count >= MaxPromptItems)
            {
                return false;
            }

            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append(value.ToString($"D{SequenceWidth}"));
            count++;
            return true;
        }

        private void CopyXyzPrompt()
        {
            if (!string.IsNullOrWhiteSpace(XyzPromptText))
            {
                Clipboard.SetText(XyzPromptText);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
