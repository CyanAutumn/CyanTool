using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace CyanTools.Model
{
    public class DatasetDataItem : INotifyPropertyChanged
    {
        private string _operation = "";
        private string _baseName = "";
        private string? _imagePath;
        private string? _textPath;

        public string Operation
        {
            get => _operation;
            set => SetField(ref _operation, value);
        }

        public string BaseName
        {
            get => _baseName;
            set
            {
                if (SetField(ref _baseName, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(ImageDisplayName));
                }
            }
        }

        public string? ImagePath
        {
            get => _imagePath;
            set
            {
                if (SetField(ref _imagePath, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(ImageDisplayName));
                }
            }
        }

        public string? TextPath
        {
            get => _textPath;
            set
            {
                if (SetField(ref _textPath, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(ImageDisplayName));
                }
            }
        }

        public string DisplayName
        {
            get
            {
                var imageName = string.IsNullOrEmpty(ImagePath) ? null : Path.GetFileName(ImagePath);
                var txtName = string.IsNullOrEmpty(TextPath) ? null : Path.GetFileName(TextPath);

                if (!string.IsNullOrEmpty(imageName) && !string.IsNullOrEmpty(txtName))
                {
                    return $"{imageName} + {txtName}";
                }

                if (!string.IsNullOrEmpty(imageName))
                {
                    return imageName;
                }

                if (!string.IsNullOrEmpty(txtName))
                {
                    return txtName;
                }

                return BaseName;
            }
        }

        public string ImageDisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(ImagePath))
                {
                    return Path.GetFileName(ImagePath);
                }

                if (!string.IsNullOrEmpty(TextPath))
                {
                    return Path.GetFileName(TextPath);
                }

                return BaseName;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
