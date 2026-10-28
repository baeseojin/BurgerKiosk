using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BurgerKiosk.Services
{
    public sealed class AdminStatisticsService
    {
        private readonly AdminAuthService _auth;
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public AdminStatisticsService(AdminAuthService auth) => _auth = auth;

        public async Task<AdminStatisticsResult> GetStatisticsAsync(DateTime start, DateTime end)
        {
            if (start.Date > end.Date || (end.Date - start.Date).TotalDays > 366)
                throw new ArgumentException("조회 날짜를 확인해 주세요 (최대 367일).");
            if (string.IsNullOrWhiteSpace(_auth.AdminAccessToken))
                throw new InvalidOperationException("관리자 로그인이 필요합니다.");
            using var request = new HttpRequestMessage(HttpMethod.Post,
                _auth.BaseUrl + "/rest/v1/rpc/admin_order_statistics");
            request.Headers.Add("apikey", _auth.PublishableKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.AdminAccessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                p_start_date = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                p_end_date = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            }), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? "관리자 로그인이 만료되었거나 조회 권한이 없습니다."
                    : "통계 조회에 실패했습니다. Supabase에 41-4 SQL을 적용했는지 확인해 주세요.");
            return JsonSerializer.Deserialize<AdminStatisticsResult>(json, JsonOptions)
                ?? throw new InvalidOperationException("통계 응답이 비어 있습니다.");
        }
    }
    public sealed class AdminStatisticsResult
    {
        [JsonPropertyName("order_count")] public long OrderCount { get; set; }
        [JsonPropertyName("total_sales")] public long TotalSales { get; set; }
        [JsonPropertyName("daily")] public List<AdminDailyStatistic> Daily { get; set; } = new();
        [JsonPropertyName("top_products")] public List<AdminTopProduct> TopProducts { get; set; } = new();
    }
    public sealed class AdminDailyStatistic
    {
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("order_count")] public long OrderCount { get; set; }
        [JsonPropertyName("sales")] public long Sales { get; set; }
        public string SalesText => $"{Sales:N0}원";
    }
    public sealed class AdminTopProduct
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("quantity")] public long Quantity { get; set; }
        [JsonPropertyName("sales")] public long Sales { get; set; }
        public string SalesText => $"{Sales:N0}원";
    }
}
