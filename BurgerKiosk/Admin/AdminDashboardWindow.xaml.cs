using BurgerKiosk.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BurgerKiosk
{
    public partial class AdminDashboardWindow : UserControl
    {
        private readonly LoyaltyService _loyalty;
        private bool _loyaltyBusy;
        private readonly AdminInventoryService _inventory;
        private readonly AdminStatisticsService _statistics;
        private readonly AdminOrderCancelService _cancelService;
        private readonly ObservableCollection<AdminCancelOrder> _cancelOrders = new();
        private bool _cancelBusy;
        private bool _statsBusy;
        private readonly ObservableCollection<AdminStockItem> _stocks = new();
        private bool _busy;
        private bool _menuBusy;
        private readonly AdminMenuService _menuService;
        private readonly ObservableCollection<AdminMenuItem> _menus = new();
        private long? _editingMenuId;

        public AdminDashboardWindow(AdminInventoryService inventory, AdminMenuService menuService, AdminStatisticsService statistics, AdminOrderCancelService cancelService, LoyaltyService loyalty)
        {
            InitializeComponent();
            _loyalty = loyalty;
            _inventory = inventory;
            _statistics = statistics;
            _cancelService = cancelService;
            CancelOrderGrid.ItemsSource = _cancelOrders;
            var today = DateTime.Today;
            StatsStartDate.SelectedDate = today;
            StatsEndDate.SelectedDate = today;
            _menuService = menuService;
            MenuGrid.ItemsSource = _menus;
            StockGrid.ItemsSource = _stocks;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshLoyaltyAsync();
            await RefreshAsync();
            await RefreshMenusAsync();
            await RefreshStatisticsAsync();
            await RefreshCancelOrdersAsync();
        }
        private async System.Threading.Tasks.Task RefreshLoyaltyAsync()
        {
            if (_loyaltyBusy) return; _loyaltyBusy = true;
            try { LoyaltyMemberGrid.ItemsSource = await _loyalty.GetMembersAsync(); LoyaltyAdminStatus.Text = "최근 등록 회원을 조회했습니다. 적립률은 1%입니다."; }
            catch (Exception ex) { LoyaltyAdminStatus.Text = ex.Message; }
            finally { _loyaltyBusy = false; }
        }
        private async void LoyaltyRefresh_Click(object sender, RoutedEventArgs e) => await RefreshLoyaltyAsync();
        private void MemberPhoneKey_Click(object sender, RoutedEventArgs e)
        {
            if (_loyaltyBusy || sender is not Button b) return;
            string key = b.Tag?.ToString() ?? "";
            if (key == "CLEAR") MemberPhoneBox.Clear();
            else if (key == "BACK") MemberPhoneBox.Text = MemberPhoneBox.Text.Length > 0 ? MemberPhoneBox.Text[..^1] : "";
            else if (MemberPhoneBox.Text.Length < 11) MemberPhoneBox.Text += key;
        }
        private async void LoyaltyRegister_Click(object sender, RoutedEventArgs e)
        {
            if (_loyaltyBusy) return;
            if (!System.Text.RegularExpressions.Regex.IsMatch(MemberPhoneBox.Text, "^01[016789][0-9]{7,8}$"))
            { LoyaltyAdminStatus.Text = "휴대전화번호 10~11자리를 입력해 주세요."; return; }
            _loyaltyBusy = true;
            try { await _loyalty.RegisterAsync(MemberPhoneBox.Text); MemberPhoneBox.Clear(); LoyaltyAdminStatus.Text = "회원 등록 완료"; }
            catch (Exception ex) { LoyaltyAdminStatus.Text = ex.Message; }
            finally { _loyaltyBusy = false; }
            await RefreshLoyaltyAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

        private async System.Threading.Tasks.Task RefreshAsync(long? selectId = null)
        {
            if (_busy) return;
            _busy = true;
            SaveStockButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;
            StatusText.Text = "최신 재고를 불러오는 중...";
            try
            {
                var items = await _inventory.GetStockAsync();
                _stocks.Clear();
                foreach (var item in items.OrderBy(x => x.Category).ThenBy(x => x.Name))
                    _stocks.Add(item);
                StatusText.Text = $"총 {_stocks.Count}개 상품 조회 완료 · 품절 {_stocks.Count(x => x.IsAvailable && x.StockQuantity == 0)}개";
                if (selectId.HasValue)
                    StockGrid.SelectedItem = _stocks.FirstOrDefault(x => x.Id == selectId.Value);
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
            finally
            {
                _busy = false;
                SaveStockButton.IsEnabled = true;
                RefreshButton.IsEnabled = true;
            }
        }

        private void StockGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StockGrid.SelectedItem is AdminStockItem item)
            {
                SelectedProductText.Text = $"선택: {item.Name} (상품 ID {item.Id})";
                StockQuantityBox.Text = item.StockQuantity.ToString();
            }
            else
            {
                SelectedProductText.Text = "상품을 선택해 주세요.";
                StockQuantityBox.Clear();
            }
        }

        private async void SaveStockButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || StockGrid.SelectedItem is not AdminStockItem item) return;
            if (!int.TryParse(StockQuantityBox.Text, out var count) || count < 0 || count > 1000000)
            {
                MessageBox.Show("0~1,000,000 사이의 정수를 입력하세요.", "입력 오류");
                return;
            }
            if (MessageBox.Show($"{item.Name} 재고를 {item.StockQuantity}개에서 {count}개로 변경할까요?",
                "재고 변경 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _busy = true;
            SaveStockButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;
            try
            {
                await _inventory.SetStockAsync(item.Id, count);
                StatusText.Text = $"{item.Name} 재고가 {count}개로 변경되었습니다.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "재고 변경 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _busy = false;
                SaveStockButton.IsEnabled = true;
                RefreshButton.IsEnabled = true;
            }
            await RefreshAsync(item.Id);
        }


        private async System.Threading.Tasks.Task RefreshMenusAsync(long? selectedId = null)
        {
            if (_menuBusy) return;
            _menuBusy = true;
            MenuSaveButton.IsEnabled = false;
            MenuStopButton.IsEnabled = false;
            MenuDeleteButton.IsEnabled = false;
            MenuRefreshButton.IsEnabled = false;
            MenuStatusText.Text = "메뉴 정보를 조회하는 중...";
            try
            {
                var items = await _menuService.GetMenusAsync();
                _menus.Clear();
                foreach (var item in items) _menus.Add(item);
                MenuBurgerBox.ItemsSource = _menus.Where(x => x.Category == "버거").ToList();
                MenuStatusText.Text = $"{_menus.Count}개 상품 조회 완료";
                if (selectedId.HasValue)
                    MenuGrid.SelectedItem = _menus.FirstOrDefault(x => x.Id == selectedId.Value);
            }
            catch (Exception ex) { MenuStatusText.Text = ex.Message; }
            finally
            {
                _menuBusy = false;
                MenuSaveButton.IsEnabled = true;
                MenuStopButton.IsEnabled = true;
                MenuDeleteButton.IsEnabled = true;
                MenuRefreshButton.IsEnabled = true;
            }
        }

        private async void MenuRefresh_Click(object sender, RoutedEventArgs e) => await RefreshMenusAsync();
        private void MenuCategoryChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MenuStockBox == null || MenuStockLabel == null) return;
            bool isSet = (MenuCategoryBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "세트";
            MenuStockBox.Visibility = isSet ? Visibility.Collapsed : Visibility.Visible;
            MenuStockLabel.Text = isSet ? "세트는 버거·사이드·음료 재고를 사용합니다." : "새 상품 초기 재고 (수정은 재고 관리 탭)";
            if (isSet) MenuStockBox.Text = "0";
        }

        private void MenuNew_Click(object sender, RoutedEventArgs e)
        {
            MenuGrid.SelectedItem = null;
            _editingMenuId = null;
            MenuNameBox.Clear(); MenuCategoryBox.SelectedIndex = 0;
            MenuPriceBox.Text = "0"; MenuStockBox.Text = "0";
            MenuDescriptionBox.Clear(); MenuImageBox.Clear(); MenuBurgerBox.SelectedItem = null;
            MenuAvailableBox.IsChecked = true;
            MenuRecommendedBox.IsChecked = false;
            MenuStockBox.IsReadOnly = false;
            MenuStatusText.Text = "새 상품을 입력하고 저장하세요.";
        }

        private void MenuGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MenuGrid.SelectedItem is not AdminMenuItem item) return;
            _editingMenuId = item.Id;
            MenuNameBox.Text = item.Name;
            for (int i = 0; i < MenuCategoryBox.Items.Count; i++)
            {
                if ((MenuCategoryBox.Items[i] as ComboBoxItem)?.Content?.ToString() == item.Category)
                { MenuCategoryBox.SelectedIndex = i; break; }
            }
            MenuBurgerBox.SelectedValue = item.BurgerProductId;
            MenuPriceBox.Text = item.Price.ToString();
            MenuStockBox.Text = item.StockQuantity.ToString();
            MenuStockBox.IsReadOnly = true;
            MenuDescriptionBox.Text = item.Description ?? "";
            MenuImageBox.Text = item.ImageUrl ?? "";
            MenuAvailableBox.IsChecked = item.IsAvailable;
            MenuRecommendedBox.IsChecked = item.IsRecommended;
        }

        private bool TryGetMenu(out AdminMenuItem menu)
        {
            menu = new AdminMenuItem();
            var name = MenuNameBox.Text.Trim();
            var category = (MenuCategoryBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            if (name.Length == 0 || name.Length > 100 ||
                !int.TryParse(MenuPriceBox.Text, out var price) || price < 0 || price > 1000000 ||
                !int.TryParse(MenuStockBox.Text, out var stock) || stock < 0 || stock > 1000000)
            {
                MessageBox.Show("상품명(1~100자), 가격, 재고(0~1,000,000)를 확인하세요.", "입력 오류");
                return false;
            }
            if (category == "세트" && MenuBurgerBox.SelectedItem is not AdminMenuItem)
            { MessageBox.Show("세트에 연결할 버거를 선택하세요.", "입력 오류"); return false; }
            var url = MenuImageBox.Text.Trim();
            if (url.Length != 0 && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("이미지는 HTTPS URL을 사용하세요.", "입력 오류");
                return false;
            }
            menu = new AdminMenuItem
            {
                Id = _editingMenuId ?? 0, Name = name, Category = category,
                BurgerProductId = category == "세트" ? (MenuBurgerBox.SelectedItem as AdminMenuItem)?.Id : null,
                Price = price, StockQuantity = stock, Description = MenuDescriptionBox.Text.Trim(),
                ImageUrl = url, IsAvailable = MenuAvailableBox.IsChecked == true,
                IsRecommended = MenuRecommendedBox.IsChecked == true
            };
            return true;
        }

        private async void MenuSave_Click(object sender, RoutedEventArgs e)
        {
            if (_menuBusy || !TryGetMenu(out var item)) return;
            if (MessageBox.Show($"'{item.Name}' 상품을 저장하시겠습니까?", "상품 저장",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _menuBusy = true; MenuSaveButton.IsEnabled = false;
            try
            {
                var id = await _menuService.SaveMenuAsync(item);
                MenuStatusText.Text = $"상품 저장 완료 (ID: {id})";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "상품 저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally { _menuBusy = false; MenuSaveButton.IsEnabled = true; }
            await RefreshMenusAsync(item.Id == 0 ? null : item.Id);
        }

        private async void MenuStop_Click(object sender, RoutedEventArgs e)
        {
            if (_menuBusy || !_editingMenuId.HasValue) return;
            if (MessageBox.Show("이 상품을 판매 중지할까요? 기존 주문 내역은 유지됩니다.",
                "판매 중지", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _menuBusy = true; MenuStopButton.IsEnabled = false;
            var id = _editingMenuId.Value;
            try { await _menuService.StopSellingAsync(id); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "판매 중지 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally { _menuBusy = false; MenuStopButton.IsEnabled = true; }
            await RefreshMenusAsync(id);
        }

        private async void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_menuBusy || !_editingMenuId.HasValue ||
                MenuGrid.SelectedItem is not AdminMenuItem selected ||
                selected.Id != _editingMenuId.Value) return;

            var productId = selected.Id;
            var productName = selected.Name;
            var answer = MessageBox.Show(
                $"'{productName}' (ID: {productId}) 상품을 영구 삭제할까요?\n\n" +
                "주문 내역이 있는 상품은 삭제할 수 없습니다.\n" +
                "삭제 후 상품 및 재고를 복구할 수 없습니다.",
                "상품 영구 삭제 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            _menuBusy = true;
            MenuDeleteButton.IsEnabled = false;
            MenuSaveButton.IsEnabled = false;
            MenuStopButton.IsEnabled = false;
            MenuRefreshButton.IsEnabled = false;
            try
            {
                await _menuService.DeleteMenuAsync(productId);
                _editingMenuId = null;
                MenuGrid.SelectedItem = null;
                MenuNameBox.Clear();
                MenuPriceBox.Clear();
                MenuStockBox.Clear();
                MenuDescriptionBox.Clear();
                MenuImageBox.Clear();
                MenuStatusText.Text = $"'{productName}' 상품이 삭제되었습니다.";
            }
            catch (AdminMenuDeleteException ex)
            {
                MessageBox.Show(ex.Message, "영구 삭제 불가", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "상품 삭제 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                _menuBusy = false;
                MenuDeleteButton.IsEnabled = true;
                MenuSaveButton.IsEnabled = true;
                MenuStopButton.IsEnabled = true;
                MenuRefreshButton.IsEnabled = true;
            }
            await RefreshMenusAsync();
            await RefreshAsync();
        }

        private async void StatsSearch_Click(object sender, RoutedEventArgs e)
            => await RefreshStatisticsAsync();

        private async System.Threading.Tasks.Task RefreshStatisticsAsync()
        {
            if (_statsBusy) return;
            if (!StatsStartDate.SelectedDate.HasValue || !StatsEndDate.SelectedDate.HasValue)
            {
                StatsStatusText.Text = "조회 시작일과 종료일을 선택해 주세요.";
                return;
            }
            var start = StatsStartDate.SelectedDate.Value.Date;
            var end = StatsEndDate.SelectedDate.Value.Date;
            if (start > end || (end - start).TotalDays > 366)
            {
                StatsStatusText.Text = "시작일은 종료일 이전이어야 하며 조회 범위는 최대 367일입니다.";
                return;
            }
            _statsBusy = true;
            StatsSearchButton.IsEnabled = false;
            StatsStatusText.Text = "주문 통계를 불러오는 중...";
            try
            {
                var result = await _statistics.GetStatisticsAsync(start, end);
                StatsOrderCount.Text = $"{result.OrderCount:N0}건";
                StatsTotalSales.Text = $"{result.TotalSales:N0}원";
                StatsDailyGrid.ItemsSource = result.Daily;
                StatsTopGrid.ItemsSource = result.TopProducts;
                StatsStatusText.Text = $"{start:yyyy-MM-dd} ~ {end:yyyy-MM-dd} 조회 완료 · 한국 시간 기준 · 상태가 '취소'인 주문 제외";
            }
            catch (Exception ex)
            {
                StatsStatusText.Text = ex.Message;
            }
            finally
            {
                _statsBusy = false;
                StatsSearchButton.IsEnabled = true;
            }
        }

        private async void CancelRefresh_Click(object sender, RoutedEventArgs e)
            => await RefreshCancelOrdersAsync();

        private async System.Threading.Tasks.Task RefreshCancelOrdersAsync()
        {
            if (_cancelBusy) return;
            _cancelBusy = true;
            CancelRefreshButton.IsEnabled = false;
            CancelOrderButton.IsEnabled = false;
            CancelStatusText.Text = "주문 목록을 불러오는 중...";
            try
            {
                var orders = await _cancelService.GetOrdersAsync();
                _cancelOrders.Clear();
                foreach (var item in orders) _cancelOrders.Add(item);
                CancelOrderGrid.SelectedItem = null;
                CancelStatusText.Text = $"최근 {_cancelOrders.Count}건 조회 완료";
            }
            catch (Exception ex)
            {
                CancelStatusText.Text = ex.Message;
            }
            finally
            {
                _cancelBusy = false;
                CancelRefreshButton.IsEnabled = true;
            }
        }

        private void CancelOrderGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CancelOrderGrid.SelectedItem is AdminCancelOrder order)
            {
                CancelSelectionText.Text = $"주문 #{order.Id} · {order.Status} · {order.PriceText}";
                CancelOrderButton.IsEnabled = !_cancelBusy && order.Status == "접수";
            }
            else
            {
                CancelSelectionText.Text = "취소할 주문을 선택하세요. 접수 상태만 취소할 수 있습니다.";
                CancelOrderButton.IsEnabled = false;
            }
        }

        private async void CancelOrder_Click(object sender, RoutedEventArgs e)
        {
            if (_cancelBusy || CancelOrderGrid.SelectedItem is not AdminCancelOrder selected || selected.Status != "접수")
                return;
            var id = selected.Id;
            var result = MessageBox.Show(
                $"주문 #{id}을 취소할까요?\n\n상품: {selected.ItemsText}\n금액: {selected.PriceText}\n\n취소 시 주문 상품의 재고가 복구됩니다.\n(실제 카드 환불은 처리되지 않습니다.)",
                "주문 취소 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;
            _cancelBusy = true;
            CancelOrderButton.IsEnabled = false;
            CancelRefreshButton.IsEnabled = false;
            try
            {
                await _cancelService.CancelAsync(id);
                MessageBox.Show($"주문 #{id} 취소 및 재고 복구 완료", "주문 취소", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "주문 취소 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _cancelBusy = false;
                CancelRefreshButton.IsEnabled = true;
            }
            await RefreshCancelOrdersAsync();
            await RefreshAsync();
            await RefreshStatisticsAsync();
        }

        public event EventHandler? ExitRequested;
        private void Close_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
