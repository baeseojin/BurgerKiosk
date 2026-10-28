using System.Windows.Input;
using BurgerKiosk.Commands;
using BurgerKiosk.Services;

namespace BurgerKiosk.ViewModels;
public partial class MainViewModel
{
    public Func<string,Task<LoyaltyMemberResult>>? LoyaltyLookupAsync {get;set;}
    private string? _loyaltySelectionId;
    private string _loyaltyPhone="";
    private string _loyaltyMaskedPhone="";
    private long _loyaltyBalance;
    private bool _loyaltyNew;
    private int _loyaltyStep;
    private bool _loyaltyVisible;
    private string _loyaltyMessage="";
    public bool IsLoyaltyVisible {get=>_loyaltyVisible;private set{_loyaltyVisible=value;OnPropertyChanged(nameof(IsLoyaltyVisible));}}
    public bool IsLoyaltyChoice=>_loyaltyStep==0;
    public bool IsLoyaltyPhone=>_loyaltyStep==1;
    public bool IsLoyaltySummary=>_loyaltyStep==2;
    public bool CanUseLoyalty=>!IsSavingOrder;
    public string LoyaltyPhoneDisplay=>_loyaltyPhone.Length<=3?_loyaltyPhone:
        _loyaltyPhone.Length<=7?_loyaltyPhone.Insert(3,"-"):_loyaltyPhone.Insert(_loyaltyPhone.Length-4,"-").Insert(3,"-");
    public string LoyaltyBalanceText=>$"{_loyaltyMaskedPhone}  {_loyaltyBalance:N0}p";
    public string LoyaltyMemberNotice=>_loyaltyNew?"등록되지 않은 번호라 새 회원으로 등록했습니다.":"등록된 회원입니다.";
    public string LoyaltyExpectedText=>$"이번 결제 적립 예정  {TotalPrice/100:N0}p (1%)";
    public string LoyaltyMessage {get=>_loyaltyMessage;private set{_loyaltyMessage=value;OnPropertyChanged(nameof(LoyaltyMessage));}}
    public string CompletedLoyaltyMessage {get;private set;}="";
    private ICommand? _chooseLoyalty,_skipLoyalty,_phoneKey,_lookupLoyalty,_continueLoyalty,_cancelLoyalty;
    public ICommand ChooseLoyaltyCommand=>_chooseLoyalty??=new RelayCommand(_=>{if(IsSavingOrder)return;SetLoyaltyStep(1);});
    public ICommand SkipLoyaltyCommand=>_skipLoyalty??=new RelayCommand(async _=>{if(IsSavingOrder||!IsLoyaltyVisible)return;_loyaltySelectionId=null;await ContinueLoyaltyPaymentAsync();});
    public ICommand LoyaltyPhoneKeyCommand=>_phoneKey??=new RelayCommand(PhoneKey);
    public ICommand LookupLoyaltyCommand=>_lookupLoyalty??=new RelayCommand(async _=>await LookupLoyaltyAsync());
    public ICommand ContinueLoyaltyCommand=>_continueLoyalty??=new RelayCommand(async _=>{if(IsSavingOrder||!IsLoyaltySummary||_loyaltySelectionId==null)return;await ContinueLoyaltyPaymentAsync();});
    public ICommand CancelLoyaltyCommand=>_cancelLoyalty??=new RelayCommand(_=>{if(!IsSavingOrder)ResetLoyalty();});
    private void SetLoyaltyStep(int step)
    {
        _loyaltyStep=step;LoyaltyMessage="";
        foreach(var name in new[]{nameof(IsLoyaltyChoice),nameof(IsLoyaltyPhone),nameof(IsLoyaltySummary),nameof(LoyaltyBalanceText),nameof(LoyaltyMemberNotice),nameof(LoyaltyExpectedText)})OnPropertyChanged(name);
    }
    private bool BeginLoyaltyPrompt()
    {
        if(!IsPaymentVisible||!CanConfirmPayment||IsLoyaltyVisible)return false;
        ResetLoyalty();SetLoyaltyStep(0);IsLoyaltyVisible=true;return true;
    }
    private void ResetLoyalty()
    {
        IsLoyaltyVisible=false;_loyaltySelectionId=null;_loyaltyPhone="";_loyaltyMaskedPhone="";_loyaltyBalance=0;
        LoyaltyMessage="";CompletedLoyaltyMessage="";OnPropertyChanged(nameof(CompletedLoyaltyMessage));OnPropertyChanged(nameof(LoyaltyPhoneDisplay));
        _orderService.LoyaltySelectionId=null;
        if(KakaoPayReadyService!=null)KakaoPayReadyService.LoyaltySelectionId=null;
    }
    private void PhoneKey(object? parameter)
    {
        if(IsSavingOrder||!IsLoyaltyPhone)return;
        var key=parameter?.ToString()??"";
        if(key=="CLEAR")_loyaltyPhone="";
        else if(key=="BACK")_loyaltyPhone=_loyaltyPhone.Length>0?_loyaltyPhone[..^1]:"";
        else if(key.Length==1&&char.IsAsciiDigit(key[0])&&_loyaltyPhone.Length<11)_loyaltyPhone+=key;
        LoyaltyMessage="";OnPropertyChanged(nameof(LoyaltyPhoneDisplay));
    }
    private async Task LookupLoyaltyAsync()
    {
        if(IsSavingOrder||!IsLoyaltyVisible||!IsLoyaltyPhone)return;
        if(!System.Text.RegularExpressions.Regex.IsMatch(_loyaltyPhone,"^01[016789][0-9]{7,8}$"))
        {LoyaltyMessage="휴대전화번호 10~11자리를 입력해 주세요.";return;}
        IsSavingOrder=true;
        try
        {
            if(LoyaltyLookupAsync==null)throw new InvalidOperationException("회원 서비스가 연결되지 않았습니다.");
            var member=await LoyaltyLookupAsync(_loyaltyPhone);
            if(!Guid.TryParse(member.SelectionId,out _))throw new InvalidOperationException("회원 정보를 다시 확인해 주세요.");
            _loyaltySelectionId=member.SelectionId;_loyaltyMaskedPhone=member.MaskedPhone;_loyaltyBalance=member.Points;_loyaltyNew=member.IsNew;
            _loyaltyPhone="";OnPropertyChanged(nameof(LoyaltyPhoneDisplay));SetLoyaltyStep(2);
        }
        catch(Exception){LoyaltyMessage="회원 정보를 확인하지 못했습니다. 연결 상태를 확인하고 다시 시도해 주세요.";}
        finally{IsSavingOrder=false;}
    }
    private async Task ContinueLoyaltyPaymentAsync()
    {
        _orderService.LoyaltySelectionId=_loyaltySelectionId;
        if(KakaoPayReadyService!=null)KakaoPayReadyService.LoyaltySelectionId=_loyaltySelectionId;
        IsLoyaltyVisible=false;
        await ProcessPaymentAsync();
    }
    private void SetCompletedLoyalty(int amount)
    {
        CompletedLoyaltyMessage=_loyaltySelectionId!=null?$"{_loyaltyMaskedPhone}\n{amount/100:N0}p 적립 완료":"";
        OnPropertyChanged(nameof(CompletedLoyaltyMessage));
    }
}
