
using BurgerKiosk.Services;
using BurgerKiosk.ViewModels;
using BurgerKiosk.Models;
using System.Windows.Threading;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BurgerKiosk
{
    public partial class App : Application
    {
        private System.Threading.Mutex? _instanceGuard;
        private bool _ownsInstance;
        private SupabaseService? _deviceService;
        protected override void OnExit(ExitEventArgs e)
        {
            _deviceService?.StopAuthRefresh();
            if (_ownsInstance) _instanceGuard?.ReleaseMutex();
            _instanceGuard?.Dispose();
            base.OnExit(e);
        }
         
         
         

        // [DllImport("kernel32.dll")]
        // private static extern bool AllocConsole();

        protected override async void OnStartup(
            StartupEventArgs e)
        {
            base.OnStartup(e);
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            _instanceGuard = new System.Threading.Mutex(false, @"Local\BurgerKiosk-" + user);
            try { _ownsInstance = _instanceGuard.WaitOne(0); }
            catch (System.Threading.AbandonedMutexException) { _ownsInstance = true; }
            if (!_ownsInstance)
            {
                MessageBox.Show("이미 키오스크가 실행 중입니다. 기기 등록을 변경하려면 실행 중인 앱을 먼저 종료해 주세요.", "키오스크 실행 안내");
                Shutdown(); return;
            }

             
             
             

#if DEBUG
            // AllocConsole();

//             Console.OutputEncoding = Encoding.UTF8;

//             Console.WriteLine(
//                 "====================================");

//             Console.WriteLine(
//                 "    BURGER KIOSK AI 음성 디버그");

//             Console.WriteLine(
//                 "====================================");

//             Console.WriteLine(
//                 $"[시작] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

//             Console.WriteLine(
//                 "[상태] 키오스크 초기화 중...");

//             Console.WriteLine();
#endif

             
             
             

            Resources["BoolToVisibility"] =
                new BooleanToVisibilityConverter();

             
             
             

            string url =
                "https://qtckzactriwoiiscrkqd.supabase.co";

            string key =
                "sb_publishable_Um04ERgyhHb5YtXYspqH5A_Tjvf_ZmF";

            try
            {
                 
                 
                 

                var supabaseService =
                    new SupabaseService(url, key, new DeviceSessionStore(url));
                _deviceService = supabaseService;

                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var registration = new DeviceRegistrationWindow(supabaseService, e.Args.Contains("--register-device"));
                MainWindow = registration;
                if (registration.ShowDialog() != true) { Shutdown(); return; }

                var products =
                    await supabaseService.GetProductsAsync();

                if (products.Count == 0)
                {
                    throw new Exception(
                        "조회된 상품이 없습니다.");
                }

#if DEBUG
//                 Console.WriteLine(
//                     $"[Supabase] 상품 {products.Count}개 조회 완료");
#endif

                 
                 
                 

                var orderService =
                    new OrderService(
                        supabaseService,
                        url,
                        key);

                 
                 
                 

                var liveTokenService =
                    new LiveTokenService(
                        supabaseService,
                        url,
                        key);

                 
                 
                 

                var geminiLiveService =
                    new GeminiLiveService(products);

                 
                 
                 

                var window = new MainWindow(
                    products,
                    orderService,
                    liveTokenService,
                    geminiLiveService,
                    new AdminAuthService(url, key));

                MainWindow = window;

                ShutdownMode = ShutdownMode.OnMainWindowClose;
                window.Show();

                 
                 
                 

                if (window.DataContext is MainViewModel mainVm)
                {
                    mainVm.LoyaltyLookupAsync = new LoyaltyService(url, key, supabaseService.GetAccessToken).EnrollAsync;
                    mainVm.KakaoPayReadyService = new KakaoPayReadyService(supabaseService, url, key);
                    mainVm.KakaoPayStatusService = new KakaoPayStatusService(supabaseService, url, key);

                     
                     
                     

                    mainVm.StockLoaderAsync = async () =>
                    {
                        IReadOnlyList<ProductModel> latestProducts =
                            await supabaseService.GetProductsAsync();

                        return latestProducts;
                    };
                    await mainVm.RecoverPendingPaymentAsync();
                }

                 
                 
                 

                var stockTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(10)
                };

                 
                bool stockRefreshRunning = false;

                stockTimer.Tick += async (_, _) =>
                {
                    if (stockRefreshRunning)
                        return;

                    if (window.DataContext is not MainViewModel vm)
                        return;

                    stockRefreshRunning = true;

                    try
                    {
                         
                        var latest =
                            await supabaseService.GetProductsAsync();

                         
                        vm.RefreshProductStock(latest);

                         
                         
                         

                        geminiLiveService.UpdateProducts(latest);

#if DEBUG
//                         Console.WriteLine(
//                             $"[{DateTime.Now:HH:mm:ss}] " +
//                             "[재고] 최신 재고 갱신 완료");
#endif
                    }
                    catch (Exception)
                    {
#if DEBUG
//                         Console.WriteLine(
//                             $"[{DateTime.Now:HH:mm:ss}] " +
//                             $"[재고 갱신 실패] {ex.Message}");
#endif

                         
                         
                    }
                    finally
                    {
                        stockRefreshRunning = false;
                    }
                };

                 
                window.Closed += (_, _) =>
                {
                    stockTimer.Stop();
                };

                 
                stockTimer.Start();

#if DEBUG
//                 Console.WriteLine(
//                     "[WPF] 키오스크 화면 실행 완료");

//                 Console.WriteLine(
//                     "[대기] 음성 주문을 시작할 수 있습니다.");

//                 Console.WriteLine();
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
//                 Console.WriteLine(
//                     $"[오류] {ex}");
#endif

                MessageBox.Show(
                    ex.ToString(),
                    "프로그램 실행 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
            }
        }
    }
}
