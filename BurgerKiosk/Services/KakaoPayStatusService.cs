using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BurgerKiosk.Services
{
    public sealed record KakaoPayStatusResult(string Status, long? OrderId, int? Amount,
        string? PaymentId = null, string? PaymentUrl = null);

    public interface IKakaoPayStatusService
    {
        Task<KakaoPayStatusResult> GetStatusAsync(string orderId, CancellationToken token = default);
        Task<string> CancelAsync(string orderId);
        Task<KakaoPayStatusResult?> GetPendingAsync();
        Task AcknowledgeAsync(string paymentId);
    }

    public sealed class KakaoPayStatusService : IKakaoPayStatusService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };
        private readonly SupabaseService _supabase;
        private readonly string _baseUrl;
        private readonly string _key;

        public KakaoPayStatusService(SupabaseService supabase, string projectUrl, string publishableKey)
        {
            _supabase = supabase;
            _baseUrl = projectUrl.TrimEnd('/') + "/functions/v1/";
            _key = publishableKey;
        }

        private async Task<JsonDocument> RequestAsync(string function, HttpMethod method, object? body,
            CancellationToken token = default)
        {
            var access = _supabase.GetAccessToken();
            if (string.IsNullOrWhiteSpace(access)) throw new InvalidOperationException("키오스크 로그인이 만료되었습니다.");
            using var request = new HttpRequestMessage(method, _baseUrl + function);
            request.Headers.Add("apikey", _key);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            if (body != null) request.Content = JsonContent.Create(body);
            using var response = await Http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"결제 확인 실패 (HTTP {(int)response.StatusCode}). 연결 상태를 확인하고 다시 시도해 주세요.");
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        }

        private static KakaoPayStatusResult Parse(JsonElement root)
        {
            string? Text(string name) => root.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            long? Number(string name) => root.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
            var amount = Number("amount");
            return new KakaoPayStatusResult(Text("status") ?? "unknown", Number("final_order_id"),
                amount is >= 0 and <= int.MaxValue ? (int)amount : null, Text("payment_id"), Text("payment_url"));
        }

        public async Task<KakaoPayStatusResult> GetStatusAsync(string orderId, CancellationToken token = default)
        {
            using var doc = await RequestAsync("kakaopay-status?order_id=" + Uri.EscapeDataString(orderId), HttpMethod.Get, null, token);
            return Parse(doc.RootElement);
        }

        public async Task<string> CancelAsync(string orderId)
        {
            using var doc = await RequestAsync("kakaopay-cancel", HttpMethod.Post, new { order_id = orderId });
            return Parse(doc.RootElement).Status;
        }

        public async Task<KakaoPayStatusResult?> GetPendingAsync()
        {
            using var doc = await RequestAsync("kakaopay-recover", HttpMethod.Get, null);
            if (!doc.RootElement.TryGetProperty("payment", out var value) || value.ValueKind == JsonValueKind.Null) return null;
            return Parse(value);
        }

        public async Task AcknowledgeAsync(string paymentId)
        {
            using var doc = await RequestAsync("kakaopay-recover", HttpMethod.Post, new { order_id = paymentId });
        }
    }
}
