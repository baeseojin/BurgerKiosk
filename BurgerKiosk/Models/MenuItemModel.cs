using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BurgerKiosk.Models
{
    public class MenuItemModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _stockQuantity;

        public long Id { get; set; }
        public long? BurgerProductId { get; set; }
        public string? SetOptionKey { get; set; }
        private string _name = "";
        private int _price = 0;
        private string _category = "";
        private bool _isRecommended = false;
        private string _imagePath = "";
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnPropertyChanged(); }
        }
        public int Price
        {
            get => _price;
            set { if (_price == value) return; _price = value; OnPropertyChanged(); }
        }
        public string Category
        {
            get => _category;
            set { if (_category == value) return; _category = value; OnPropertyChanged(); }
        }
        public bool IsRecommended
        {
            get => _isRecommended;
            set { if (_isRecommended == value) return; _isRecommended = value; OnPropertyChanged(); }
        }
        public string ImagePath
        {
            get => _imagePath;
            set { if (_imagePath == value) return; _imagePath = value; OnPropertyChanged(); }
        }

        public int StockQuantity
        {
            get => _stockQuantity;
            set
            {
                if (_stockQuantity == value) return;
                _stockQuantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSoldOut));
            }
        }

        public bool IsSoldOut => StockQuantity <= 0;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
