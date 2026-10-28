using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BurgerKiosk.Services
{
    public class LiveTokenService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        private readonly SupabaseService _supabase;
        private readonly string _url;
        private readonly string _publishableKey;

        public LiveTokenService(
            SupabaseService supabase,
            string projectUrl,
            string publishableKey)
        {
            _supabase = supabase;
            _publishableKey = publishableKey;

            _url = projectUrl.TrimEnd('/') +
                "/functions/v1/create-live-token";
        }

        public async Task<string> GetTokenAsync()
        {
            string? jwt = _supabase.GetAccessToken();

            if (string.IsNullOrWhiteSpace(jwt))
                throw new Exception(
                    "키오스크 로그인이 필요합니다.");

            using var request = new HttpRequestMessage(
                HttpMethod.Post, _url);

            request.Headers.Add(
                "apikey", _publishableKey);

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer", jwt);

            request.Content =
                JsonContent.Create(new { });

            using var response =
                await Http.SendAsync(request);

            string json =
                await response.Content.ReadAsStringAsync();

            TokenResponse? result;
            try { result = JsonSerializer.Deserialize<TokenResponse>(json); }
            catch (JsonException)
            {
                throw new Exception("음성 연결 서버의 응답을 읽지 못했습니다. 잠시 후 다시 시도해 주세요.");
            }
            if (!response.IsSuccessStatusCode)
                throw new Exception(result?.Error ?? $"음성 연결을 준비하지 못했습니다. 잠시 후 다시 시도해 주세요. (HTTP {(int)response.StatusCode})");

            if (result?.Success != true ||
                string.IsNullOrWhiteSpace(result.Token))
                throw new Exception(
                    "올바른 임시 토큰을 받지 못했습니다.");

            return result.Token;
        }

        private class TokenResponse
        {
            [JsonPropertyName("error")]
            public string? Error { get; set; }
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("token")]
            public string? Token { get; set; }
        }
    }
}
