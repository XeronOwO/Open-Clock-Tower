using System.Security.Cryptography;

namespace OpenClockTower.Application;

/// <summary>
/// 账号自助（D-0021）：注册 / 登录校验 / 改玩家名 / 恢复码重置。
/// </summary>
/// <remarks>
/// <para>
/// 这里不碰游戏状态、不进四道闸：账号是"你是谁"的全局身份；席位与游戏命令仍由既有凭据链判定。
/// 账号自助改的只是账号与会话数据——领域规则在本类，凭据与推送在 Server 层，各管一段。
/// </para>
/// <para>
/// 秘密姿态：口令与恢复码只以哈希形式进入存储；明文只在下发它的那一次结果里出现，
/// 调用方（AccountHub）必须只把它写回给当事连接，绝不写日志、不落盘。
/// </para>
/// </remarks>
public sealed class AccountService
{
    private readonly IAccountStore _accounts;
    private readonly IPasswordHasher _hasher;

    /// <summary>登录名不存在时也走一次哈希校验，避免"快 = 不存在、慢 = 存在"的时序枚举。</summary>
    private readonly Lazy<string> _dummyHash;

    /// <summary>构造账号服务。</summary>
    public AccountService(IAccountStore accounts, IPasswordHasher hasher)
    {
        _accounts = accounts;
        _hasher = hasher;
        _dummyHash = new Lazy<string>(() => hasher.Hash("account-service-dummy-secret"));
    }

    /// <summary>按标识取账号（认领席位读玩家名、改名同步用）；没有返回 null。</summary>
    public Task<Account?> FindAsync(AccountId accountId, CancellationToken cancellationToken) =>
        _accounts.FindByIdAsync(accountId, cancellationToken);

    /// <summary>注册：登录名唯一（大小写不敏感）、玩家名有界、口令有最低强度；成功后返回一次性恢复码。</summary>
    public async Task<AccountOutcome> RegisterAsync(
        string? username,
        string? displayName,
        string? password,
        CancellationToken cancellationToken)
    {
        if (!UsernameText.TryNormalize(username, out var normalizedUsername, out var usernameFailure))
        {
            return Reject("invalid_username", UsernameFailureMessage(usernameFailure));
        }

        if (!DisplayNameText.TryNormalize(displayName, out var normalizedDisplayName, out var displayNameFailure))
        {
            return Reject("invalid_display_name", DisplayNameFailureMessage(displayNameFailure));
        }

        if (!PasswordPolicy.TryValidate(password, out var passwordFailure))
        {
            return Reject("invalid_password", PasswordFailureMessage(passwordFailure));
        }

        var recoveryCode = CreateRecoveryCode();
        var created = await _accounts.TryCreateAsync(
            new NewAccount
            {
                Username = normalizedUsername,
                DisplayName = normalizedDisplayName,
                PasswordHash = _hasher.Hash(password!),
                RecoveryCodeHash = _hasher.Hash(recoveryCode),
            },
            cancellationToken);

        if (created is null)
        {
            return Reject("username_taken", "这个登录名已经被占用");
        }

        return new AccountOutcome
        {
            Accepted = true,
            Code = "ok",
            Account = created,
            RecoveryCode = recoveryCode,
        };
    }

    /// <summary>登录校验：失败一律收敛成"凭据无效"，不区分登录名不存在与口令不对。</summary>
    public async Task<AccountOutcome> AuthenticateAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var secret = password ?? string.Empty;
        if (!UsernameText.TryNormalize(username, out var normalizedUsername, out _) || secret.Length == 0)
        {
            _hasher.Verify(secret, _dummyHash.Value);
            return InvalidCredentials();
        }

        var account = await _accounts.FindByUsernameAsync(normalizedUsername, cancellationToken);
        if (account is null)
        {
            _hasher.Verify(secret, _dummyHash.Value);
            return InvalidCredentials();
        }

