using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BurgerKiosk.Services
{
    public sealed class AdminInventoryService
    {
        private readonly string _baseUrl;
        private readonly string _publicKey;
        private readonly AdminAuthService _auth;
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        public AdminInventoryService(string baseUrl, string publicKey, AdminAuthService auth)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _publicKey = publicKey;
            _auth = auth;
        }

        private async Task<string> RpcAsync(string functionName, object arguments)
        {
            if (string.IsNullOrWhiteSpace(_auth.AdminAccessToken))
                throw new InvalidOperationException("관리자 로그인이 필요합니다.");

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_baseUrl}/rest/v1/rpc/{functionName}");
            request.Headers.Add("apikey", _publicKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.AdminAccessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(arguments), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                 
                throw new InvalidOperationException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? "관리자 인증이 만료되었거나 권한이 없습니다. 다시 로그인해 주세요."
                    : "재고 서버 요청에 실패했습니다. 권한 및 연결 상태를 확인해 주세요.");
            }
            return body;
        }

        public async Task<List<AdminStockItem>> GetStockAsync()
        {
            var body = await RpcAsync("admin_list_product_stock", new { });
            return JsonSerializer.Deserialize<List<AdminStockItem>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }

        public async Task SetStockAsync(long productId, int newStock)
        {
            if (newStock < 0 || newStock > 1000000)
                throw new ArgumentOutOfRangeException(nameof(newStock), "재고는 0~1,000,000개만 입력할 수 있습니다.");
            await RpcAsync("admin_set_product_stock", new {
                p_product_id = productId,
                p_new_stock = newStock
            });
        }
    }

    public sealed class AdminStockItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public long Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("category")]
        public string Category { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("stock_quantity")]
        public int StockQuantity { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("is_available")]
        public bool IsAvailable { get; set; }
        public string Status => !IsAvailable ? "판매 중지" : StockQuantity == 0 ? "품절" : "판매 중";
    }
}
