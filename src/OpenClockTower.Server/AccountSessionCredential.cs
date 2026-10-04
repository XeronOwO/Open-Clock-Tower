namespace OpenClockTower.Server;

/// <summary>
/// 账号会话凭据（D-0021）：登录后签发，用于**认领席位**与账号自助（改名 / 找回）。
/// </summary>
/// <remarks>
/// 与连接凭据共用同一套秘密姿态（见 <see cref="SecretToken"/>）：32 字节密码学随机、base64url；
/// 服务端只存 SHA-256 哈希、固定时间比较；日志只写 <see cref="FingerprintOf"/> 的短指纹，
/// 明文不进日志、不落盘。它不是游戏命令凭据——认领完成后，游戏命令仍只认连接级凭据。
/// </remarks>
public readonly record struct AccountSessionCredential(string Value)
{
    /// <summary>生成一枚新的账号会话凭据。</summary>
    public static AccountSessionCredential CreateNew() => new(SecretToken.CreateNew());

    /// <summary>凭据的 SHA-256 哈希（存储与比较用；明文不落地）。</summary>
    public static byte[] HashOf(string value) => SecretToken.HashOf(value);

    /// <summary>短指纹（审计日志用；由哈希截断而来，不能据此反推凭据）。</summary>
    public static string FingerprintOf(string? value) => SecretToken.FingerprintOf(value);
}
