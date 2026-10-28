using BurgerKiosk.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BurgerKiosk.Services
{
    public class SupabaseService
    {
        private readonly Supabase.Client _client;
        private readonly DeviceSessionStore? _sessionStore;
        private readonly string _url, _key;
        private static readonly System.Net.Http.HttpClient DeviceHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
        private readonly System.Net.Http.HttpClient _deviceHttp;

        public SupabaseService(string url, string key, DeviceSessionStore? sessionStore = null, System.Net.Http.HttpClient? transport = null)
        {
            _url = url.TrimEnd('/'); _key = key; _sessionStore = sessionStore;
            _deviceHttp = transport ?? DeviceHttp;
            var options = new Supabase.SupabaseOptions { AutoRefreshToken = true, AutoConnectRealtime = false,
                HttpClient = _deviceHttp, GotrueRetry = new Supabase.Core.Http.RetryOptions { MaxRetries = 0 } };
            if (sessionStore != null) options.SessionHandler = sessionStore;
            _client = new Supabase.Client(url, key, options);
        }

        public async Task<bool> RestoreRegisteredDeviceAsync()
        {
            await InitializeAsync();
            if (string.IsNullOrEmpty(_client.Auth.CurrentSession?.RefreshToken)) return false;
            await _client.Auth.RefreshSession();
            return await VerifyRegisteredDeviceAsync();
        }

        public async Task<bool> RegisterDeviceAsync(string email, string password)
        {
            _sessionStore?.SuspendWrites();
            if (!await LoginAsync(email, password)) return false;
            return await VerifyRegisteredDeviceAsync();
        }

        public void SaveRegisteredDevice()
        {
            var session = _client.Auth.CurrentSession;
            if (session == null) throw new InvalidOperationException("기기 로그인을 다시 확인해 주세요.");
            try { _sessionStore?.CommitVerifiedSession(session); }
            catch (Exception) { throw new InvalidOperationException("기기 정보를 저장하지 못했습니다. 저장 공간과 권한을 확인해 주세요."); }
        }
        public void StopAuthRefresh() => _client.Auth.StopAutoRefresh();

        private async Task<bool> VerifyRegisteredDeviceAsync()
        {
            string? token = GetAccessToken(); if (string.IsNullOrWhiteSpace(token)) return false;
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, _url + "/rest/v1/rpc/registered_kiosk_device_info");
            request.Headers.Add("apikey", _key);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var response = await _deviceHttp.SendAsync(request);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) return false;
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("기기 확인 서버에 연결하지 못했습니다.");
            using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && document.RootElement.TryGetProperty("id", out _);
        }

         
        public async Task InitializeAsync()
        {
            await _client.InitializeAsync();
        }

         
        public async Task<List<ProductModel>> GetProductsAsync()
        {
            var response = await _client
                .From<ProductModel>()
                .Get();

            return response.Models;
        }

         
        public async Task<bool> LoginAsync(
            string email,
            string password)
        {
            var session = await _client.Auth.SignIn(
                email,
                password);
            return session != null &&
                   !string.IsNullOrEmpty(session.AccessToken);
        }

         
        public string? GetAccessToken()
        {
            return _client.Auth.CurrentSession?.AccessToken;
        }
    }
}
