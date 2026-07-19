using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace EiTRVO.ProEngine.Models;

public enum AccountType
{
    Microsoft,   // 现有 Microsoft OAuth 账号（默认值 0，兼容旧数据）
    Yggdrasil    // 第三方 Yggdrasil 验证账号
}

public class Account : INotifyPropertyChanged
{
    private string _username = "";
    private string _uuid = "";
    private string _lastUsed = "";
    private string? _microsoftRefreshToken;
    private string? _yggdrasilServerUrl;
    private string? _yggdrasilEmail;
    private string? _yggdrasilAccessToken;
    private string? _yggdrasilClientToken;

    // === 类型标识 ===
    public AccountType Type { get; set; } = AccountType.Microsoft;

    // === 公共字段（两种账号共享）===

    [JsonPropertyName("username")]
    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    [JsonPropertyName("uuid")]
    public string UUID
    {
        get => _uuid;
        set { _uuid = value; OnPropertyChanged(); OnPropertyChanged(nameof(MaskedUUID)); }
    }

    [JsonPropertyName("lastUsed")]
    public string LastUsed
    {
        get => _lastUsed;
        set { _lastUsed = value; OnPropertyChanged(); OnPropertyChanged(nameof(LastUsedDisplay)); }
    }

    // === Microsoft 专用 ===

    /// <summary>
    /// Microsoft OAuth refresh token.
    /// Serialized as "refreshToken" to match the old MicrosoftAccount format
    /// so existing accounts.json files deserialize correctly.
    /// </summary>
    [JsonPropertyName("refreshToken")]
    public string? MicrosoftRefreshToken
    {
        get => _microsoftRefreshToken;
        set { _microsoftRefreshToken = value; OnPropertyChanged(); }
    }

    // === Yggdrasil 专用 ===

    public string? YggdrasilServerUrl
    {
        get => _yggdrasilServerUrl;
        set { _yggdrasilServerUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(AccountTypeLabel)); OnPropertyChanged(nameof(ServerDisplayName)); }
    }

    public string? YggdrasilEmail
    {
        get => _yggdrasilEmail;
        set { _yggdrasilEmail = value; OnPropertyChanged(); }
    }

    /// <summary>DPAPI-encrypted password (encrypted separately inside accounts.json).</summary>
    public string? YggdrasilEncryptedPassword { get; set; }

    public string? YggdrasilAccessToken
    {
        get => _yggdrasilAccessToken;
        set { _yggdrasilAccessToken = value; OnPropertyChanged(); }
    }

    public string? YggdrasilClientToken
    {
        get => _yggdrasilClientToken;
        set { _yggdrasilClientToken = value; OnPropertyChanged(); }
    }

    // === 计算属性（UI 绑定）===

    [JsonIgnore]
    public bool IsMicrosoftAccount => Type == AccountType.Microsoft;

    [JsonIgnore]
    public string DisplayText => Username;

    public override string ToString() => DisplayText;

    [JsonIgnore]
    public string MaskedUUID
    {
        get
        {
            if (string.IsNullOrEmpty(_uuid)) return "";
            if (_uuid.Length <= 8) return new string('*', _uuid.Length);
            return _uuid.Substring(0, 8) + new string('*', _uuid.Length - 8);
        }
    }

    [JsonIgnore]
    public string LastUsedDisplay => DateTime.TryParse(_lastUsed, out var dt)
        ? dt.ToString("yyyy-MM-dd HH:mm:ss")
        : _lastUsed;

    [JsonIgnore]
    public string AccountTypeLabel => Type switch
    {
        AccountType.Microsoft => "Microsoft",
        AccountType.Yggdrasil => ServerDisplayName,
        _ => "未知"
    };

    [JsonIgnore]
    public string ServerDisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_yggdrasilServerUrl))
                return "";

            try { return new Uri(_yggdrasilServerUrl).Host; }
            catch { return _yggdrasilServerUrl; }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
