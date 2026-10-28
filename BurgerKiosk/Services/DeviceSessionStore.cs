using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace BurgerKiosk.Services;

// Only a verified device session is persisted. Passwords are never serialized.
public sealed class DeviceSessionStore : IGotrueSessionPersistence<Session>
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly byte[] _entropy;
    private bool _saveEnabled;
    private Session? _memorySession;
    public string? LastError { get; private set; }
    public bool Exists => File.Exists(_path);
    public DeviceSessionStore(string projectUrl, string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BurgerKiosk", "device-session.dat");
        _entropy = SHA256.HashData(Encoding.UTF8.GetBytes("BurgerKiosk.DeviceSession.v1|" + projectUrl.TrimEnd('/')));
    }
    public Session? LoadSession()
    {
        lock (_sync)
        {
            if (_memorySession != null) return _memorySession;
            if (!File.Exists(_path)) return null;
            try
            {
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), _entropy, DataProtectionScope.CurrentUser);
                try
                {
                    var session = JsonSerializer.Deserialize<Session>(plain);
                    if (string.IsNullOrWhiteSpace(session?.AccessToken) || string.IsNullOrWhiteSpace(session.RefreshToken)) throw new InvalidDataException();
                    _saveEnabled = true; _memorySession = session; LastError = null; return session;
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            catch (Exception) { LastError = "저장된 기기 정보를 읽지 못했습니다. 기기 계정을 다시 등록해 주세요."; return null; }
        }
    }
    private void Write(Session session)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            byte[] encrypted = ProtectedData.Protect(plain, _entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            using (var file = new FileStream(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            { file.Write(encrypted); file.Flush(true); }
            File.Move(_path + ".tmp", _path, true); LastError = null;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void SaveSession(Session session)
    {
        lock (_sync)
        {
            _memorySession = session;
            if (!_saveEnabled) return;
            try { Write(session); }
            catch (Exception) { LastError = "자동 로그인 정보 저장에 실패했습니다. 저장 공간과 권한을 확인해 주세요."; }
        }
    }
    public void CommitVerifiedSession(Session session)
    {
        lock (_sync)
        {
            Write(session); _memorySession = session; _saveEnabled = true;
        }
    }
    public void SuspendWrites() { lock (_sync) _saveEnabled = false; }
    public void DestroySession()
    {
        lock (_sync)
        {
            _memorySession = null;
            if (!_saveEnabled) return;
            try { File.Delete(_path); _saveEnabled = false; }
            catch (Exception) { LastError = "저장된 기기 정보를 정리하지 못했습니다."; }
        }
    }
}
