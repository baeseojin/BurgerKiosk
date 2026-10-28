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
    public sealed class AdminMenuItem
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("burger_product_id")] public long? BurgerProductId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("category")] public string Category { get; set; } = "버거";
        [JsonPropertyName("price")] public int Price { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
        [JsonPropertyName("stock_quantity")] public int StockQuantity { get; set; }
        [JsonPropertyName("is_available")] public bool IsAvailable { get; set; }
        [JsonPropertyName("is_recommended")] public bool IsRecommended { get; set; }
    }

    public sealed class AdminMenuDeleteException : Exception
    {
        public AdminMenuDeleteException(string message) : base(message) { }
    }

    public sealed class AdminMenuService
    {
        private readonly AdminAuthService _auth;
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
        public AdminMenuService(AdminAuthService auth) => _auth = auth;

        private async Task<string> RpcAsync(string function, object args)
        {
            var token = _auth.AdminAccessToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("관리자 로그인이 필요합니다.");
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_auth.BaseUrl}/rest/v1/rpc/{function}");
            request.Headers.Add("apikey", _auth.PublishableKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(args), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode && function == "admin_delete_product")
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    if (message.Contains("PRODUCT_IS_SET_COMPONENT"))
                        throw new AdminMenuDeleteException("세트 구성품은 삭제할 수 없습니다. 판매 중지를 이용해 주세요.");
                    if (message.Contains("PRODUCT_HAS_ACTIVE_PAYMENT"))
                        throw new AdminMenuDeleteException("결제 중인 상품은 삭제할 수 없습니다. 결제 종료 후 다시 시도해 주세요.");
                    if (message.Contains("ORDER_HISTORY_EXISTS"))
                        throw new AdminMenuDeleteException("이 상품은 주문 이력이 있어 영구 삭제할 수 없습니다. 판매 중지를 이용해 주세요.");
                    if (message.Contains("PRODUCT_NOT_FOUND"))
                        throw new AdminMenuDeleteException("이미 삭제되었거나 존재하지 않는 상품입니다.");
                }
                catch (JsonException) { }
            }
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? "관리자 인증 또는 권한이 유효하지 않습니다. 다시 로그인하세요."
                    : "메뉴 변경에 실패했습니다. SQL 함수, 입력값 및 서버 로그를 확인하세요.");
            return body;
        }

        public async Task<List<AdminMenuItem>> GetMenusAsync()
        {
            var json = await RpcAsync("admin_list_products_v2", new { });
            return JsonSerializer.Deserialize<List<AdminMenuItem>>(json) ?? new();
        }

        public async Task<long> SaveMenuAsync(AdminMenuItem item)
        {
            var json = await RpcAsync("admin_save_product_v2", new
            {
                p_id = item.Id <= 0 ? (long?)null : item.Id,
                p_name = item.Name,
                p_category = item.Category,
                p_price = item.Price,
                p_description = item.Description,
                p_image_url = item.ImageUrl,
                p_is_available = item.IsAvailable,
                p_is_recommended = item.IsRecommended,
                p_stock_quantity = item.StockQuantity,
                p_burger_product_id = item.BurgerProductId
            });
            return JsonSerializer.Deserialize<long>(json);
        }

        public async Task DeleteMenuAsync(long id)
        {
            try
            {
                await RpcAsync("admin_delete_product", new { p_id = id });
            }
            catch (InvalidOperationException ex) when
                (ex.Message.Contains("SQL 함수"))
            {
                 
                throw;
            }
        }

        public Task StopSellingAsync(long id) => RpcAsync("admin_disable_product", new { p_id = id });
    }
}
