using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BurgerKiosk.Services;
public sealed record LoyaltyMemberResult(
    [property:JsonPropertyName("selection_id")] string SelectionId,
    [property:JsonPropertyName("masked_phone")] string MaskedPhone,
    [property:JsonPropertyName("points")] long Points,
    [property:JsonPropertyName("is_new")] bool IsNew);
public sealed class LoyaltyMember
{
    [JsonPropertyName("phone")] public string Phone {get;set;}="";
    [JsonPropertyName("points")] public long Points {get;set;}
    public string PointsText=>$"{Points:N0}p";
}
public sealed class LoyaltyService(string url,string key,Func<string?> token)
{
    private static readonly HttpClient Http=new(){Timeout=TimeSpan.FromSeconds(20)};
    private async Task<string> SendAsync(string path,object body)
    {
        var access=token();if(string.IsNullOrEmpty(access))throw new InvalidOperationException("로그인 연결을 확인해 주세요.");
        using var req=new HttpRequestMessage(HttpMethod.Post,url.TrimEnd('/')+path);
        req.Headers.Add("apikey",key);req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
        req.Content=JsonContent.Create(body);
        using var response=await Http.SendAsync(req);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("회원 정보를 확인하지 못했습니다. 번호와 연결 상태를 확인해 주세요.");
        return await response.Content.ReadAsStringAsync();
    }
    public async Task<LoyaltyMemberResult> EnrollAsync(string phone)=>JsonSerializer.Deserialize<LoyaltyMemberResult>(
        await SendAsync("/functions/v1/loyalty-member",new{phone})) ?? throw new InvalidOperationException("회원 응답을 확인할 수 없습니다.");
    public async Task<List<LoyaltyMember>> GetMembersAsync()=>JsonSerializer.Deserialize<List<LoyaltyMember>>(
        await SendAsync("/rest/v1/rpc/admin_list_loyalty_members",new{})) ?? new();
    public async Task RegisterAsync(string phone)=>await SendAsync("/rest/v1/rpc/admin_register_loyalty_member",new{p_phone=phone});
}
