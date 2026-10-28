using System.Windows;
using System.Windows.Controls;
using BurgerKiosk.Services;

namespace BurgerKiosk;
public partial class DeviceRegistrationWindow : Window
{
    private readonly SupabaseService _service;
    private readonly bool _forceRegistration;
    private bool _closed;
    private bool _working;
    public DeviceRegistrationWindow(SupabaseService service, bool forceRegistration=false)
    {
        InitializeComponent();_service=service;_forceRegistration=forceRegistration;
        Loaded+=OnLoaded;Closed+=(_,_)=>_closed=true;
    }
    private async void OnLoaded(object sender,RoutedEventArgs e)
    {
        Loaded-=OnLoaded;
        if(_forceRegistration)
        {
            try {await _service.InitializeAsync();}
            catch(Exception){ }
            if(!_closed)ShowRegistration("이 기기에서 사용할 키오스크 계정을 등록해 주세요.");
            return;
        }
        await RestoreAsync();
    }
    private async System.Threading.Tasks.Task RestoreAsync()
    {
        if(_working||_closed)return;
        _working=true;
        RegistrationHost.Content=new TextBlock{Text="자동 로그인 중…",FontSize=23,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        StatusText.Text="기기 계정과 연결 상태를 확인하고 있습니다.";
        try
        {
            if(await _service.RestoreRegisteredDeviceAsync())
            {if(!_closed)DialogResult=true;return;}
            if(!_closed)ShowRegistration("최초 실행이거나 기기 계정 확인이 필요합니다. 아래에서 등록해 주세요.");
        }
        catch(Exception)
        {
            if(!_closed)ShowRegistration("자동 로그인에 실패했습니다. 네트워크를 확인하고 재시도하거나 기기 계정을 다시 등록해 주세요.");
        }
        finally{_working=false;}
    }
    private void ShowRegistration(string message)
    {
        StatusText.Text=message;
        var form=new AdminLoginWindow(async (email,password)=>
        {
            if(_working||_closed)return false;
            _working=true;
            try
            {
            bool verified;
            try {verified=await _service.RegisterDeviceAsync(email,password);}
            catch(Supabase.Gotrue.Exceptions.GotrueException ex) when(ex.StatusCode is 400 or 401 or 403){return false;}
            if(!verified||_closed)return false;
            _service.SaveRegisteredDevice();return true;
            }
            finally{_working=false;}
        },deviceRegistration:true);
        form.LoginSucceeded+=(_,_)=>{if(!_closed)DialogResult=true;};
        form.CancelRequested+=(_,_)=>{if(!_closed)DialogResult=false;};
        var panel=new DockPanel();
        var retry=new Button{Content="저장된 기기로 자동 로그인 재시도",MinHeight=40,Margin=new Thickness(12,0,12,6)};
        retry.Click+=async(_,_)=>{if(_working)return;retry.IsEnabled=false;await RestoreAsync();};
        DockPanel.SetDock(retry,Dock.Bottom);panel.Children.Add(retry);panel.Children.Add(form);
        RegistrationHost.Content=panel;
    }
}
