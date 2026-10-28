using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BurgerKiosk.Services
{
    public sealed class AdminOrderCancelService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
        private readonly AdminAuthService _auth;
        public AdminOrderCancelService(AdminAuthService auth) => _auth = auth;

        private async Task<string> RpcAsync(string function, object args)
        {
            if (string.IsNullOrWhiteSpace(_auth.AdminAccessToken))
                throw new InvalidOperationException("관리자 로그인이 필요합니다.");
            using var request = new HttpRequestMessage(HttpMethod.Post, _auth.BaseUrl + "/rest/v1/rpc/" + function);
            request.Headers.Add("apikey", _auth.PublishableKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.AdminAccessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(args), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (text.Contains("PAID_ORDER_REFUND_REQUIRED", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("카카오페이 결제 주문은 먼저 결제 환불 확인이 필요합니다. 결제 관리자에서 환불 상태를 확인해 주세요.");
                if (text.Contains("ORDER_ALREADY_CANCELLED", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("이미 취소된 주문입니다. 재고를 중복 복구하지 않았습니다.");
                if (text.Contains("ORDER_NOT_CANCELLABLE", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("접수 상태인 주문만 취소할 수 있습니다.");
                if (text.Contains("ORDER_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("주문을 찾을 수 없습니다.");
                if (text.Contains("42501", StringComparison.OrdinalIgnoreCase) || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    throw new InvalidOperationException("관리자 권한이 없거나 로그인이 만료되었습니다.");
                throw new InvalidOperationException("주문 처리에 실패했습니다. SQL 적용 여부와 Supabase 로그를 확인해 주세요.");
            }
            return text;
        }

        public async Task<List<AdminCancelOrder>> GetOrdersAsync()
        {
            var json = await RpcAsync("admin_list_cancel_orders", new { p_limit = 100 });
            return JsonSerializer.Deserialize<List<AdminCancelOrder>>(json, Options) ?? new();
        }

        public async Task CancelAsync(long orderId)
        {
            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId));
            await RpcAsync("admin_cancel_order", new { p_order_id = orderId });
        }
    }

    public sealed class AdminCancelOrder
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("order_type")] public string OrderType { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("total_price")] public long TotalPrice { get; set; }
        [JsonPropertyName("items")] public List<AdminCancelOrderItem> Items { get; set; } = new();
        public string CreatedText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        public string PriceText => $"{TotalPrice:N0}원";
        public string ItemsText => string.Join(", ", Items.ConvertAll(x => $"{x.Name} ×{x.Quantity}"));
    }
    public sealed class AdminCancelOrderItem
    {
        [JsonPropertyName("product_name")] public string Name { get; set; } = "";
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
    }
}
