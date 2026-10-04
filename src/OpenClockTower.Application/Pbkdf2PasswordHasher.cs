using System.Globalization;
using System.Security.Cryptography;

namespace OpenClockTower.Application;

/// <summary>
/// PBKDF2-SHA256 口令哈希器（D-0021）：16 字节随机盐、210000 次迭代、32 字节派生键、固定时间比较。
/// </summary>
/// <remarks>
/// 输出自描述串 <c>pbkdf2-sha256$迭代$盐$哈希</c>（盐与哈希为 base64），
/// 这样迭代次数与盐随哈希一起走，将来升参数也能逐个校验旧串、不需要"全局版本号"。
/// 口令明文只在本方法调用栈内出现，不写日志、不落盘。
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;

    /// <inheritdoc />
    public string Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
        return string.Join(
            '$',
            Prefix,
            Iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key));
    }

    /// <inheritdoc />
    public bool Verify(string secret, string storedHash)
    {
        if (secret is null || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 4
            || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
