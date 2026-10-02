using System.Security.Cryptography;
using System.Text;

namespace OpenClockTower.Server;

/// <summary>
/// 连接级私有凭据（D-0012 §4.1）：加入时下发，只在**下发它的那条连接**上有效；重连必须重新出示票据。
/// </summary>
/// <remarks>
/// <para>
/// 随机来源于 <see cref="RandomNumberGenerator"/>（32 字节 / 256 bit，base64url 无填充）；
/// 服务端只在内存里保存它的 SHA-256 哈希（见 <see cref="ConnectionCredentialRecord"/>），
/// 比较用固定时间算法——客户端被完全攻陷也无法靠时序猜出凭据。
/// </para>
/// <para>
/// 凭据是**秘密**：不渲染、不进日志、不落盘；日志只允许写 <see cref="FingerprintOf"/> 的短指纹。
/// </para>
/// </remarks>
public readonly record struct ConnectionCredential(string Value)
{
    /// <summary>生成一枚新的连接凭据。</summary>
    public static ConnectionCredential CreateNew() =>
        new(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('='));

    /// <summary>凭据的 SHA-256 哈希（存储与比较用；明文不落地）。</summary>
    public static byte[] HashOf(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    /// <summary>短指纹（审计日志用；由哈希截断而来，不能据此反推凭据）。</summary>
    public static string FingerprintOf(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "无";
        }

        return Convert.ToHexString(HashOf(value))[..12].ToLowerInvariant();
    }
}
