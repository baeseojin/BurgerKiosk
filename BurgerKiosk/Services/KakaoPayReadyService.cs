using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BurgerKiosk.Models;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BurgerKiosk.Services
{
    public sealed record KakaoPayReadyResult(Uri PaymentUri, string OrderId, int Amount);

     
     
    public sealed class KakaoPayReadyService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
        private readonly SupabaseService _supabase;
        private readonly string _url;
        private readonly string _publishableKey;
        public string? LoyaltySelectionId { get; set; }

        public KakaoPayReadyService(SupabaseService supabase, string projectUrl, string publishableKey)
        {
            _supabase = supabase;
            _url = projectUrl.TrimEnd('/') + "/functions/v1/kakaopay-ready";
            _publishableKey = publishableKey;
        }

        public async Task<KakaoPayReadyResult> PrepareAsync(string orderType, IEnumerable<CartItemModel> cartItems)
        {
            var token = _supabase.GetAccessToken();
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("키오스크 로그인 상태를 확인해 주세요.");

            var items = cartItems.Select(item => new
            {
                product_id = item.Menu.Id, quantity = item.Quantity,
                option_type = item.OptionType, side_name = item.Side, drink_name = item.Drink,
                displayed_unit_price = item.UnitPrice
            }).ToArray();
            if (items.Length == 0) throw new InvalidOperationException("장바구니가 비어 있습니다.");
            int expectedAmount = checked(items.Sum(item => checked(item.displayed_unit_price * item.quantity)));
            string apiOrderType = orderType switch
            {
                "매장에서 식사" or "매장식사" or "매장" => "매장",
                "포장" => "포장",
                _ => throw new InvalidOperationException("올바른 식사 방법을 선택해 주세요.")
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, _url);
            request.Headers.Add("apikey", _publishableKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new { loyalty_selection_id = LoyaltySelectionId, request_id = Guid.NewGuid().ToString(), order_type = apiOrderType, items, expected_amount = expectedAmount });
            System.Diagnostics.Debug.WriteLine("[KakaoPay] Edge Function 요청 전송");
            using var response = await Http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"[KakaoPay] Edge Function HTTP {(int)response.StatusCode}");
            ReadyResponse? data;
            try { data = System.Text.Json.JsonSerializer.Deserialize<ReadyResponse>(raw); }
            catch (System.Text.Json.JsonException) { throw new InvalidOperationException("결제 준비 응답을 읽지 못했습니다."); }
            if (!response.IsSuccessStatusCode || data?.Success != true)
                throw new InvalidOperationException(data?.Error ?? $"카카오페이 결제 준비 실패 (HTTP {(int)response.StatusCode}). 서버 함수를 업데이트했는지 확인해 주세요.");
            if (!Uri.TryCreate(data.RedirectUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !(uri.Host.Equals("kakaopay.com", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.EndsWith(".kakaopay.com", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.Equals("kakao.com", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.EndsWith(".kakao.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("카카오페이 결제 페이지 주소를 확인할 수 없습니다.");
            if (string.IsNullOrWhiteSpace(data.OrderId))
                throw new InvalidOperationException("결제 준비 응답에 주문번호가 없습니다.");
            if (data.Amount <= 0 || data.Amount != expectedAmount)
                throw new InvalidOperationException("서버 결제금액이 장바구니와 다릅니다. 결제를 중단하고 메뉴 가격을 다시 확인해 주세요.");
            return new KakaoPayReadyResult(uri, data.OrderId, data.Amount);
        }

        private sealed class ReadyResponse
        {
            [JsonPropertyName("amount")] public int Amount { get; set; }
            [JsonPropertyName("order_id")] public string? OrderId { get; set; }
            [JsonPropertyName("success")] public bool Success { get; set; }
            [JsonPropertyName("error")] public string? Error { get; set; }
            [JsonPropertyName("next_redirect_pc_url")] public string? RedirectUrl { get; set; }
        }
    }
}
