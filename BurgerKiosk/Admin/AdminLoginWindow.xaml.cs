using BurgerKiosk.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BurgerKiosk;
public partial class AdminLoginWindow : UserControl
{
    private readonly Func<string,string,System.Threading.Tasks.Task<bool>> _signIn;
    private readonly Action? _onDetached;
    private readonly bool _deviceRegistration;
    private Control _activeField = null!;
    private int _failedAttempts;
    private bool _upper;
    private bool _symbols;
    private bool _authenticating;
    private bool _detached;
    public event EventHandler? LoginSucceeded;
    public event EventHandler? CancelRequested;

    public AdminLoginWindow(AdminAuthService auth) : this(auth.SignInAndVerifyAsync, auth.SignOut) { }
    public AdminLoginWindow(Func<string,string,System.Threading.Tasks.Task<bool>> signIn, Action? onDetached=null, bool deviceRegistration=false)
    {
        InitializeComponent();
        _signIn=signIn;_onDetached=onDetached;_deviceRegistration=deviceRegistration;
        if(deviceRegistration)
        {
            LoginHeading.Text="이 키오스크 기기 등록";
            LoginHeading.FontSize=23;
            LoginCard.Padding=new Thickness(14,10,14,10);
            LoginDescription.Text="키오스크 기기 계정을 등록하면 다음 실행부터 자동 로그인됩니다. 관리자 계정과 다릅니다.";
            LoginDescription.Margin=new Thickness(0,4,0,10);
            ErrorText.MinHeight=18;ErrorText.Margin=new Thickness(0,2,0,4);
            LoginButton.Content="이 기기 등록";
        }
        _activeField=EmailBox;
        BuildKeyboard();
    }
    private void LoginView_Loaded(object sender,RoutedEventArgs e)
    {
        _detached=false;
        ActivateField(EmailBox);
    }
    private void LoginView_Unloaded(object sender,RoutedEventArgs e)
    {
        _detached=true;
        PasswordInput.Clear();
    }
    private void ActivateField(Control field)
    {
        _activeField=field;
        KeyboardTargetText.Text=field==EmailBox?"이메일 입력 중":"비밀번호 입력 중";
        NextButton.Content=field==EmailBox?"다음":_deviceRegistration?"등록":"로그인";
        EmailBox.BorderBrush=new SolidColorBrush(field==EmailBox?Color.FromRgb(236,107,48):Color.FromRgb(233,223,213));
        PasswordInput.BorderBrush=new SolidColorBrush(field==PasswordInput?Color.FromRgb(236,107,48):Color.FromRgb(233,223,213));
        if(field.IsVisible) field.Focus();
    }
    private void Input_GotKeyboardFocus(object sender,KeyboardFocusChangedEventArgs e)
    {
        if(sender is Control field && field!=_activeField) ActivateField(field);
    }
    private void BuildKeyboard()
    {
        KeyboardRows.Children.Clear();
        string[] rows=_symbols
            ? new[]{"!@#$%^&*()","-_+=[]{}\\|",";:'\",./?<>","~`"}
            : new[]{"1234567890","qwertyuiop","asdfghjkl","zxcvbnm"};
        foreach(var row in rows)
        {
            var grid=new UniformGrid {Columns=row.Length};
            foreach(char c in row)
            {
                string text=!_symbols && _upper?char.ToUpperInvariant(c).ToString():c.ToString();
                var key=new Button {Content=text,Tag=text,Style=(Style)FindResource("KeyboardKey")};
                key.Click+=QuickText_Click;
                grid.Children.Add(key);
            }
            KeyboardRows.Children.Add(grid);
        }
        UpperButton.Content=_upper?"소문자":"대문자";
        SymbolButton.Content=_symbols?"영문/숫자":"특수문자";
    }
    private void QuickText_Click(object sender,RoutedEventArgs e)
    {
        if(_authenticating || sender is not Button key || key.Tag is not string text) return;
        if(_activeField.IsVisible) _activeField.Focus();
        AdminTouchKeyboard.Insert(_activeField,text);
    }
    private void KeyboardAction_Click(object sender,RoutedEventArgs e)
    {
        if(_authenticating || sender is not Button key) return;
        switch(key.Tag as string)
        {
            case "EMAIL": ActivateField(EmailBox);return;
            case "PASSWORD": ActivateField(PasswordInput);return;
            case "UPPER": _upper=!_upper;BuildKeyboard();return;
            case "SYMBOL": _symbols=!_symbols;BuildKeyboard();return;
            case "NEXT":
                if(_activeField==EmailBox) ActivateField(PasswordInput);
                else LoginButton_Click(LoginButton,e);
                return;
        }
        if(_activeField.IsVisible) _activeField.Focus();
        switch(key.Tag as string)
        {
            case "SPACE": AdminTouchKeyboard.Insert(_activeField," ");break;
            case "BACK": AdminTouchKeyboard.Backspace(_activeField);break;
            case "CLEAR": AdminTouchKeyboard.Clear(_activeField);break;
            case "LEFT": AdminTouchKeyboard.Left(_activeField);break;
            case "RIGHT": AdminTouchKeyboard.Right(_activeField);break;
        }
    }
    private async void LoginButton_Click(object sender,RoutedEventArgs e)
    {
        if(_authenticating) return;
        string email=EmailBox.Text.Trim();
        string password=PasswordInput.Password;
        if(string.IsNullOrWhiteSpace(email)||string.IsNullOrEmpty(password))
        {
            ErrorText.Text="이메일과 비밀번호를 입력하세요.";
            ActivateField(string.IsNullOrWhiteSpace(email)?EmailBox:PasswordInput);
            return;
        }
        _authenticating=true;
        LoginButton.IsEnabled=CancelButton.IsEnabled=KeyboardPanel.IsEnabled=EmailBox.IsEnabled=PasswordInput.IsEnabled=false;
        ErrorText.Text="인증 중...";
        try
        {
            if(await _signIn(email,password))
            {
                PasswordInput.Clear();
                if(!_detached) LoginSucceeded?.Invoke(this,EventArgs.Empty);
                else _onDetached?.Invoke();
                return;
            }
            if(_detached) return;
            _failedAttempts++;
            ErrorText.Text=_deviceRegistration?"계정 정보가 올바르지 않거나 등록된 활성 키오스크 계정이 아닙니다.":"계정 정보가 올바르지 않거나 관리자 권한이 없습니다.";
            if(_failedAttempts>=5)
            {
                MessageBox.Show("로그인 시도가 반복되었습니다. 잠시 후 다시 시도하세요.","관리자 로그인");
                CancelRequested?.Invoke(this,EventArgs.Empty);
            }
        }
        catch(InvalidOperationException ex) when(_deviceRegistration)
        {
            if(!_detached) ErrorText.Text=ex.Message;
        }
        catch(Exception)
        {
            if(!_detached) ErrorText.Text="인증 서버에 연결하지 못했습니다. 네트워크를 확인하세요.";
        }
        finally
        {
            PasswordInput.Clear();
            _authenticating=false;
            LoginButton.IsEnabled=CancelButton.IsEnabled=KeyboardPanel.IsEnabled=EmailBox.IsEnabled=PasswordInput.IsEnabled=true;
        }
    }
    private void CancelButton_Click(object sender,RoutedEventArgs e)
    {
        if(_authenticating) return;
        PasswordInput.Clear();
        CancelRequested?.Invoke(this,EventArgs.Empty);
    }
}
