using BurgerKiosk.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BurgerKiosk.Services
{
    public class OrderService
    {
        private static readonly HttpClient _httpClient = new();

        private readonly SupabaseService _supabaseService;
        private readonly string _functionUrl;
        private readonly string _publishableKey;
        public string? LoyaltySelectionId { get; set; }
        private readonly Dictionary<string,string> _pendingRequestIds = new();

        public OrderService(
            SupabaseService supabaseService,
            string projectUrl,
            string publishableKey)
        {
            _supabaseService = supabaseService;
            _publishableKey = publishableKey;

            _functionUrl =
                projectUrl.TrimEnd('/') +
                "/functions/v1/create-order";
        }

         
        public async Task<long> CreateOrderAsync(
            string orderType,
            IEnumerable<CartItemModel> cartItems)
        {
             
            string? accessToken =
                _supabaseService.GetAccessToken();

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException(
                    "키오스크 로그인이 필요합니다.");
            }

             
            var items = cartItems.Select(item => new
            {
                product_id = item.Menu.Id,
                quantity = item.Quantity,
                option_type = item.OptionType,
                side_name = item.Side,
                drink_name = item.Drink,
                displayed_unit_price = item.UnitPrice
            }).ToList();

            if (items.Count == 0)
            {
                throw new InvalidOperationException(
                    "장바구니가 비어 있습니다.");
            }

             
            string apiOrderType = orderType switch
            {
                "매장에서 식사" => "매장",
                "매장식사" => "매장",
                "매장" => "매장",
                "포장" => "포장",
                _ => throw new InvalidOperationException(
                    "올바르지 않은 식사 방법입니다.")
            };

             
            string fingerprint = JsonSerializer.Serialize(new { order_type = apiOrderType, items, loyalty_selection_id = LoyaltySelectionId });
            if (!_pendingRequestIds.TryGetValue(fingerprint, out string? requestId))
                _pendingRequestIds[fingerprint] = requestId = Guid.NewGuid().ToString();
            var requestBody = new
            {
                loyalty_selection_id = LoyaltySelectionId,
                request_id = requestId,
                order_type = apiOrderType,
                items = items
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                _functionUrl);

            request.Headers.Add(
                "apikey",
                _publishableKey);

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            request.Content =
                JsonContent.Create(requestBody);

             
            using var response =
                await _httpClient.SendAsync(request);

            string responseText =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                 
                OrderResponse? failure = null;
                try
                {
                    failure = JsonSerializer.Deserialize<OrderResponse>(responseText);
                }
                catch (JsonException)
                {
                     
                }

                string code = failure?.Code ?? "ORDER_API_FAILED";
                string message = failure?.Error ?? "주문 요청을 처리하지 못했습니다. 잠시 후 다시 시도해 주세요.";

                throw new OrderApiException(code, message, response.StatusCode);
            }

             
            var result =
                JsonSerializer.Deserialize<OrderResponse>(
                    responseText);

            if (result == null ||
                !result.Success ||
                result.OrderId == null)
            {
                throw new Exception(
                    result?.Error ?? "주문 저장에 실패했습니다.");
            }

             
            _pendingRequestIds.Remove(fingerprint);
            return result.OrderId.Value;
        }

         
        private class OrderResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("order_id")]
            public long? OrderId { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("code")]
            public string? Code { get; set; }
        }
    }
}

namespace BurgerKiosk.Services
{
     
    public sealed class OrderApiException : Exception
    {
        public string ErrorCode { get; }
        public HttpStatusCode StatusCode { get; }

        public OrderApiException(string errorCode, string message, HttpStatusCode statusCode)
            : base(message)
        {
            ErrorCode = errorCode;
            StatusCode = statusCode;
        }
    }
}
