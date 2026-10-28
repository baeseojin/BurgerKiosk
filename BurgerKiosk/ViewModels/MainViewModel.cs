using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using BurgerKiosk.Commands;
using BurgerKiosk.Models;
using BurgerKiosk.Services;

namespace BurgerKiosk.ViewModels
{
    public partial class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }

         
         
         

        private readonly OrderService _orderService;

         
        public KakaoPayReadyService? KakaoPayReadyService { get; set; }
        public IKakaoPayStatusService? KakaoPayStatusService { get; set; }
        private System.Threading.CancellationTokenSource? _kakaoPollCts;
        private string? _kakaoOrderId;
        private string? _completedPaymentId;
        private int _kakaoPaymentAmount;
        private int _kakaoCompletionVersion;
        private bool _isKakaoCancelInProgress;
        private bool _recoveryRunning;
        private bool _recoveryRequired = true;
        public bool IsPaymentRecoveryRequired
        {
            get => _recoveryRequired;
            private set { _recoveryRequired = value; OnPropertyChanged(nameof(IsPaymentRecoveryRequired)); OnPropertyChanged(nameof(CanConfirmPayment)); OnPropertyChanged(nameof(CanStartOrder)); }
        }
        private bool _recoveryCheckFailed;
        public bool IsRecoveryCheckFailed
        {
            get => _recoveryCheckFailed;
            private set { _recoveryCheckFailed = value; OnPropertyChanged(nameof(IsRecoveryCheckFailed)); OnPropertyChanged(nameof(StartupStatusMessage)); }
        }
        public string StartupStatusMessage => IsRecoveryCheckFailed
            ? "주문 준비 정보를 불러오지 못했습니다. 연결 상태를 확인하고 다시 시도해 주세요."
            : "";
        public bool CanStartOrder => !IsPaymentRecoveryRequired && !IsSavingOrder && _kakaoOrderId == null && _completedPaymentId == null;
        public bool IsCardDemoEnabled => true;
        public string CardPaymentLabel => "신용/체크카드";
        private bool _isKakaoPaymentCompleted;
        public bool IsKakaoPaymentCompleted
        {
            get => _isKakaoPaymentCompleted;
            private set { _isKakaoPaymentCompleted = value; OnPropertyChanged(nameof(IsKakaoPaymentCompleted)); }
        }
        private bool _isKakaoPayVisible;
        public bool IsKakaoPayVisible
        {
            get => _isKakaoPayVisible;
            private set { _isKakaoPayVisible = value; OnPropertyChanged(nameof(IsKakaoPayVisible)); }
        }
        private Uri? _kakaoPaymentUri;
        public Uri? KakaoPaymentUri
        {
            get => _kakaoPaymentUri;
            private set { _kakaoPaymentUri = value; OnPropertyChanged(nameof(KakaoPaymentUri)); }
        }
        private string _kakaoStatusMessage = "결제 결과를 확인하고 있습니다...";
        public string KakaoStatusMessage
        {
            get => _kakaoStatusMessage;
            private set { _kakaoStatusMessage = value; OnPropertyChanged(nameof(KakaoStatusMessage)); }
        }
        public ICommand CloseKakaoPayCommand { get; private set; } = null!;
        public ICommand ResumePaymentCommand { get; private set; } = null!;

        private static Uri? SafePaymentUri(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https") return null;
            return uri.Host.Equals("kakaopay.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".kakaopay.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("kakao.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".kakao.com", StringComparison.OrdinalIgnoreCase) ? uri : null;
        }

        public async System.Threading.Tasks.Task RecoverPendingPaymentAsync()
        {
            if (_recoveryRunning || IsSavingOrder || _kakaoOrderId != null || _completedPaymentId != null) return;
            _recoveryRunning = true;
            IsRecoveryCheckFailed = false;
            IsSavingOrder = true;
            IsPaymentRecoveryRequired = true;
            try
            {
                if (KakaoPayStatusService == null) throw new InvalidOperationException("결제 확인 서비스가 연결되지 않았습니다.");
                var pending = await KakaoPayStatusService.GetPendingAsync();
                if (pending != null)
                {
                    if (string.IsNullOrWhiteSpace(pending.PaymentId) || pending.Amount is not > 0)
                        throw new InvalidOperationException("이전 결제 정보를 확인할 수 없습니다. 직원을 호출해 주세요.");
                    _kakaoOrderId = pending.PaymentId;
                    _kakaoPaymentAmount = pending.Amount.Value;
                    IsHomeVisible = false;
                    IsPaymentVisible = true;
                    KakaoStatusMessage = "이전 결제 결과를 확인하고 있습니다. 다시 결제하지 말고 잠시 기다려 주세요.";
                    KakaoPaymentUri = pending.Status == "ready" ? SafePaymentUri(pending.PaymentUrl) ?? new Uri("about:blank") : new Uri("about:blank");
                    IsKakaoPayVisible = true;
                }
                IsPaymentRecoveryRequired = false;
            }
            catch (Exception)
            {
                IsRecoveryCheckFailed = true;
            }
            finally
            {
                IsSavingOrder = false;
                _recoveryRunning = false;
                if (_kakaoOrderId != null) StartKakaoPolling();
            }
        }

        private void StartKakaoPolling()
        {
            _kakaoPollCts?.Cancel();
            if (_kakaoOrderId == null) return;
            _kakaoPollCts = new System.Threading.CancellationTokenSource();
            _ = PollKakaoStatusAsync(_kakaoOrderId, _kakaoPollCts.Token);
        }

        private async void CloseKakaoPay(object? parameter) => await CancelCurrentPaymentAsync();

        private async System.Threading.Tasks.Task<bool> CancelCurrentPaymentAsync()
        {
            if (_isKakaoCancelInProgress || IsSavingOrder) return false;
            if (_kakaoOrderId == null) return true;
            if (KakaoPayStatusService == null) return false;
            var paymentId = _kakaoOrderId;
            _isKakaoCancelInProgress = true;
            _kakaoPollCts?.Cancel();
            IsSavingOrder = true;
            KakaoStatusMessage = "이전 결제를 종료하고 있습니다. 잠시 기다려 주세요.";
            try
            {
                var status = (await KakaoPayStatusService.CancelAsync(paymentId)).ToLowerInvariant();
                if (status is "cancelled" or "failed" or "refunded" or "historical")
                {
                    _kakaoOrderId = null;
                    IsKakaoPayVisible = false;
                    KakaoPaymentUri = new Uri("about:blank");
                    return true;
                }
                IsKakaoPayVisible = true;
                KakaoStatusMessage = "승인 결과를 확인하고 있습니다. 확인이 끝날 때까지 기다려 주세요.";
                MessageBox.Show("이미 승인이 진행 중이거나 결제 결과 확인이 필요합니다. 잠시 기다려 주세요.", "결제 확인 중");
                return false;
            }
            catch (Exception)
            {
                IsKakaoPayVisible = true;
                MessageBox.Show("결제 종료를 확인하지 못했습니다. 기존 결제 확인을 계속합니다. 다시 결제하지 말고 잠시 기다려 주세요.", "결제 확인 필요");
                return false;
            }
            finally
            {
                IsSavingOrder = false;
                _isKakaoCancelInProgress = false;
                if (_kakaoOrderId != null) StartKakaoPolling();
            }
        }

        private async System.Threading.Tasks.Task PollKakaoStatusAsync(string paymentId, System.Threading.CancellationToken token)
        {
            bool warned = false;
            int failures = 0;
            var started = DateTime.UtcNow;
            while (!token.IsCancellationRequested && _kakaoOrderId == paymentId)
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay((DateTime.UtcNow - started).TotalSeconds < 120 ? 2000 : 5000, token);
                    if (KakaoPayStatusService == null) return;
                    var result = await KakaoPayStatusService.GetStatusAsync(paymentId, token);
                    if (token.IsCancellationRequested || _kakaoOrderId != paymentId) return;
                    failures = 0;
                    PaymentDiagnostics.Write("status=" + result.Status);
                    if (result.Status == "historical")
                    {
                        // Preserve the already-acknowledged legacy approval without
                        // trying to reconstruct a new order from a missing snapshot.
                        _kakaoOrderId = null;
                        IsKakaoPayVisible = false;
                        KakaoPaymentUri = new Uri("about:blank");
                        await GoHomeAsync();
                        return;
                    }
                    if (result.Status == "approved")
                    {
                        if (result.OrderId == null || result.Amount != _kakaoPaymentAmount)
                            throw new InvalidOperationException("승인 정보가 일치하지 않습니다.");
                        _completedPaymentId = paymentId;
                        _kakaoOrderId = null;
                        IsKakaoPayVisible = false;
                        KakaoPaymentUri = new Uri("about:blank");
                        IsPaymentVisible = false;
                        CompletedPaymentAmount = result.Amount.Value;
                        CompletedOrderId = result.OrderId.Value;
                        SetCompletedLoyalty(result.Amount.Value);
                        IsKakaoPaymentCompleted = true;
                        IsPaymentCompleteVisible = true;
                        int version = ++_kakaoCompletionVersion;
                        _ = ReturnHomeAfterKakaoCompletionAsync(version);
                        return;
                    }
                    if (result.Status is "failed" or "cancelled" or "refunded")
                    {
                        await KakaoPayStatusService.AcknowledgeAsync(paymentId);
                        if (token.IsCancellationRequested || _kakaoOrderId != paymentId) return;
                        _kakaoOrderId = null;
                        IsKakaoPayVisible = false;
                        KakaoPaymentUri = new Uri("about:blank");
                        MessageBox.Show(result.Status == "refunded" ? "결제가 전액 취소되었습니다. 다시 주문할 수 있습니다." : "이전 결제가 종료되었습니다. 다시 결제할 수 있습니다.", "결제 안내");
                        if (CartItems.Count == 0) await GoHomeAsync();
                        return;
                    }
                    KakaoStatusMessage = result.Status is "order_error" or "manual_review" or "refund_pending"
                        ? "결제 결과를 추가 확인하고 있습니다. 다시 결제하지 말고 직원을 호출해 주세요."
                        : "결제 결과를 확인하고 있습니다. 잠시 기다려 주세요.";
                    if (!warned && (result.Status is "order_error" or "manual_review" or "refund_pending" || (DateTime.UtcNow - started).TotalSeconds > 120))
                    {
                        warned = true;
                        MessageBox.Show("결제 확인이 지연되고 있습니다. 자동 확인을 계속하니 다시 결제하지 말고 필요하면 직원을 호출해 주세요.", "결제 확인 중");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception)
                {
                    if (token.IsCancellationRequested || _kakaoOrderId != paymentId) return;
                    failures++;
                    KakaoStatusMessage = "연결 상태를 확인하고 있습니다. 결제 결과 확인을 계속합니다.";
                    if (!warned && failures >= 3)
                    {
                        warned = true;
                        MessageBox.Show("결제 서버 연결이 불안정합니다. 결과 확인을 계속하니 다시 결제하지 말고 잠시 기다려 주세요.", "연결 확인");
                    }
                }
            }
        }

        private async System.Threading.Tasks.Task ReturnHomeAfterKakaoCompletionAsync(int version)
        {
            await System.Threading.Tasks.Task.Delay(10000);
            if (version == _kakaoCompletionVersion && IsKakaoPaymentCompleted) await GoHomeAsync();
        }

        private async System.Threading.Tasks.Task OpenKakaoPayAsync()
        {
            if (IsSavingOrder || _kakaoOrderId != null || _completedPaymentId != null) return;
            if (IsPaymentRecoveryRequired)
            {
                await RecoverPendingPaymentAsync();
                return;
            }
            try
            {
                if (KakaoPayReadyService == null || KakaoPayStatusService == null)
                    throw new InvalidOperationException("결제 서비스가 연결되지 않았습니다.");
                IsSavingOrder = true;
                int displayedAmount = TotalPrice;
                await RefreshMenusAsync();
                if (displayedAmount != TotalPrice) throw new InvalidOperationException("메뉴 가격이 변경되었습니다. 갱신된 장바구니 금액을 확인하고 다시 결제해 주세요.");
                if (!CanCheckout) throw new InvalidOperationException("상품 가격 또는 재고를 확인하고 장바구니를 수정해 주세요.");
                IsKakaoPaymentCompleted = false;
                var prepared = await KakaoPayReadyService.PrepareAsync(OrderType, CartItems.ToArray());
                _kakaoPaymentAmount = prepared.Amount;
                _kakaoOrderId = prepared.OrderId;
                KakaoPaymentUri = prepared.PaymentUri;
                IsKakaoPayVisible = true;
                StartKakaoPolling();
            }
            catch (Exception ex)
            {
                IsPaymentRecoveryRequired = true;
                MessageBox.Show(ex.Message + "\n중복 결제를 방지하기 위해 이전 결제 여부를 확인합니다.", "결제 준비 확인");
            }
            finally { IsSavingOrder = false; }
            if (IsPaymentRecoveryRequired) await RecoverPendingPaymentAsync();
        }


        public Func<System.Threading.Tasks.Task<IReadOnlyList<ProductModel>>>? StockLoaderAsync { get; set; }

        private static bool IsStockError(Exception ex)
        {
             
            if (ex is OrderApiException apiError &&
                apiError.ErrorCode == "INSUFFICIENT_STOCK")
                return true;

             
            string message = ex.Message;
            return message.Contains("재고 부족", StringComparison.OrdinalIgnoreCase)
                || message.Contains("재고가 부족", StringComparison.OrdinalIgnoreCase)
                || message.Contains("판매 불가 상품", StringComparison.OrdinalIgnoreCase)
                || message.Contains("품절", StringComparison.OrdinalIgnoreCase)
                || message.Contains("insufficient stock", StringComparison.OrdinalIgnoreCase);
        }

         
        private async System.Threading.Tasks.Task<string> HandleOrderFailureAsync(Exception ex)
        {
            if (!IsStockError(ex))
                return $"주문 접수 중 오류가 발생했습니다.\n\n{ex.Message}";

            IsPaymentVisible = false;
            SelectedPaymentMethod = "";

            string refreshMessage;
            try
            {
                if (StockLoaderAsync == null)
                    throw new InvalidOperationException("재고 조회 기능이 연결되지 않았습니다.");

                var latest = await StockLoaderAsync();
                RefreshProductStock(latest);
                refreshMessage = "최신 재고 정보를 다시 불러왔습니다.";
            }
            catch (Exception refreshEx)
            {
                refreshMessage = "최신 재고 조회에 실패했습니다. 네트워크 연결을 확인해 주세요. "
                    + refreshEx.Message;
            }

            var exceeded = CartItems
                .GroupBy(x => x.Menu.Id)
                .Select(g => new
                {
                    Name = g.First().Menu.Name,
                    Requested = g.Sum(x => x.Quantity),
                    Available = g.First().Menu.StockQuantity
                })
                .Where(x => x.Requested > x.Available)
                .Select(x => $"{x.Name}: 장바구니 {x.Requested}개 / 재고 {x.Available}개")
                .ToList();

            string details = exceeded.Count > 0
                ? "\n\n수량을 수정해야 하는 상품:\n" + string.Join("\n", exceeded)
                : "\n\n장바구니 수량을 확인한 뒤 다시 주문해 주세요.";

            return "다른 주문으로 인해 재고가 부족하여 접수되지 않았습니다.\n"
                + refreshMessage + details;
        }


        private bool isSavingOrder;

        public bool IsSavingOrder
        {
            get => isSavingOrder;
            set
            {
                if (isSavingOrder == value)
                    return;

                isSavingOrder = value;

                OnPropertyChanged(nameof(IsSavingOrder));
                OnPropertyChanged(nameof(CanUseLoyalty));
                OnPropertyChanged(nameof(CanConfirmPayment));
                OnPropertyChanged(nameof(CanStartOrder));
            }
        }

         
         
         

        private string _idleWarning = "";
        public string IdleWarning
        {
            get => _idleWarning;
            set { _idleWarning = value; OnPropertyChanged(nameof(IdleWarning)); }
        }

        private string title = "식사 방법을 선택해주세요";

        public string Title
        {
            get => title;
            set
            {
                title = value;
                OnPropertyChanged(nameof(Title));
            }
        }

        private bool isHomeVisible = true;

        public bool IsHomeVisible
        {
            get => isHomeVisible;
            set
            {
                isHomeVisible = value;

                OnPropertyChanged(nameof(IsHomeVisible));
                OnPropertyChanged(nameof(IsOrderVisible));
            }
        }

        public bool IsOrderVisible => !IsHomeVisible;

        public bool CanResetIdleOrder => !IsHomeVisible && !IsSavingOrder && !IsPaymentVisible &&
            !IsPaymentCompleteVisible && !IsKakaoPayVisible && !IsPaymentRecoveryRequired && !IsStaffCallVisible;

        private string orderType = "";

        public string OrderType
        {
            get => orderType;
            set
            {
                orderType = value;
                OnPropertyChanged(nameof(OrderType));
            }
        }

         
         
         

        private string selectedCategory = "추천메뉴";

        public string SelectedCategory
        {
            get => selectedCategory;
            set
            {
                selectedCategory = value;

                OnPropertyChanged(nameof(SelectedCategory));
                UpdateFilteredMenus();
            }
        }

        public ObservableCollection<MenuItemModel> AllMenus
        { get; } = new();

        public ObservableCollection<MenuItemModel> FilteredMenus
        { get; } = new();

        public ObservableCollection<CartItemModel> CartItems
        { get; } = new();

        public int TotalPrice =>
            CartItems.Sum(item => item.TotalPrice);

        public int TotalItemCount =>
            CartItems.Sum(item => item.Quantity);

        public bool CanCheckout => CartItems.Count > 0 && CartStock.CanFulfill(CartItems, AllMenus);

         
         
         

        public ICommand SelectOrderTypeCommand { get; }
        public ICommand GoHomeCommand { get; }

        public ICommand SelectCategoryCommand { get; }

        public ICommand IncreaseQuantityCommand { get; }
        public ICommand DecreaseQuantityCommand { get; }
        public ICommand RemoveCartItemCommand { get; }

        public ICommand OpenMenuCommand { get; }

        public ICommand SelectSingleCommand { get; }
        public ICommand SelectSetCommand { get; }
        public ICommand ConfirmSetCommand { get; }
        public ICommand CancelOptionCommand { get; }

        public ICommand SelectSideCommand { get; }
        public ICommand SelectDrinkCommand { get; }

        public ICommand CallStaffCommand { get; }
        public ICommand CloseStaffCallCommand { get; }

        public ICommand CheckoutCommand { get; }
        public ICommand CompletePaymentCommand { get; }

        public ICommand SelectPaymentMethodCommand { get; }
        public ICommand ConfirmPaymentCommand { get; }
        public ICommand BackToOrderCommand { get; }

         
         
         

        public MainViewModel(
            IReadOnlyList<ProductModel> products,
            OrderService orderService)
        {
             
            _orderService = orderService;
            CloseKakaoPayCommand = new RelayCommand(CloseKakaoPay);
            ResumePaymentCommand = new RelayCommand(async _ => await RecoverPendingPaymentAsync());

            SelectOrderTypeCommand =
                new RelayCommand(SelectOrderType);

            GoHomeCommand =
                new RelayCommand(GoHome);

            SelectCategoryCommand =
                new RelayCommand(SelectCategory);

            IncreaseQuantityCommand =
                new RelayCommand(IncreaseQuantity);

            DecreaseQuantityCommand =
                new RelayCommand(DecreaseQuantity);

            RemoveCartItemCommand =
                new RelayCommand(RemoveCartItem);

            OpenMenuCommand =
                new RelayCommand(OpenMenu);

            SelectSingleCommand =
                new RelayCommand(SelectSingle);

            SelectSetCommand =
                new RelayCommand(SelectSet);

            ConfirmSetCommand =
                new RelayCommand(ConfirmSet);

            CancelOptionCommand =
                new RelayCommand(CancelOption);

            SelectSideCommand =
                new RelayCommand(SelectSide);

            SelectDrinkCommand =
                new RelayCommand(SelectDrink);

            CallStaffCommand =
                new RelayCommand(CallStaff);

            CloseStaffCallCommand =
                new RelayCommand(CloseStaffCall);

            CheckoutCommand =
                new RelayCommand(Checkout);

            CompletePaymentCommand =
                new RelayCommand(CompletePayment);

            SelectPaymentMethodCommand =
                new RelayCommand(SelectPaymentMethod);

            ConfirmPaymentCommand =
                new RelayCommand(ConfirmPayment);

            BackToOrderCommand =
                new RelayCommand(BackToOrder);

             
            LoadMenus(products);

             
            UpdateFilteredMenus();
        }

         
         
         

        private void LoadMenus(
            IReadOnlyList<ProductModel> products)
        {
            AllMenus.Clear();

            foreach (var product in products)
            {
                if (!product.IsAvailable)
                    continue;

                AllMenus.Add(new MenuItemModel
                {
                    Id = product.Id,
                    BurgerProductId = product.BurgerProductId,
                    SetOptionKey = product.SetOptionKey,
                    Name = product.Name,
                    Price = product.Price,
                    Category = product.Category,
                    IsRecommended = product.IsRecommended,
                    ImagePath = ResolveImagePath(product),
                    StockQuantity = product.StockQuantity
                });
            }
            RefreshSetStock();
        }

         
         
        public async System.Threading.Tasks.Task RefreshMenusAsync()
        {
            if (StockLoaderAsync != null) RefreshProductStock(await StockLoaderAsync());
        }

        public void RefreshProductStock(IReadOnlyList<ProductModel> latestProducts)
        {
            if (_kakaoOrderId != null || IsKakaoPaymentCompleted) return;
            var byId = latestProducts.ToDictionary(p => p.Id);
            var renamed = new System.Collections.Generic.HashSet<long>();
            foreach (var menu in AllMenus.ToArray())
            {
                if (!byId.TryGetValue(menu.Id, out var product) || !product.IsAvailable)
                {
                    menu.StockQuantity = 0;
                    AllMenus.Remove(menu);
                    continue;
                }
                if (menu.Name != product.Name || menu.Category != product.Category) renamed.Add(menu.Id);
                menu.BurgerProductId = product.BurgerProductId;
                menu.SetOptionKey = product.SetOptionKey;
                menu.Name = product.Name;
                menu.Price = product.Price;
                menu.Category = product.Category;
                menu.IsRecommended = product.IsRecommended;
                menu.ImagePath = ResolveImagePath(product);
                menu.StockQuantity = Math.Max(0, product.StockQuantity);
            }
            foreach (var product in latestProducts.Where(p => p.IsAvailable && !AllMenus.Any(m => m.Id == p.Id)))
                AllMenus.Add(new MenuItemModel { Id = product.Id, BurgerProductId = product.BurgerProductId, SetOptionKey = product.SetOptionKey, Name = product.Name, Price = product.Price,
                    Category = product.Category, IsRecommended = product.IsRecommended,
                    ImagePath = ResolveImagePath(product), StockQuantity = Math.Max(0, product.StockQuantity) });
            for (int i = 0; i < CartItems.Count; i++)
            {
                var old = CartItems[i];
                var menu = AllMenus.FirstOrDefault(m => m.Id == old.Menu.Id);
                if (menu == null) continue;
                int price = menu.Price + (old.OptionType == "세트" && old.Side == "치즈스틱" ? 500 : 0);
                if (old.UnitPrice == price && ReferenceEquals(menu, old.Menu) && !renamed.Contains(menu.Id)) continue;
                old.PropertyChanged -= CartItem_PropertyChanged;
                var replacement = new CartItemModel(menu, old.OptionType, old.Side, old.Drink, price) { Quantity = old.Quantity };
                replacement.PropertyChanged += CartItem_PropertyChanged;
                CartItems[i] = replacement;
            }
            RefreshSetStock();
            UpdateFilteredMenus();
            UpdateTotalPrice();
            OnPropertyChanged(nameof(SinglePrice));
            OnPropertyChanged(nameof(SetPrice));
            OnPropertyChanged(nameof(FinalSetPrice));
            OnPropertyChanged(nameof(LoyaltyExpectedText));
            OnPropertyChanged(nameof(CanCheckout));
        }

        private static string ResolveImagePath(
            ProductModel product)
        {
            if (!string.IsNullOrWhiteSpace(product.ImageUrl))
                return product.ImageUrl;

            string name = product.Name;

            if (name.Contains("불고기"))
                return "/Images/bulgogi.png";

            if (name.Contains("치즈버거"))
                return "/Images/cheese.png";

            if (name.Contains("새우"))
                return "/Images/shrimp.png";

            if (name.Contains("더블"))
                return "/Images/double.png";

            if (name.Contains("치킨"))
                return "/Images/double.png";

            if (name.Contains("감자"))
                return "/Images/fries.png";

            if (name.Contains("치즈스틱"))
                return "/Images/cheesestick.png";

            if (name.Contains("사이다"))
                return "/Images/cider.png";

            if (name.Contains("콜라"))
                return "/Images/cola.png";

            return "/Images/cola.png";
        }

         
         
         

        private void UpdateFilteredMenus()
        {
            var desired = (SelectedCategory == "추천메뉴"
                ? AllMenus.Where(m => m.IsRecommended)
                : AllMenus.Where(m => m.Category == SelectedCategory)).ToArray();
            for (int i = 0; i < desired.Length; i++)
            {
                if (i < FilteredMenus.Count && ReferenceEquals(FilteredMenus[i], desired[i])) continue;
                int oldIndex = FilteredMenus.IndexOf(desired[i]);
                if (oldIndex >= 0) FilteredMenus.Move(oldIndex, i);
                else FilteredMenus.Insert(i, desired[i]);
            }
            while (FilteredMenus.Count > desired.Length) FilteredMenus.RemoveAt(FilteredMenus.Count - 1);
        }

        private void SelectCategory(object? parameter)
        {
            SelectedCategory =
                parameter?.ToString() ?? "추천메뉴";
        }

         
         
         

        private void SelectOrderType(object? parameter)
        {
            if (IsSavingOrder || _kakaoOrderId != null || _completedPaymentId != null || IsPaymentRecoveryRequired) return;
            OrderType = parameter?.ToString() ?? "";

            SelectedCategory = "추천메뉴";

            IsHomeVisible = false;
        }

         
         
        public bool TrySelectVoiceOrderType(
            string orderType, out string message)
        {
            if (IsSavingOrder || IsPaymentVisible ||
                IsPaymentCompleteVisible || IsOptionVisible ||
                !IsHomeVisible)
            {
                message = "현재는 주문 방식을 변경할 수 없습니다.";
                return false;
            }

            if (orderType != "매장" && orderType != "포장")
            {
                message = "매장 식사 또는 포장 중 선택해 주세요.";
                return false;
            }

             
            SelectOrderTypeCommand.Execute(orderType);
            bool ok = !IsHomeVisible && OrderType == orderType;
            message = ok
                ? (orderType == "매장"
                    ? "매장 식사로 선택했습니다. 메뉴를 골라 주세요."
                    : "포장으로 선택했습니다. 메뉴를 골라 주세요.")
                : "주문 방식 선택에 실패했습니다.";
            return ok;
        }

        private async void GoHome(object? parameter) => await GoHomeAsync();

        private async System.Threading.Tasks.Task GoHomeAsync()
        {
            if (IsSavingOrder) return;
            if (_kakaoOrderId != null && !await CancelCurrentPaymentAsync()) return;
            if (_completedPaymentId != null)
            {
                IsSavingOrder = true;
                try
                {
                    if (KakaoPayStatusService == null) throw new InvalidOperationException();
                    await KakaoPayStatusService.AcknowledgeAsync(_completedPaymentId);
                    _completedPaymentId = null;
                }
                catch (Exception)
                {
                    MessageBox.Show("완료 확인을 서버에 저장하지 못했습니다. 확인 버튼을 눌러 다시 시도해 주세요.", "연결 확인");
                    return;
                }
                finally { IsSavingOrder = false; }
            }
            _kakaoPollCts?.Cancel();
            _kakaoOrderId = null;
            ++_kakaoCompletionVersion;
            IsKakaoPaymentCompleted = false;
            IsKakaoPayVisible = false;
            KakaoPaymentUri = new Uri("about:blank");

            foreach (var item in CartItems)
            {
                item.PropertyChanged -=
                    CartItem_PropertyChanged;
            }

            ResetLoyalty();
            CartItems.Clear();

            UpdateTotalPrice();

            IsPaymentRecoveryRequired = true;
            IsHomeVisible = true;

            IsPaymentVisible = false;
            IsPaymentCompleteVisible = false;

            SelectedPaymentMethod = "";

            OrderType = "";

            CloseOptions();

            IsStaffCallVisible = false;
        
            try { await RefreshMenusAsync(); }
            catch (Exception) { MessageBox.Show("메뉴 갱신에 실패했습니다. 결제 전에 다시 확인합니다.", "연결 확인"); }
            await RecoverPendingPaymentAsync();
        }

         
         
         

         
        private int CurrentCartQuantity(long productId) =>
            CartItems.Where(x => x.Menu.Id == productId)
                     .Sum(x => x.Quantity);

        private bool HasEnoughStock(MenuItemModel menu, int count) => CartStock.CanAdd(CartItems, AllMenus, menu, count);

        private void RefreshSetStock()
        {
            foreach (var set in AllMenus.Where(x => x.Category == "세트"))
                set.StockQuantity = CartStock.AvailableSets(set, AllMenus);
        }

        private bool AddCartItem(
            MenuItemModel menu,
            string optionType,
            string side,
            string drink,
            int price,
            int count = 1)
        {
            if (IsSavingOrder || count < 1 || count > 20 || !CartStock.CanAdd(CartItems, AllMenus, menu, count, side, drink))
            {
                MessageBox.Show($"{menu.Name}의 남은 재고는 {menu.StockQuantity}개입니다.\n현재 장바구니 수량을 확인해 주세요.",
                    "재고 부족", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var existingItem = CartItems.FirstOrDefault(item =>
                item.HasSameOptions(menu, optionType, side, drink, price));
            if ((existingItem?.Quantity ?? 0) + count > 20)
            {
                MessageBox.Show("같은 상품과 옵션은 최대 20개까지 주문할 수 있습니다.", "수량 안내");
                return false;
            }
            if (existingItem != null)
                existingItem.Quantity += count;
            else
            {
                var cartItem = new CartItemModel(menu, optionType, side, drink, price) { Quantity = count };
                cartItem.PropertyChanged += CartItem_PropertyChanged;
                CartItems.Add(cartItem);
            }
            UpdateTotalPrice();
            return true;
        }

         
         
         
         
        public bool TryAddVoiceCartItem(
            long productId,
            int quantity,
            out string message)
        {
            if (IsSavingOrder || IsPaymentVisible ||
                IsPaymentCompleteVisible || IsHomeVisible)
            {
                message = "현재 주문을 추가할 수 없는 화면입니다.";
                return false;
            }

            if (quantity < 1 || quantity > 20)
            {
                message = "수량은 1개부터 20개까지 선택할 수 있습니다.";
                return false;
            }

            MenuItemModel? menu = AllMenus.FirstOrDefault(
                m => m.Id == productId);

            if (menu == null)
            {
                message = "판매 중인 상품을 찾을 수 없습니다.";
                return false;
            }

            if (!HasEnoughStock(menu, quantity))
            {
                message = $"{menu.Name}의 주문 가능 재고는 {Math.Max(0, menu.StockQuantity - CurrentCartQuantity(menu.Id))}개입니다.";
                return false;
            }

            if (IsOptionVisible)
            {
                message = "먼저 열려 있는 상품 옵션 선택을 완료해 주세요.";
                return false;
            }

            if (menu.Category == "세트")
            {
                 
                 
                OpenMenu(menu);
                message = "세트의 사이드와 음료를 화면에서 선택해 주세요. 아직 장바구니에 추가되지 않았습니다.";
                return false;
            }

            string optionType = menu.Category == "버거"
                ? "단품"
                : "";

             
             
            if (!AddCartItem(menu, optionType, "", "", menu.Price, quantity))
            {
                message = "상품별 최대 수량 또는 재고를 확인해 주세요.";
                return false;
            }

            CartItemModel? added = CartItems.FirstOrDefault(item =>
                item.HasSameOptions(
                    menu, optionType, "", "", menu.Price));

            if (added == null)
            {
                message = "장바구니 반영에 실패했습니다.";
                return false;
            }



            message = $"{menu.Name} {quantity}개를 장바구니에 담았습니다.";
            return true;
        }

         
        public bool TryChangeVoiceCartQuantity(
            long productId, string action, int quantity, out string message)
        {
            if (IsHomeVisible || IsSavingOrder || IsPaymentVisible ||
                IsPaymentCompleteVisible || IsOptionVisible)
            {
                message = "현재는 장바구니 수량을 변경할 수 없습니다.";
                return false;
            }

            if (action != "increase" && action != "decrease")
            {
                message = "수량 변경 방법이 올바르지 않습니다.";
                return false;
            }

            if (quantity < 1 || quantity > 20)
            {
                message = "변경 수량은 1개부터 20개까지 가능합니다.";
                return false;
            }

            var matches = CartItems.Where(x => x.Menu.Id == productId).ToList();
            if (matches.Count == 0)
            {
                message = "장바구니에 해당 상품이 없습니다.";
                return false;
            }

             
            if (matches.Count > 1)
            {
                message = "같은 상품의 옵션이 여러 개 있습니다. 화면에서 변경할 항목을 선택해 주세요.";
                return false;
            }

            var item = matches[0];
            if (action == "increase")
            {
                if (!CartStock.CanAdd(CartItems, AllMenus, item.Menu, quantity, item.Side, item.Drink))
                {
                    message = $"{item.Menu.Name}의 남은 주문 가능 재고가 부족합니다.";
                    return false;
                }
                if (item.Quantity + quantity > 20)
                {
                    message = "한 항목은 최대 20개까지 담을 수 있습니다.";
                    return false;
                }
                item.Quantity += quantity;
            }
            else
            {
                 
                if (item.Quantity - quantity < 1)
                {
                    message = "수량을 줄이면 0개가 됩니다. 상품을 없애려면 삭제해 달라고 말씀해 주세요.";
                    return false;
                }
                item.Quantity -= quantity;
            }

            UpdateTotalPrice();
            message = $"{item.Name} 수량을 {item.Quantity}개로 변경했습니다.";
            return true;
        }

         
        public bool TryRemoveVoiceCartItem(long productId, out string message)
        {
            if (IsHomeVisible || IsSavingOrder || IsPaymentVisible ||
                IsPaymentCompleteVisible || IsOptionVisible)
            {
                message = "현재는 장바구니에서 상품을 삭제할 수 없습니다.";
                return false;
            }

            var matches = CartItems.Where(x => x.Menu.Id == productId).ToList();
            if (matches.Count == 0)
            {
                message = "장바구니에 해당 상품이 없습니다.";
                return false;
            }
            if (matches.Count > 1)
            {
                message = "같은 상품의 옵션이 여러 개 있습니다. 화면에서 삭제할 항목을 선택해 주세요.";
                return false;
            }

            var item = matches[0];
            string name = item.Name;
            RemoveCartItem(item);  
            message = $"{name}을(를) 장바구니에서 삭제했습니다.";
            return true;
        }

        private void IncreaseQuantity(object? parameter)
        {
            if (IsSavingOrder)
                return;

            if (parameter is CartItemModel item)
            {
                if (item.Quantity >= 20 || !CartStock.CanAdd(CartItems, AllMenus, item.Menu, 1, item.Side, item.Drink))
                {
                    MessageBox.Show("상품별 최대 수량은 20개입니다. 남은 재고도 확인해 주세요.", "수량 안내");
                    return;
                }
                item.Quantity++;

                UpdateTotalPrice();
            }
        }

        private void DecreaseQuantity(object? parameter)
        {
            if (IsSavingOrder)
                return;

            if (parameter is CartItemModel item &&
                item.Quantity > 1)
            {
                item.Quantity--;

                UpdateTotalPrice();
            }
        }

        private void RemoveCartItem(object? parameter)
        {
            if (IsSavingOrder)
                return;

            if (parameter is CartItemModel item)
            {
                item.PropertyChanged -=
                    CartItem_PropertyChanged;

                CartItems.Remove(item);

                UpdateTotalPrice();
            }
        }

        private void CartItem_PropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName ==
                nameof(CartItemModel.TotalPrice))
            {
                UpdateTotalPrice();
            }
        }

        private void UpdateTotalPrice()
        {
            OnPropertyChanged(nameof(TotalPrice));
            OnPropertyChanged(nameof(TotalItemCount));
            OnPropertyChanged(nameof(CanCheckout));
            OnPropertyChanged(nameof(CanConfirmPayment));
        }

         
         
         

        private bool isOptionVisible;

        public bool IsOptionVisible
        {
            get => isOptionVisible;
            set
            {
                isOptionVisible = value;
                OnPropertyChanged(nameof(IsOptionVisible));
            }
        }

        private MenuItemModel? selectedMenu;

        public MenuItemModel? SelectedMenu
        {
            get => selectedMenu;
            set
            {
                selectedMenu = value;

                OnPropertyChanged(nameof(SelectedMenu));
                OnPropertyChanged(nameof(SelectedMenuName));
                OnPropertyChanged(nameof(SinglePrice));
                OnPropertyChanged(nameof(SetPrice));
                OnPropertyChanged(nameof(FinalSetPrice));
            }
        }

        public string SelectedMenuName =>
            SelectedMenu?.Name ?? "";

        private bool isChoosingSetOptions;

        public bool IsChoosingSetOptions
        {
            get => isChoosingSetOptions;
            set
            {
                isChoosingSetOptions = value;

                OnPropertyChanged(nameof(IsChoosingSetOptions));
                OnPropertyChanged(nameof(IsChoosingBurgerType));
            }
        }

        public bool IsChoosingBurgerType =>
            !IsChoosingSetOptions;

         
         
         

        private string selectedSide = "감자튀김";

        public string SelectedSide
        {
            get => selectedSide;
            set
            {
                if (selectedSide == value)
                    return;

                selectedSide = value;

                OnPropertyChanged(nameof(SelectedSide));
                OnPropertyChanged(nameof(OptionExtraPrice));
                OnPropertyChanged(nameof(FinalSetPrice));
            }
        }

        private string selectedDrink = "콜라";

        public string SelectedDrink
        {
            get => selectedDrink;
            set
            {
                if (selectedDrink == value)
                    return;

                selectedDrink = value;

                OnPropertyChanged(nameof(SelectedDrink));
                OnPropertyChanged(nameof(OptionExtraPrice));
                OnPropertyChanged(nameof(FinalSetPrice));
            }
        }

         
         
         

        public int SinglePrice =>
            SelectedMenu?.Price ?? 0;

        public int SetPrice
        {
            get
            {
                if (SelectedMenu == null)
                    return 0;

                 
                if (SelectedMenu.Category == "세트")
                    return SelectedMenu.Price;

                 
                string setName =
                    SelectedMenu.Name + " 세트";

                var setMenu = AllMenus.FirstOrDefault(m =>
                    m.Category == "세트" &&
                    m.BurgerProductId == SelectedMenu.Id);

                return setMenu?.Price ?? 0;
            }
        }

        public int OptionExtraPrice
        {
            get
            {
                int extraPrice = 0;

                if (SelectedSide == "치즈스틱")
                {
                    extraPrice += 500;
                }

                return extraPrice;
            }
        }

        public int FinalSetPrice =>
            SetPrice + OptionExtraPrice;

         
         
         

        private void OpenMenu(object? parameter)
        {
            if (IsSavingOrder)
                return;

            if (parameter is not MenuItemModel menu)
                return;

            if (!HasEnoughStock(menu, 1))
            {
                MessageBox.Show($"{menu.Name}은(는) 품절이거나 재고가 부족합니다.", "품절 안내");
                return;
            }

             
            if (menu.Category != "버거" &&
                menu.Category != "세트")
            {
                AddCartItem(
                    menu,
                    "",
                    "",
                    "",
                    menu.Price);

                return;
            }

             
            if (menu.Category == "세트")
            {
                string burgerName =
                    menu.Name.EndsWith(" 세트")
                        ? menu.Name[..^3]
                        : menu.Name;

                SelectedMenu = AllMenus.FirstOrDefault(
                    m => m.Category == "버거" &&
                         m.Id == menu.BurgerProductId
                ) ?? menu;

                SelectedSide = "감자튀김";
                SelectedDrink = "콜라";

                IsChoosingSetOptions = true;
                IsOptionVisible = true;

                return;
            }

             
            SelectedMenu = menu;

            IsChoosingSetOptions = false;

            IsOptionVisible = true;
        }

         
        private void SelectSingle(object? parameter)
        {
            if (SelectedMenu == null)
                return;

            if (AddCartItem(
                SelectedMenu,
                "단품",
                "",
                "",
                SelectedMenu.Price))
                CloseOptions();
        }

         
        private void SelectSet(object? parameter)
        {
            if (SelectedMenu == null)
                return;

            SelectedSide = "감자튀김";
            SelectedDrink = "콜라";

            IsChoosingSetOptions = true;
        }

         
        private void SelectSide(object? parameter)
        {
            SelectedSide =
                parameter?.ToString() ?? "감자튀김";
        }

         
        private void SelectDrink(object? parameter)
        {
            SelectedDrink =
                parameter?.ToString() ?? "콜라";
        }

         
        private void ConfirmSet(object? parameter)
        {
            if (!TryConfirmVoiceSet(out string result))
                MessageBox.Show(result, "세트 주문 안내");
        }

        private void CancelOption(object? parameter)
        {
            CloseOptions();
        }

        private void CloseOptions()
        {
            _voiceSetQuantity = 1;
            IsOptionVisible = false;
            IsChoosingSetOptions = false;
            SelectedMenu = null;
        }

         
         
         

        private bool isStaffCallVisible;

        public bool IsStaffCallVisible
        {
            get => isStaffCallVisible;
            set
            {
                isStaffCallVisible = value;
                OnPropertyChanged(nameof(IsStaffCallVisible));
            }
        }

        public string StaffCallMessage => "직원을 호출했습니다. 잠시만 기다려 주세요.";
        private void CallStaff(object? parameter)
        {
            IsStaffCallVisible = true;
        }

        private void CloseStaffCall(object? parameter)
        {
            IsStaffCallVisible = false;
        }

         
         
         

        private bool isPaymentVisible;

        public bool IsPaymentVisible
        {
            get => isPaymentVisible;
            set
            {
                if (isPaymentVisible == value)
                    return;

                isPaymentVisible = value;
                OnPropertyChanged(nameof(IsPaymentVisible));
            }
        }

        private string selectedPaymentMethod = "";

        public string SelectedPaymentMethod
        {
            get => selectedPaymentMethod;
            set
            {
                if (selectedPaymentMethod == value)
                    return;

                selectedPaymentMethod = value;

                OnPropertyChanged(nameof(SelectedPaymentMethod));
                OnPropertyChanged(nameof(CanConfirmPayment));
            }
        }

         
        public bool CanConfirmPayment =>
            CanCheckout &&
            !IsSavingOrder && !IsPaymentRecoveryRequired && _kakaoOrderId == null &&
            !string.IsNullOrEmpty(SelectedPaymentMethod);

        private void SelectPaymentMethod(object? parameter)
        {
            if (IsSavingOrder)
                return;

            string method =
                parameter?.ToString() ?? "";

            if (method == "신용/체크카드" || method == "간편결제")
            {
                SelectedPaymentMethod = method;
            }
        }

         
        private async void BackToOrder(object? parameter)
        {
            if (IsSavingOrder) return;
            if (_kakaoOrderId != null && !await CancelCurrentPaymentAsync()) return;

            IsPaymentVisible = false;

            SelectedPaymentMethod = "";
        }

         
         
         

        private void ConfirmPayment(object? parameter) => BeginLoyaltyPrompt();

        private async System.Threading.Tasks.Task ProcessPaymentAsync()
        {
            System.Diagnostics.Debug.WriteLine(
                $"[KakaoPay] 결제 버튼 클릭 / 화면={IsPaymentVisible}, 결제가능={CanConfirmPayment}, 결제수단='{SelectedPaymentMethod}', 주문중={IsSavingOrder}, 장바구니={CartItems.Count}");

            if (!IsPaymentVisible || !CanConfirmPayment)
            {
                MessageBox.Show(
                    $"결제 요청이 중단되었습니다.\n결제화면: {IsPaymentVisible}\n장바구니: {CartItems.Count}건\n결제수단: '{SelectedPaymentMethod}'\n처리 중: {IsSavingOrder}",
                    "결제 상태 확인", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

             
            if (SelectedPaymentMethod == "간편결제")
            {
                await OpenKakaoPayAsync();
                return;
            }

            await CompleteCardDemoAsync();
        }

        public Func<string, IReadOnlyList<CartItemModel>, System.Threading.Tasks.Task<long>>? DemoOrderWriterAsync { get; set; }
        private async System.Threading.Tasks.Task CompleteCardDemoAsync()
        {
            if (!IsPaymentVisible || !CanConfirmPayment) return;
            IsSavingOrder = true;
            try
            {
                int amount = TotalPrice;
                var items = CartItems.ToArray();
                long id = DemoOrderWriterAsync != null
                    ? await DemoOrderWriterAsync(OrderType, items)
                    : await _orderService.CreateOrderAsync(OrderType, items);
                IsKakaoPaymentCompleted = false;
                CompletedOrderId = id;
                SetCompletedLoyalty(amount);
                CompletedPaymentAmount = amount;
                IsPaymentVisible = false;
                IsPaymentCompleteVisible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(await HandleOrderFailureAsync(ex), "주문 접수 안내");
            }
            finally { IsSavingOrder = false; }
        }

        private bool isPaymentCompleteVisible;

        public bool IsPaymentCompleteVisible
        {
            get => isPaymentCompleteVisible;
            set
            {
                if (isPaymentCompleteVisible == value)
                    return;

                isPaymentCompleteVisible = value;

                OnPropertyChanged(
                    nameof(IsPaymentCompleteVisible));
            }
        }

        private int completedPaymentAmount;

        public int CompletedPaymentAmount
        {
            get => completedPaymentAmount;
            set
            {
                completedPaymentAmount = value;

                OnPropertyChanged(
                    nameof(CompletedPaymentAmount));
            }
        }


        private long completedOrderId;

        public long CompletedOrderId
        {
            get => completedOrderId;
            set
            {
                if (completedOrderId == value)
                    return;

                completedOrderId = value;

                OnPropertyChanged(nameof(CompletedOrderId));
            }
        }


         
        private void Checkout(object? parameter)
        {
            if (!CanCheckout || IsSavingOrder || _kakaoOrderId != null || IsPaymentRecoveryRequired) return;

            SelectedPaymentMethod = "";

            IsPaymentVisible = true;
        }

         
        private void CompletePayment(object? parameter)
        {
            if (IsSavingOrder)
                return;

            GoHome(null);
        }

         
        private int _voiceSetQuantity = 1;

        public bool TryOpenVoiceSet(long productId, int quantity, out string message)
        {
            if (IsHomeVisible || IsSavingOrder || IsPaymentVisible ||
                IsPaymentCompleteVisible || IsOptionVisible || quantity < 1 || quantity > 20)
            {
                message = "지금은 세트 옵션을 열 수 없습니다.";
                return false;
            }
            var menu = AllMenus.FirstOrDefault(m => m.Id == productId);
            if (menu == null || (menu.Category != "세트" && menu.Category != "버거"))
            {
                message = "세트로 주문할 수 있는 상품이 아닙니다.";
                return false;
            }
            string setName = menu.Category == "세트" ? menu.Name : menu.Name + " 세트";
            var stockSet = menu.Category == "세트" ? menu : AllMenus.FirstOrDefault(m => m.Category == "세트" && m.BurgerProductId == menu.Id);
            if (stockSet == null)
            {
                message = "해당 상품의 세트 메뉴가 없습니다.";
                return false;
            }
            if (!HasEnoughStock(stockSet, quantity))
            {
                message = $"{setName}의 주문 가능 재고가 부족합니다.";
                return false;
            }
            OpenMenu(stockSet);
            if (!IsOptionVisible || !IsChoosingSetOptions)
            {
                message = "세트 옵션창을 열지 못했습니다.";
                return false;
            }
            setName = stockSet.Name;
            _voiceSetQuantity = quantity;
            message = $"{setName} {quantity}개 옵션창을 열었습니다. 사이드는 감자튀김 또는 치즈스틱, 음료는 콜라 또는 사이다 중 선택해 주세요.";
            return true;
        }

        public bool TryChooseVoiceSetOptions(string side, string drink, out string message)
        {
            if (!IsOptionVisible || !IsChoosingSetOptions || SelectedMenu == null)
            {
                message = "먼저 세트 메뉴를 선택해 주세요.";
                return false;
            }
            if (side != "감자튀김" && side != "치즈스틱")
            {
                message = "사이드는 감자튀김 또는 치즈스틱 중 선택해 주세요.";
                return false;
            }
            if (drink != "콜라" && drink != "사이다")
            {
                message = "음료는 콜라 또는 사이다 중 선택해 주세요.";
                return false;
            }
            SelectedSide = side;
            SelectedDrink = drink;
            message = $"사이드 {side}, 음료 {drink}로 선택했습니다. 세트를 담을까요?";
            return true;
        }

        public bool TryConfirmVoiceSet(out string message)
        {
            if (!IsOptionVisible || !IsChoosingSetOptions || SelectedMenu == null || IsSavingOrder)
            {
                message = "확정할 세트 옵션이 없습니다.";
                return false;
            }
            string setName = SelectedMenu.Category == "세트" ? SelectedMenu.Name : SelectedMenu.Name + " 세트";
            var setMenu = SelectedMenu.Category == "세트" ? SelectedMenu : AllMenus.FirstOrDefault(m => m.Category == "세트" && m.BurgerProductId == SelectedMenu.Id);
            if (setMenu == null)
            {
                message = "해당 세트 상품을 찾을 수 없습니다.";
                return false;
            }
            setName = setMenu.Name;
            int count = _voiceSetQuantity;
            if (!HasEnoughStock(setMenu, count))
            {
                message = $"{setName}의 재고가 부족합니다. 장바구니에 추가하지 않았습니다.";
                return false;
            }
            string side = SelectedSide, drink = SelectedDrink;
            int price = setMenu.Price + OptionExtraPrice;
            if (!AddCartItem(setMenu, "세트", side, drink, price, count))
            {
                message = "상품별 최대 수량 또는 재고를 확인해 주세요. 세트를 추가하지 않았습니다.";
                return false;
            }
            var matched = CartItems.FirstOrDefault(x => x.HasSameOptions(setMenu, "세트", side, drink, price));
            if (matched == null)
            {
                message = "세트를 장바구니에 추가하지 못했습니다.";
                return false;
            }
            CloseOptions();
            _voiceSetQuantity = 1;
            message = $"{setName} {count}개, {side}와 {drink} 옵션을 장바구니에 담았습니다. 현재 총금액은 {TotalPrice:N0}원입니다.";
            return true;
        }

        public bool TryStartVoiceCheckout(out string message)
        {
            if (IsHomeVisible || IsOptionVisible || IsStaffCallVisible ||
                IsPaymentCompleteVisible || IsSavingOrder || IsPaymentVisible || !CanCheckout)
            {
                message = "주문 내역이 없거나 현재 결제로 이동할 수 없습니다.";
                return false;
            }
            Checkout(null);
            string items = string.Join(", ", CartItems.Select(x =>
                $"{x.Name} {x.Quantity}개" +
                (x.OptionType == "세트" ? $"(사이드 {x.Side}, 음료 {x.Drink})" : "")));
            message = $"주문 내역은 {items}입니다. 총금액은 {TotalPrice:N0}원입니다. 신용/체크카드와 간편결제 중 어떤 방식으로 진행할까요?";
            return true;
        }

        public bool TrySelectVoicePaymentMethod(string method, out string message)
        {
            if (!IsPaymentVisible || IsSavingOrder || IsPaymentCompleteVisible)
            {
                message = "먼저 결제 안내 화면으로 이동해 주세요.";
                return false;
            }
            if (method != "신용/체크카드" && method != "간편결제")
            {
                message = "신용/체크카드 또는 간편결제를 선택해 주세요.";
                return false;
            }
            SelectPaymentMethod(method);

            message = method == "간편결제"
                ? "간편결제를 선택했습니다. 결제하기를 누르면 장바구니 금액으로 카카오페이 결제를 진행합니다. 승인 후 주문이 접수됩니다."
                : $"{method} 방식을 선택했습니다. 실제 카드 승인이 아닌 데모 주문 접수입니다. 총 {TotalPrice:N0}원 주문을 접수하려면 '최종 접수해 줘'라고 말씀해 주세요.";
            return true;
        }

        public System.Threading.Tasks.Task<(bool Success, string Message)> TryConfirmVoicePaymentAsync()
        {
            bool opened = BeginLoyaltyPrompt();
            return System.Threading.Tasks.Task.FromResult((opened, opened ? "화면에서 포인트 적립 여부를 선택해 주세요. 아직 결제되지 않았습니다." : "현재 결제를 시작할 수 없습니다. 화면의 안내를 확인해 주세요."));
        }

        public bool TryArmVoiceFinalConfirmation(out string message)
        {
            if (!IsPaymentVisible || !CanConfirmPayment || IsSavingOrder || IsPaymentCompleteVisible)
            {
                message = "결제 방식과 주문 내역을 먼저 확인해 주세요.";
                return false;
            }
            message = "최종 주문 접수 요청을 확인했습니다.";
            return true;
        }
    }
}
