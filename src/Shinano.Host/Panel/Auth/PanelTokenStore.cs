using System.Security.Cryptography;
using System.Text;
using Shinano.Host.Configuration;

namespace Shinano.Host.Panel.Auth;

/// <summary>
/// 面板登录 Token：保存在本地配置 <c>Shinano:Web:Token</c>，缺失时生成并写入。Token 不写入日志，也不下发给前端，
/// 只有重新生成时在响应中返回一次。会话中记录 Token 指纹，Token 改变后全部现有会话失效。
/// </summary>
public sealed class PanelTokenStore
{
    /// <summary>Token 长度。</summary>
    public const int TOKEN_LENGTH = 64;

    private const string ALPHABET            = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int    FINGERPRINT_LENGTH = 32;

    private static readonly string[] TokenPath = ["Shinano", "Web", "Token"];

    private readonly LocalConfigFile          _file;
    private readonly ILogger<PanelTokenStore> _logger;
    private readonly Lock                     _lock = new();
    private          string                   _token;

    /// <summary>读取 Token，缺失时生成并写入本地配置。</summary>
    public PanelTokenStore(LocalConfigFile file, ILogger<PanelTokenStore> logger)
    {
        _file   = file;
        _logger = logger;
        string? existing = file.ReadString(TokenPath);
        if (existing is { Length: TOKEN_LENGTH })
        {
            _token = existing;
            return;
        }

        _token = Save(Generate());
        _logger.LogWarning("本地配置中没有有效的面板 Token，已生成新 Token 并写入本地配置文件");
    }

    /// <summary>当前 Token 的指纹，写入会话用于判断 Token 是否已更换。</summary>
    public string Fingerprint
    {
        get
        {
            lock (_lock)
            {
                return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_token)))[..FINGERPRINT_LENGTH];
            }
        }
    }

    /// <summary>以固定时间比较判断 <paramref name="candidate" /> 是否为当前 Token。</summary>
    public bool Verify(string candidate)
    {
        lock (_lock)
        {
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(_token));
        }
    }

    /// <summary>生成新 Token 并写入本地配置，全部现有会话随之失效。</summary>
    /// <returns>新 Token，只应向调用者显示一次。</returns>
    public string Regenerate()
    {
        string token = Save(Generate());
        _logger.LogWarning("面板 Token 已重新生成，现有会话全部失效");
        return token;
    }

    private string Save(string token)
    {
        _file.Update(root => LocalConfigFile.Section(root, TokenPath[..^1])[TokenPath[^1]] = token);
        lock (_lock)
        {
            _token = token;
        }

        return token;
    }

    private static string Generate() => RandomNumberGenerator.GetString(ALPHABET, TOKEN_LENGTH);
}