        if (!_hasher.Verify(secret, account.PasswordHash))
        {
            return InvalidCredentials();
        }

        return new AccountOutcome { Accepted = true, Code = "ok", Account = account };
    }

    /// <summary>改玩家名：账号设置里随时可改；运行期由 Server 推给该账号已绑定的席位。</summary>
    public async Task<AccountOutcome> ChangeDisplayNameAsync(
        AccountId accountId,
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (!DisplayNameText.TryNormalize(displayName, out var normalized, out var failure))
        {
            return Reject("invalid_display_name", DisplayNameFailureMessage(failure));
        }

        var account = await _accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return Reject("account_missing", "账号不存在");
        }

        var updated = account with { DisplayName = normalized };
        if (!await _accounts.TryUpdateAsync(updated, cancellationToken))
        {
            return Reject("account_missing", "账号不存在");
        }

        return new AccountOutcome { Accepted = true, Code = "ok", Account = updated };
    }

    /// <summary>用一次性恢复码重置口令；成功后**轮换**恢复码，并把新的明文下发一次。</summary>
    public async Task<AccountOutcome> ResetPasswordAsync(
        string? username,
        string? recoveryCode,
        string? newPassword,
        CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.TryValidate(newPassword, out var passwordFailure))
        {
            return Reject("invalid_password", PasswordFailureMessage(passwordFailure));
        }

        var presented = recoveryCode ?? string.Empty;
        if (!UsernameText.TryNormalize(username, out var normalizedUsername, out _) || presented.Length == 0)
        {
            _hasher.Verify(presented, _dummyHash.Value);
            return InvalidRecovery();
        }

        var account = await _accounts.FindByUsernameAsync(normalizedUsername, cancellationToken);
        if (account is null || account.RecoveryCodeHash is null)
        {
            _hasher.Verify(presented, _dummyHash.Value);
            return InvalidRecovery();
        }

        if (!_hasher.Verify(presented, account.RecoveryCodeHash))
        {
            return InvalidRecovery();
        }

        var rotated = CreateRecoveryCode();
        var updated = account with
        {
            PasswordHash = _hasher.Hash(newPassword!),
            RecoveryCodeHash = _hasher.Hash(rotated),
        };

        if (!await _accounts.TryUpdateAsync(updated, cancellationToken))
        {
            return Reject("account_missing", "账号不存在");
        }

        return new AccountOutcome
        {
            Accepted = true,
            Code = "ok",
            Account = updated,
            RecoveryCode = rotated,
        };
    }

    /// <summary>口令重置后轮换恢复码，明文只在下发它的那一次结果里出现。</summary>
    private static string CreateRecoveryCode() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static AccountOutcome Reject(string code, string message) => new()
    {
        Accepted = false,
        Code = code,
        Message = message,
    };

    private static AccountOutcome InvalidCredentials() => Reject("invalid_credentials", "登录名或口令不正确");

    private static AccountOutcome InvalidRecovery() => Reject("invalid_recovery", "恢复码无效");

    private static string UsernameFailureMessage(string failure) => failure switch
    {
        "too_short" => $"登录名至少 {UsernameText.MinLength} 个字符",
        "too_long" => $"登录名最多 {UsernameText.MaxLength} 个字符",
        "whitespace" => "登录名不能含空白",
        "control" => "登录名不能含控制字符",
        _ => "登录名不能为空",
    };

    private static string DisplayNameFailureMessage(string failure) => failure switch
    {
        "too_long" => $"玩家名最多 {DisplayNameText.MaxLength} 个字符",
        "control" => "玩家名不能含控制字符",
        _ => "玩家名不能为空",
    };

    private static string PasswordFailureMessage(string failure) => failure switch
    {
        "too_short" => $"口令至少 {PasswordPolicy.MinLength} 个字符",
        "too_long" => $"口令最多 {PasswordPolicy.MaxLength} 个字符",
        "control" => "口令不能含控制字符",
        _ => "口令不能为空",
    };
}
