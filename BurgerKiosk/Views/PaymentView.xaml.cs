using System;
using BurgerKiosk.Services;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace BurgerKiosk.Views
{
    public partial class PaymentView : UserControl
    {
        public PaymentView()
        {
            InitializeComponent();
            KakaoWebView.NavigationCompleted += (_, e) =>
                PaymentDiagnostics.Write($"webview-navigation-completed: success={e.IsSuccess} http={e.HttpStatusCode} error={e.WebErrorStatus}");
        }

        private void KakaoPayOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (KakaoWebView == null || CallbackWaitingText == null) return;
            if (e.NewValue is bool isVisible && isVisible)
            {
                bool waiting = KakaoWebView.Source == null || KakaoWebView.Source.AbsoluteUri == "about:blank";
                KakaoWebView.Visibility = waiting ? Visibility.Collapsed : Visibility.Visible;
                CallbackWaitingText.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void KakaoWebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Uri)) return;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
             
             
            if (uri.Host.Equals("qtckzactriwoiiscrkqd.supabase.co", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.EndsWith("/functions/v1/kakaopay-callback", StringComparison.OrdinalIgnoreCase))
            {
                PaymentDiagnostics.Write("callback-navigation-started");
                KakaoWebView.Visibility = Visibility.Collapsed;
                CallbackWaitingText.Visibility = Visibility.Visible;
            }
        }
    }
}
