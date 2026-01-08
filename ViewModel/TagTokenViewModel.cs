using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CyanTools.ViewModel
{
    public sealed class TagTokenViewModel : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        private string _editingText = string.Empty;
        private string? _translation;
        private bool _isEditing;
        private bool _isTranslating;

        public string Text
        {
            get => _text;
            set
            {
                if (SetField(ref _text, value))
                {
                    if (string.IsNullOrEmpty(EditingText))
                    {
                        EditingText = value;
                    }
                }
            }
        }

        public string EditingText
        {
            get => _editingText;
            set => SetField(ref _editingText, value);
        }

        public string? Translation
        {
            get => _translation;
            set
            {
                if (SetField(ref _translation, value))
                {
                    OnPropertyChanged(nameof(TranslationDisplay));
                }
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set => SetField(ref _isEditing, value);
        }

        public bool IsTranslating
        {
            get => _isTranslating;
            set
            {
                if (SetField(ref _isTranslating, value))
                {
                    OnPropertyChanged(nameof(TranslationDisplay));
                }
            }
        }

        public string TranslationDisplay => IsTranslating ? "翻译中..." : (Translation ?? string.Empty);

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
