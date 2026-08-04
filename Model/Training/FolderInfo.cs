using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CyanTools.Model.Training
{
    public class FolderInfo : INotifyPropertyChanged
    {
        private string _folderName = string.Empty;
        public string FolderName
        {
            get => _folderName;
            set => SetField(ref _folderName, value);
        }

        private int _imageCount;
        public int ImageCount
        {
            get => _imageCount;
            set => SetField(ref _imageCount, value);
        }

        private int _repetitions;
        public int Repetitions
        {
            get => _repetitions;
            set => SetField(ref _repetitions, value);
        }

        private double _stepPercentage;
        public double StepPercentage
        {
            get => _stepPercentage;
            set => SetField(ref _stepPercentage, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

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
