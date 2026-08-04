using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CyanTools.Model
{
    public enum ParameterValueKind
    {
        String,
        Number,
        Boolean,
        Raw
    }

    public class TrainingParameter : INotifyPropertyChanged
    {
        private string _value;
        private bool _boolValue;

        public string Key { get; }
        public ParameterValueKind ValueKind { get; }
        public string DisplayLabel { get; }
        public string Description { get; }
        public IReadOnlyList<string> Options { get; }
        public bool HasOptions => Options != null && Options.Count > 0;

        public string Value
        {
            get => _value;
            set => SetField(ref _value, value);
        }

        public bool BoolValue
        {
            get => _boolValue;
            set => SetField(ref _boolValue, value);
        }

        public TrainingParameter(string key, ParameterValueKind kind, string value, string displayLabel = "", string description = "", IReadOnlyList<string> options = null)
        {
            Key = key;
            ValueKind = kind;
            Description = description;
            DisplayLabel = string.IsNullOrWhiteSpace(displayLabel) ? key : displayLabel;
            Options = options;
            if (kind == ParameterValueKind.Boolean)
            {
                bool.TryParse(value, out _boolValue);
            }
            else
            {
                _value = value;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

    public class TrainingSection
    {
        public string Name { get; }
        public string DisplayName { get; }
        public ObservableCollection<TrainingParameter> Parameters { get; }

        public TrainingSection(string name, string displayName, ObservableCollection<TrainingParameter> parameters)
        {
            Name = name;
            DisplayName = displayName;
            Parameters = parameters;
        }
    }
}
