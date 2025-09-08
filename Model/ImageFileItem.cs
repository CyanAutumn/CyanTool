using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CyanTools.Model
{
    public class ImageFileItem : INotifyPropertyChanged
    {
        private string _operation = "";
        private string _fileName;
        private string _fullPath;

        public string Operation
        {
            get => _operation;
            set => SetField(ref _operation, value);
        }

        public string FileName
        {
            get => _fileName;
            set => SetField(ref _fileName, value);
        }

        public string FullPath
        {
            get => _fullPath;
            set => SetField(ref _fullPath, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}