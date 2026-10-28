using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BurgerKiosk.Services
{
     
    public sealed class AdminAuthService
    {
        public string? AdminAccessToken { get; private set; }
        public void SignOut() => AdminAccessToken = null;
        private readonly string _url;
        public string BaseUrl => _url;
        private readonly string _publishableKey;
        public string PublishableKey => _publishableKey;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public AdminAuthService(string url, string publishableKey)
        {
            _url = url.TrimEnd('/');
            _publishableKey = publishableKey;
        }

        public async Task<bool> SignInAndVerifyAsync(string email, string password)
        {
            SignOut();
             
            using var login = new HttpRequestMessage(HttpMethod.Post, _url + "/auth/v1/token?grant_type=password");
            login.Headers.Add("apikey", _publishableKey);
            login.Content = new StringContent(
                JsonSerializer.Serialize(new { email, password }), Encoding.UTF8, "application/json");
            using var loginResponse = await Http.SendAsync(login);
            if (!loginResponse.IsSuccessStatusCode) return false;
            using var authJson = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
            if (!authJson.RootElement.TryGetProperty("access_token", out var tokenNode)) return false;
            var accessToken = tokenNode.GetString();
            if (string.IsNullOrWhiteSpace(accessToken)) return false;

             
            using var verify = new HttpRequestMessage(HttpMethod.Post, _url + "/rest/v1/rpc/is_kiosk_admin");
            verify.Headers.Add("apikey", _publishableKey);
            verify.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            verify.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var verifyResponse = await Http.SendAsync(verify);
            if (!verifyResponse.IsSuccessStatusCode) return false;
            var result = (await verifyResponse.Content.ReadAsStringAsync()).Trim();
            if (result != "true") return false;
            AdminAccessToken = accessToken;
            return true;
        }
    }
}
