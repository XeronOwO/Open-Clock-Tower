using System.Security.Cryptography;
using System.Text;

namespace OpenClockTower.Server;

/// <summary>
/// 秘密令牌的公共零件（D-0012 / D-0021）：连接凭据与账号会话凭据共用同一套
/// 密码学随机、只存哈希、固定时间比较与短指纹口径。
/// </summary>
/// <remarks>
/// 明文只在签发那一刻出现在内存里；本类不缓存明文、不写日志。上层（凭据记录 / 会话注册表）
/// 只保存 <see cref="HashOf"/> 的结果，比较一律走 <see cref="CryptographicOperations.FixedTimeEquals"/>。
/// </remarks>
internal static class SecretToken
{
    /// <summary>生成一枚 32 字节（256 bit）密码学随机的 base64url 令牌。</summary>
    public static string CreateNew() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>令牌的 SHA-256 哈希（存储与比较用；明文不落地）。</summary>
    public static byte[] HashOf(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    /// <summary>短指纹（审计日志用；由哈希截断而来，不能据此反推令牌）。</summary>
    public static string FingerprintOf(string? value) =>
        string.IsNullOrEmpty(value)
            ? "无"
            : Convert.ToHexString(HashOf(value))[..12].ToLowerInvariant();
}
