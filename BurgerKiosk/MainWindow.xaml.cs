using BurgerKiosk.Models;
using BurgerKiosk.Services;
using BurgerKiosk.ViewModels;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;

namespace BurgerKiosk
{
    public partial class MainWindow : Window
    {
        private readonly System.Windows.Threading.DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private DateTime _lastInteractionUtc = DateTime.UtcNow;
        private readonly AdminAuthService _adminAuthService;
        private int _adminTapCount;
        private DateTime _firstAdminTapUtc;
        private bool _adminDialogOpen;
        private readonly LiveTokenService _tokenService;
        private readonly GeminiLiveService _liveService;
        private readonly MicrophoneService _microphoneService;

         
        private readonly Dictionary<DependencyObject, double> _originalFontSizes = new();
        private double _fontScale = 1.0;

        private bool _isPressed;
        private bool _isStarting;
        private bool _isEnding;
        private bool _isClosing;
        private DateTime _lastVoiceErrorUtc;
        private bool _newVoiceSessionRequired;
        private Task? _voicePreparationTask;
        private bool _isPreparingVoice;
        private DateTime _nextVoicePreparationUtc;
        private int _voicePreparationFailures;
        private bool _speechStarted;
        private int _completedSpeechTurns;
        private int _paymentMethodSpeechTurn = -1;
         
        private string? _paymentSelectionSnapshot;

        public MainWindow(
            IReadOnlyList<ProductModel> products,
            OrderService orderService,
            LiveTokenService tokenService,
            GeminiLiveService liveService,
            AdminAuthService adminAuthService)
        {
            InitializeComponent();

            _adminAuthService = adminAuthService;
            _tokenService = tokenService;
            _liveService = liveService;
            _microphoneService = new MicrophoneService();

            _microphoneService.AudioDataAvailable += _liveService.QueueAudio;
            DataContext = new MainViewModel(products, orderService);
            ((MainViewModel)DataContext).PropertyChanged += OnViewModelChanged;

             
            _liveService.AddToCartRequested += OnAddToCartRequested;
            _liveService.OrderTypeRequested += OnOrderTypeRequested;
            _liveService.ChangeQuantityRequested += OnChangeQuantityRequested;
            _liveService.RemoveFromCartRequested += OnRemoveFromCartRequested;
            _liveService.OpenSetRequested += OnOpenSetRequested;
            _liveService.ChooseSetOptionsRequested += OnChooseSetOptionsRequested;
            _liveService.ConfirmSetRequested += OnConfirmSetRequested;
            _liveService.StartCheckoutRequested += OnStartCheckoutRequested;
            _liveService.PaymentMethodRequested += OnPaymentMethodRequested;
            _liveService.FinalOrderRequested += OnFinalOrderRequested;
            _liveService.ErrorOccurred += OnLiveError;
            _liveService.ConnectionChanged += OnConnectionChanged;
            UpdateVoiceStatus("음성 연결 준비 중", "🎤 연결 준비 중...");
            TalkButton.IsEnabled = false;
            Loaded += MainWindow_VoiceLoaded;
            PreviewMouseDown += OnKioskInteraction;
            PreviewTouchDown += OnKioskInteraction;
            PreviewKeyDown += OnKioskInteraction;
            _idleTimer.Tick += OnIdleTick;
            _idleTimer.Start();
            Loaded += MainWindow_AccessibilityLoaded;
            PreviewKeyDown += MainWindow_AccessibilityKeyDown;
        }

         
        private void OnKioskInteraction(object? sender, InputEventArgs e)
        {
            _lastInteractionUtc = DateTime.UtcNow;
            if (DataContext is MainViewModel vm) vm.IdleWarning = "";
        }

