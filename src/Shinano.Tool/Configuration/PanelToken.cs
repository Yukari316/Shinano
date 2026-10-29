using System.Security.Cryptography;

namespace Shinano.Tool.Configuration;

/// <summary>面板登录 Token 的生成，长度与字符集与宿主一致；宿主遇到长度不符的 Token 会重新生成。</summary>
public static class PanelToken
{
    /// <summary>Token 长度。</summary>
    public const int LENGTH = 64;

    private const string ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>生成新 Token。</summary>
    public static string Generate() => RandomNumberGenerator.GetString(ALPHABET, LENGTH);

    /// <summary>现有 Token 是否会被宿主沿用。</summary>
    public static bool IsValid(string? token) => token is { Length: LENGTH };
}
