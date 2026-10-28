using System.ComponentModel;

namespace BurgerKiosk.Models
{
    public class CartItemModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public MenuItemModel Menu { get; }
        public string OptionType { get; }
        public string Side { get; }
        public string Drink { get; }
        public int UnitPrice { get; }

        private int quantity = 1;

        public int Quantity
        {
            get => quantity;
            set
            {
                if (quantity == value) return;

                quantity = value;
                OnPropertyChanged(nameof(Quantity));
                OnPropertyChanged(nameof(TotalPrice));
            }
        }

        public string Name
        {
            get
            {
                if (OptionType == "세트")
                {
                    return Menu.Name.EndsWith(" 세트")
                        ? Menu.Name
                        : Menu.Name + " 세트";
                }

                if (OptionType == "단품")
                {
                    return Menu.Name + " 단품";
                }

                return Menu.Name;
            }
        }


        public string OptionDescription =>
            OptionType == "세트"
                ? $"{Side} / {Drink}"
                : "";

        public int TotalPrice => UnitPrice * Quantity;

        public CartItemModel(
            MenuItemModel menu,
            string optionType = "",
            string side = "",
            string drink = "",
            int? unitPrice = null)
        {
            Menu = menu;
            OptionType = optionType;
            Side = side;
            Drink = drink;
            UnitPrice = unitPrice ?? menu.Price;
        }

        public bool HasSameOptions(
            MenuItemModel menu,
            string optionType,
            string side,
            string drink,
            int unitPrice)
        {
            return Menu == menu
                && OptionType == optionType
                && Side == side
                && Drink == drink
                && UnitPrice == unitPrice;
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name));
        }
    }
}