        private void OnIdleTick(object? sender, EventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (!_isClosing && !_adminDialogOpen && !_isPressed && !_isStarting && !_isEnding &&
                !_speechStarted && !_isPreparingVoice && DateTime.UtcNow >= _nextVoicePreparationUtc &&
                (!_liveService.IsConnected || _newVoiceSessionRequired || _liveService.NeedsCatalogReconnect))
                _ = PrepareVoiceInBackgroundAsync();
            bool blocked = _isClosing || _adminDialogOpen || _isPressed || _isStarting || _isEnding || _speechStarted ||
                !vm.CanResetIdleOrder;
            if (blocked) { _lastInteractionUtc = DateTime.UtcNow; vm.IdleWarning = ""; return; }
            var seconds = (DateTime.UtcNow - _lastInteractionUtc).TotalSeconds;
            vm.IdleWarning = seconds >= 90 ? $"입력이 없어 {Math.Max(0, 120 - (int)seconds)}초 후 처음 화면으로 돌아갑니다." : "";
            if (seconds >= 120) { _lastInteractionUtc = DateTime.UtcNow; vm.IdleWarning = ""; vm.GoHomeCommand.Execute(null); }
        }

        private void EasyOrderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_adminDialogOpen) return;
            var now = DateTime.UtcNow;
            if (_adminTapCount == 0 || (now - _firstAdminTapUtc).TotalSeconds > 3)
            {
                _firstAdminTapUtc = now;
                _adminTapCount = 0;
            }
            _adminTapCount++;
            if (_adminTapCount < 5) return;
            _adminTapCount = 0;
            _adminDialogOpen = true;
            ShowAdminLogin();
        }

        private void ShowAdminLogin()
        {
            var login = new AdminLoginWindow(_adminAuthService);
            login.LoginSucceeded += (_, _) => ShowAdminDashboard();
            login.CancelRequested += (_, _) => CloseAdminPage();
            AdminContent.Content = login;
            AdminHost.Visibility = Visibility.Visible;
        }

        private void ShowAdminDashboard()
        {
            try
            {
                var inventory = new AdminInventoryService(
                    _adminAuthService.BaseUrl,
                    _adminAuthService.PublishableKey,
                    _adminAuthService);
                var dashboard = new AdminDashboardWindow(
                    inventory,
                    new AdminMenuService(_adminAuthService),
                    new AdminStatisticsService(_adminAuthService),
                    new AdminOrderCancelService(_adminAuthService),
                    new LoyaltyService(_adminAuthService.BaseUrl, _adminAuthService.PublishableKey, () => _adminAuthService.AdminAccessToken));
                dashboard.ExitRequested += (_, _) => CloseAdminPage();
                AdminContent.Content = dashboard;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "관리자 화면 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                CloseAdminPage();
            }
        }

        private void CloseAdminPage()
        {
            AdminContent.Content = null;
            AdminHost.Visibility = Visibility.Collapsed;
            _adminAuthService.SignOut();
            _adminDialogOpen = false;
            _adminTapCount = 0;
        }

         
         
         
        private void MainWindow_AccessibilityLoaded(object sender, RoutedEventArgs e)
        {
            if (Content is Grid root)
                CacheOriginalFontSizes(root);
        }

        private void ZoomButton_Click(object sender, RoutedEventArgs e)
        {
            double next = _fontScale < 1.14 ? 1.15
                        : _fontScale < 1.29 ? 1.30
                        : 1.0;
            SetFontScale(next);
        }

        private void CacheOriginalFontSizes(DependencyObject current)
        {
             
            if (current is TextBlock text &&
                !BindingOperations.IsDataBound(text, TextBlock.FontSizeProperty))
                _originalFontSizes.TryAdd(text, text.FontSize);
            else if (current is Control control &&
                !BindingOperations.IsDataBound(control, Control.FontSizeProperty))
                _originalFontSizes.TryAdd(control, control.FontSize);

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                CacheOriginalFontSizes(VisualTreeHelper.GetChild(current, i));
        }

        private void SetFontScale(double scale)
        {
            _fontScale = Math.Clamp(Math.Round(scale, 2), 1.0, 1.3);

             
            if (Content is Grid root)
                CacheOriginalFontSizes(root);

            foreach (var pair in _originalFontSizes.ToList())
            {
                switch (pair.Key)
                {
                    case TextBlock text:
                        text.FontSize = pair.Value * _fontScale;
                        break;
                    case Control control:
                        control.FontSize = pair.Value * _fontScale;
                        break;
                }
            }

            if (FindName("ZoomButton") is Button zoom)
                zoom.ToolTip = $"글씨 {(int)(_fontScale * 100)}% (누르면 확대)";

//             Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [접근성] 글씨 크기 {(int)(_fontScale * 100)}%");
        }

        private void MainWindow_AccessibilityKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                SetFontScale(_fontScale + 0.1);
                e.Handled = true;
            }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                SetFontScale(_fontScale - 0.1);
                e.Handled = true;
            }
            else if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                SetFontScale(1.0);
                e.Handled = true;
            }
        }

         
         
         
        private async void MainWindow_VoiceLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_VoiceLoaded;
            await PrepareVoiceInBackgroundAsync();
        }

        private Task EnsureVoiceConnectionAsync()
        {
            if (_voicePreparationTask is { IsCompleted: false }) return _voicePreparationTask;
            return _voicePreparationTask = ConnectVoiceAsync();
        }

        private async Task ConnectVoiceAsync()
        {
            if (_newVoiceSessionRequired || _liveService.NeedsCatalogReconnect)
            {
                await _liveService.DisconnectAsync();
                _newVoiceSessionRequired = false;
            }
            if (_isClosing || _liveService.IsConnected) return;
            string token = await _tokenService.GetTokenAsync();
            if (_isClosing) return;
            await _liveService.ConnectAsync(token);
        }

        private async Task PrepareVoiceInBackgroundAsync()
        {
            if (_isClosing || _isPreparingVoice || _isStarting || _speechStarted) return;
            _isPreparingVoice = true;
            TalkButton.IsEnabled = false;
            UpdateVoiceStatus("음성 연결 준비 중", "🎤 연결 준비 중...");
            try
            {
                await EnsureVoiceConnectionAsync();
                if (_isClosing) return;
                _voicePreparationFailures = 0;
                _nextVoicePreparationUtc = DateTime.MinValue;
                UpdateVoiceStatus("준비 완료", "🎤 누르고 말하기");
            }
            catch (Exception)
            {
                _voicePreparationFailures++;
                _nextVoicePreparationUtc = DateTime.UtcNow.AddSeconds(Math.Min(300,
                    30 * Math.Pow(2, Math.Min(_voicePreparationFailures - 1, 4))));
                UpdateVoiceStatus("연결 재시도 대기 · 터치 주문 가능", "🎤 다시 연결하기");
            }
            finally
            {
                _isPreparingVoice = false;
                if (!_isClosing) TalkButton.IsEnabled = true;
            }
        }

        private async void BeginPushToTalk()
        {
            if (_isClosing || _isPressed || _isStarting || _isEnding)
                return;

            _isPressed = true;
            _isStarting = true;
            UpdateVoiceStatus("연결 확인 중", "연결 중...");

            try
            {
                await EnsureVoiceConnectionAsync();

                if (!_isPressed || _isClosing) return;

                await _liveService.StartSpeechAsync();
                _speechStarted = true;

                if (!_isPressed || _isClosing)
                    return;

                _microphoneService.StartRecording();
                UpdateVoiceStatus("듣는 중", "🎙️ 듣는 중... (놓으면 전송)");
//                 Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [마이크] 음성 입력 시작");
            }
            catch (Exception ex)
            {
                _isPressed = false;
                UpdateVoiceStatus("연결 오류", "🎤 다시 시도하기");
                MessageBox.Show(ex.Message, "음성 상담 시작 오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isStarting = false;
                 
                if (!_isPressed && _speechStarted)
                    await FinishSpeechAsync();
            }
        }

         
         
         
        private async void EndPushToTalk()
        {
            _isPressed = false;
            if (_isStarting || _isEnding || !_speechStarted)
                return;
            await FinishSpeechAsync();
        }

        private async Task FinishSpeechAsync()
        {
            if (_isEnding || !_speechStarted) return;
            _isEnding = true;

            try
            {
                if (_microphoneService.IsRecording)
                {
                    _microphoneService.StopRecording();
//                     Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [마이크] 음성 입력 종료");
                }

                await _liveService.EndSpeechAsync();
                _completedSpeechTurns++;
                UpdateVoiceStatus("AI 응답 대기", "🎤 누르고 말하기");
            }
            catch (Exception ex)
            {
                // Debug.WriteLine("음성 입력 종료 오류: " + ex);
                if (!_isClosing)
                    MessageBox.Show(ex.Message, "음성 입력 종료 오류");
            }
            finally
            {
                _speechStarted = false;
                _isEnding = false;
                if (!_liveService.IsConnected && !_isClosing)
                    UpdateVoiceStatus("연결 끊김", "🎤 다시 연결하기");
            }
        }

         
         
         
        private void TalkButton_PreviewMouseLeftButtonDown(
            object sender, MouseButtonEventArgs e)
        {
            BeginPushToTalk();
        }

        private void TalkButton_PreviewMouseLeftButtonUp(
            object sender, MouseButtonEventArgs e)
        {
            EndPushToTalk();
        }

        private void TalkButton_PreviewTouchDown(
            object sender, TouchEventArgs e)
        {
            BeginPushToTalk();
            e.Handled = true;
        }

        private void TalkButton_PreviewTouchUp(
            object sender, TouchEventArgs e)
        {
            EndPushToTalk();
            e.Handled = true;
        }

        private void TalkButton_MouseLeave(
            object sender, MouseEventArgs e)
        {
            if (_isPressed)
                EndPushToTalk();
        }

         
         
         
        private async Task<(bool Success, string Message)>
            OnAddToCartRequested(long productId, int quantity)
        {
             
            return await Dispatcher.InvokeAsync(() =>
            {
                if (_isClosing)
                    return (false, "키오스크가 종료되고 있습니다.");

                if (DataContext is not MainViewModel vm)
                    return (false, "장바구니 정보를 찾을 수 없습니다.");

                bool success = vm.TryAddVoiceCartItem(
                    productId, quantity, out string message);

                return (success, message);
            });
        }

         
        private async Task<(bool Success, string Message)>
            OnOrderTypeRequested(string orderType)
        {
            return await Dispatcher.InvokeAsync(() =>
            {
                if (_isClosing)
                    return (false, "키오스크가 종료되고 있습니다.");

                if (DataContext is not MainViewModel vm)
                    return (false, "화면 정보를 찾을 수 없습니다.");

                return vm.TrySelectVoiceOrderType(orderType,
                    out string message)
                    ? (true, message)
                    : (false, message);
            });
        }

        private async Task<(bool Success, string Message)> OnChangeQuantityRequested(
            long productId, string action, int quantity)
        {
            return await Dispatcher.InvokeAsync(() =>
            {
                if (_isClosing)
                    return (false, "키오스크가 종료되고 있습니다.");
                if (DataContext is not MainViewModel vm)
                    return (false, "장바구니 정보를 찾을 수 없습니다.");
                bool success = vm.TryChangeVoiceCartQuantity(
                    productId, action, quantity, out string message);
                return (success, message);
            });
        }

        private async Task<(bool Success, string Message)> OnRemoveFromCartRequested(
            long productId)
        {
            return await Dispatcher.InvokeAsync(() =>
            {
                if (_isClosing)
                    return (false, "장바구니를 수정할 수 없습니다.");
                if (DataContext is not MainViewModel vm)
                    return (false, "장바구니 정보를 찾을 수 없습니다.");
                bool success = vm.TryRemoveVoiceCartItem(productId, out string message);
                return (success, message);
            });
        }


        private async Task<(bool Success, string Message)> OnOpenSetRequested(long id, int qty) =>
            await Dispatcher.InvokeAsync(() =>
            {
                var vm = DataContext as MainViewModel;
                if (_isClosing || vm == null) return (false, "키오스크가 종료 중입니다.");
                bool ok = vm.TryOpenVoiceSet(id, qty, out string message);
                return (ok, message);
            });

        private async Task<(bool Success, string Message)> OnChooseSetOptionsRequested(string side, string drink) =>
            await Dispatcher.InvokeAsync(() =>
            {
                var vm = DataContext as MainViewModel;
                if (_isClosing || vm == null) return (false, "화면 정보를 찾지 못했습니다.");
                bool ok = vm.TryChooseVoiceSetOptions(side, drink, out string message);
                return (ok, message);
            });

        private async Task<(bool Success, string Message)> OnConfirmSetRequested() =>
            await Dispatcher.InvokeAsync(() =>
            {
                var vm = DataContext as MainViewModel;
                if (_isClosing || vm == null) return (false, "화면 정보를 찾지 못했습니다.");
                bool ok = vm.TryConfirmVoiceSet(out string message);
                return (ok, message);
            });

        private async Task<(bool Success, string Message)> OnStartCheckoutRequested() =>
            await Dispatcher.InvokeAsync(() =>
            {
                var vm = DataContext as MainViewModel;
                if (_isClosing || vm == null) return (false, "화면 정보를 찾지 못했습니다.");
                bool ok = vm.TryStartVoiceCheckout(out string message);
                return (ok, message);
            });

        private async Task<(bool Success, string Message)> OnPaymentMethodRequested(string method) =>
            await Dispatcher.InvokeAsync(() =>
            {
                var vm = DataContext as MainViewModel;
                if (_isClosing || vm == null) return (false, "화면 정보를 찾지 못했습니다.");
                bool ok = vm.TrySelectVoicePaymentMethod(method, out string message);
                if (ok)
                {
                    _paymentMethodSpeechTurn = _completedSpeechTurns;
                    _paymentSelectionSnapshot = BuildPaymentSnapshot(vm);
                }
                return (ok, message);
            });

        private async Task<(bool Success, string Message)> OnFinalOrderRequested()
        {
             
            return await Dispatcher.InvokeAsync(async () =>
            {
                var vm = DataContext as MainViewModel;
                if (_isClosing || vm == null) return (false, "키오스크가 종료 중입니다.");
                if (_paymentMethodSpeechTurn < 0 || _completedSpeechTurns <= _paymentMethodSpeechTurn)
                    return (false, "결제 방식 선택 후 다음 발화에서 다시 최종 주문 접수를 요청해 주세요.");

                 
                if (_paymentSelectionSnapshot == null ||
                    _paymentSelectionSnapshot != BuildPaymentSnapshot(vm))
                {
                    _paymentMethodSpeechTurn = -1;
                    _paymentSelectionSnapshot = null;
                    return (false, "주문 내역 또는 결제 방식이 변경되었습니다. 결제 방식을 다시 선택하고 총금액을 확인해 주세요.");
                }

                if (!vm.TryArmVoiceFinalConfirmation(out string message))
                    return (false, message);

                 
                 
                var outcome = await vm.TryConfirmVoicePaymentAsync();
                if (outcome.Success)
                {
                    _paymentMethodSpeechTurn = -1;
                    _paymentSelectionSnapshot = null;
                }
                return outcome;
            }).Task.Unwrap();
        }

         
        private static string BuildPaymentSnapshot(MainViewModel vm)
        {
            string items = string.Join("|", vm.CartItems
                .Select(item => $"{item.Menu.Id}:{item.OptionType}:{item.Side}:{item.Drink}:{item.Quantity}:{item.TotalPrice}")
                .OrderBy(x => x, StringComparer.Ordinal));
            return $"{vm.OrderType}#{vm.SelectedPaymentMethod}#{vm.TotalPrice}#{items}";
        }

         
        private void UpdateVoiceStatus(string status, string buttonText)
        {
            if (_isClosing) return;
            Title = $"BURGER KIOSK | AI 음성: {status}";
            if (FindName("TalkButton") is Button button)
                button.Content = buttonText;
        }

        private void OnConnectionChanged(bool connected)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosing || _isPreparingVoice || _isStarting || _isPressed || _speechStarted) return;
                if (!connected)
                {
                    _nextVoicePreparationUtc = DateTime.UtcNow.AddSeconds(30);
                    UpdateVoiceStatus("연결 끊김", "🎤 다시 연결하기");
//                     Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [연결] 종료 - 다음 발화에서 재연결");
                }
                else
                {
                    UpdateVoiceStatus("연결됨", "🎤 누르고 말하기");
//                     Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [연결] 연결 준비 완료");
                }
            }));
        }

        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsHomeVisible) && sender is MainViewModel vm && vm.IsHomeVisible)
            {
                _newVoiceSessionRequired = true;
                _paymentMethodSpeechTurn = -1;
                _paymentSelectionSnapshot = null;
            }
        }

        private void OnLiveError(string message)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (_isClosing || _isPreparingVoice || (DateTime.UtcNow - _lastVoiceErrorUtc).TotalSeconds < 15) return;
                _lastVoiceErrorUtc = DateTime.UtcNow;
                if (!_speechStarted && !_isPressed)
                {
                    _newVoiceSessionRequired = true;
                    _nextVoicePreparationUtc = DateTime.UtcNow.AddSeconds(30);
                    UpdateVoiceStatus("연결 재시도 대기 · 터치 주문 가능", "🎤 다시 연결하기");
                    return;
                }
                _isPressed = false;
                try
                {
                    if (_microphoneService.IsRecording) _microphoneService.StopRecording();
                    await _liveService.DisconnectAsync();
                }
                catch (Exception) { }
                _speechStarted = false;
                _isStarting = false;
                _isEnding = false;
                UpdateVoiceStatus("연결 확인 필요", "🎤 다시 시도하기");
                MessageBox.Show("음성 상담 연결에 문제가 생겼습니다. 마이크 버튼을 다시 눌러 시도해 주세요. 화면 터치로도 주문할 수 있습니다.", "음성 상담 안내");
            }));
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosing = true;
            _idleTimer.Stop();
            _idleTimer.Tick -= OnIdleTick;
            PreviewMouseDown -= OnKioskInteraction;
            PreviewTouchDown -= OnKioskInteraction;
            PreviewKeyDown -= OnKioskInteraction;
            if (DataContext is MainViewModel vm) vm.PropertyChanged -= OnViewModelChanged;
            _isPressed = false;
            Loaded -= MainWindow_VoiceLoaded;
            Loaded -= MainWindow_AccessibilityLoaded;
            PreviewKeyDown -= MainWindow_AccessibilityKeyDown;
            _originalFontSizes.Clear();

            _microphoneService.AudioDataAvailable -= _liveService.QueueAudio;
            _liveService.AddToCartRequested -= OnAddToCartRequested;
            _liveService.OrderTypeRequested -= OnOrderTypeRequested;
            _liveService.ChangeQuantityRequested -= OnChangeQuantityRequested;
            _liveService.RemoveFromCartRequested -= OnRemoveFromCartRequested;
            _liveService.OpenSetRequested -= OnOpenSetRequested;
            _liveService.ChooseSetOptionsRequested -= OnChooseSetOptionsRequested;
            _liveService.ConfirmSetRequested -= OnConfirmSetRequested;
            _liveService.StartCheckoutRequested -= OnStartCheckoutRequested;
            _liveService.PaymentMethodRequested -= OnPaymentMethodRequested;
            _liveService.FinalOrderRequested -= OnFinalOrderRequested;
            _liveService.ErrorOccurred -= OnLiveError;
            _liveService.ConnectionChanged -= OnConnectionChanged;

            _microphoneService.Dispose();
            _liveService.Dispose();
            base.OnClosed(e);
        }
    }
}
