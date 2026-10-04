using OpenClockTower.Application;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 账号基座（D-0021）：文本口径、PBKDF2 哈希器与账号自助的领域行为。
/// </summary>
/// <remarks>
/// 这些是 Application 层的纯逻辑，不需要真宿主；用内存假存储验证"注册 / 登录 / 改名 / 找回"的
/// 通过路径与**拒绝路径**（唯一性、大小写、枚举防护、恢复码轮换）。
/// </remarks>
public sealed class AccountFoundationTests
{
    /// <summary>登录名口径：合法输入归一化，非法输入给出机器可读原因。</summary>
    [Theory]
    [InlineData("aaa", "aaa", null)]
    [InlineData("  aaa  ", "aaa", null)]
    [InlineData("小明", "小明", null)]
    [InlineData("a b", null, "whitespace")]
    [InlineData("a\tb", null, "control")]
    [InlineData("a", null, "too_short")]
    [InlineData("", null, "empty")]
    [InlineData("   ", null, "empty")]
    public void UsernameText_NormalizesAndRejects(string raw, string? expected, string? failure)
    {
        var accepted = UsernameText.TryNormalize(raw, out var normalized, out var actualFailure);

        Assert.Equal(failure is null, accepted);
        if (failure is null)
        {
            Assert.Equal(expected, normalized);
        }
        else
        {
            Assert.Equal(failure, actualFailure);
        }
    }

    /// <summary>登录名超长（25 字符）被拒；唯一性键大小写不敏感。</summary>
    [Fact]
    public void UsernameText_EnforcesLength_AndCaseInsensitiveKey()
    {
        Assert.False(UsernameText.TryNormalize(new string('a', 25), out _, out var failure));
        Assert.Equal("too_long", failure);

        Assert.Equal(
            UsernameText.ComparisonKeyOf("Alice"),
            UsernameText.ComparisonKeyOf("alice"));
    }

    /// <summary>玩家名口径：折叠空白 / 换行、拒绝控制字符与超长、允许重名（不做唯一性判定）。</summary>
    [Theory]
    [InlineData("  aaa  ", "aaa", null)]
    [InlineData("小\n明", "小 明", null)]
    [InlineData("a\u0000b", null, "control")]
    [InlineData("", null, "empty")]
    [InlineData("   ", null, "empty")]
    public void DisplayNameText_NormalizesAndRejects(string raw, string? expected, string? failure)
    {
        var accepted = DisplayNameText.TryNormalize(raw, out var normalized, out var actualFailure);

        Assert.Equal(failure is null, accepted);
        if (failure is null)
        {
            Assert.Equal(expected, normalized);
        }
        else
        {
            Assert.Equal(failure, actualFailure);
        }
    }

    /// <summary>玩家名 25 字符超限被拒；同名的两个账号都能通过（姓名是标签不是凭据）。</summary>
    [Fact]
    public void DisplayNameText_AllowsDuplicates_RejectsTooLong()
    {
        Assert.False(DisplayNameText.TryNormalize(new string('名', 25), out _, out var failure));
        Assert.Equal("too_long", failure);

        Assert.True(DisplayNameText.TryNormalize("aaa", out var first, out _));
        Assert.True(DisplayNameText.TryNormalize("aaa", out var second, out _));
        Assert.Equal(first, second);
    }

    /// <summary>口令口径：8–128 字符、拒绝控制字符；空与过短给出原因。</summary>
    [Theory]
    [InlineData("12345678", null)]
    [InlineData("1234567", "too_short")]
    [InlineData("", "empty")]
    [InlineData("1234\u00075678", "control")]
    public void PasswordPolicy_Validates(string raw, string? failure)
    {
        var accepted = PasswordPolicy.TryValidate(raw, out var actualFailure);

        Assert.Equal(failure is null, accepted);
        Assert.Equal(failure ?? string.Empty, actualFailure);
    }

    /// <summary>PBKDF2 哈希器：同口令两次哈希因随机盐不同而不同；校验正确、拒绝错误与畸形串。</summary>
    [Fact]
    public void Pbkdf2PasswordHasher_HashesWithRandomSalt_AndVerifies()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var first = hasher.Hash("正确口令-123");
        var second = hasher.Hash("正确口令-123");

        Assert.NotEqual(first, second);
        Assert.StartsWith("pbkdf2-sha256$", first, StringComparison.Ordinal);
        Assert.True(hasher.Verify("正确口令-123", first));
        Assert.True(hasher.Verify("正确口令-123", second));
        Assert.False(hasher.Verify("错误口令-123", first));
        Assert.False(hasher.Verify("正确口令-123", "不是哈希串"));
        Assert.False(hasher.Verify("正确口令-123", string.Empty));
    }

    /// <summary>注册成功返回一次性恢复码；登录名重复（大小写不敏感）被拒且不落库。</summary>
    [Fact]
    public async Task Register_ReturnsRecoveryCode_AndRejectsDuplicateUsernameCaseInsensitively()
    {
        var store = new FakeAccountStore();
        var service = new AccountService(store, new Pbkdf2PasswordHasher());

        var first = await service.RegisterAsync("Alice", "爱丽丝", "password-123", CancellationToken.None);
        Assert.True(first.Accepted);
        Assert.Equal("ok", first.Code);
        Assert.NotNull(first.Account);
        Assert.Equal("Alice", first.Account!.Username);
        Assert.Equal("爱丽丝", first.Account.DisplayName);
        Assert.False(string.IsNullOrEmpty(first.RecoveryCode));
        Assert.NotEqual(first.RecoveryCode, first.Account.RecoveryCodeHash);

        var duplicate = await service.RegisterAsync("alice", "另一个", "password-123", CancellationToken.None);
        Assert.False(duplicate.Accepted);
        Assert.Equal("username_taken", duplicate.Code);
        Assert.Single(store.Accounts);
    }

    /// <summary>注册的非法输入在落库前就被拒：登录名形状、玩家名形状、口令强度。</summary>
    [Fact]
    public async Task Register_RejectsInvalidInput_BeforeTouchingStore()
    {
        var store = new FakeAccountStore();
        var service = new AccountService(store, new Pbkdf2PasswordHasher());

        Assert.Equal("invalid_username", (await service.RegisterAsync("a b", "名字", "password-123", CancellationToken.None)).Code);
        Assert.Equal("invalid_display_name", (await service.RegisterAsync("aaa", " \u0000 ", "password-123", CancellationToken.None)).Code);
        Assert.Equal("invalid_password", (await service.RegisterAsync("aaa", "名字", "short", CancellationToken.None)).Code);
        Assert.Empty(store.Accounts);
    }

    /// <summary>登录：正确口令通过；错误口令与不存在的登录名收敛成同一个中性码（不做账号枚举）。</summary>
    [Fact]
    public async Task Authenticate_IsNeutral_ForWrongPasswordAndUnknownUser()
    {
        var store = new FakeAccountStore();
        var service = new AccountService(store, new Pbkdf2PasswordHasher());
        await service.RegisterAsync("Alice", "爱丽丝", "password-123", CancellationToken.None);

        var ok = await service.AuthenticateAsync("alice", "password-123", CancellationToken.None);
        Assert.True(ok.Accepted);
        Assert.Equal("Alice", ok.Account!.Username);

        var wrongPassword = await service.AuthenticateAsync("Alice", "password-456", CancellationToken.None);
        var unknownUser = await service.AuthenticateAsync("Nobody", "password-123", CancellationToken.None);

        Assert.False(wrongPassword.Accepted);
        Assert.False(unknownUser.Accepted);
        Assert.Equal("invalid_credentials", wrongPassword.Code);
        Assert.Equal("invalid_credentials", unknownUser.Code);
        Assert.Equal(wrongPassword.Message, unknownUser.Message);
    }

    /// <summary>改玩家名：合法即改；非法被拒且账号保持原样。</summary>
    [Fact]
    public async Task ChangeDisplayName_UpdatesAccount_AndRejectsInvalidName()
    {
        var store = new FakeAccountStore();
        var service = new AccountService(store, new Pbkdf2PasswordHasher());
        var registered = await service.RegisterAsync("Alice", "爱丽丝", "password-123", CancellationToken.None);

        var changed = await service.ChangeDisplayNameAsync(registered.Account!.Id, "爱丽丝 2", CancellationToken.None);
        Assert.True(changed.Accepted);
        Assert.Equal("爱丽丝 2", changed.Account!.DisplayName);

        var rejected = await service.ChangeDisplayNameAsync(registered.Account.Id, " ", CancellationToken.None);
        Assert.False(rejected.Accepted);
        Assert.Equal("invalid_display_name", rejected.Code);
        Assert.Equal("爱丽丝 2", (await store.FindByIdAsync(registered.Account.Id, CancellationToken.None))!.DisplayName);
    }

    /// <summary>找回：恢复码重置口令后旧口令失效、新口令可用；恢复码轮换，旧的不能再用。</summary>
    [Fact]
    public async Task ResetPassword_RotatesRecoveryCode_AndOldSecretsStopWorking()
    {
        var store = new FakeAccountStore();
        var service = new AccountService(store, new Pbkdf2PasswordHasher());
        var registered = await service.RegisterAsync("Alice", "爱丽丝", "password-123", CancellationToken.None);
        var originalCode = registered.RecoveryCode!;

        var missing = await service.ResetPasswordAsync("Alice", "wrong-code", "password-456", CancellationToken.None);
        Assert.False(missing.Accepted);
        Assert.Equal("invalid_recovery", missing.Code);

        var reset = await service.ResetPasswordAsync("Alice", originalCode, "password-456", CancellationToken.None);
        Assert.True(reset.Accepted);
        Assert.NotNull(reset.RecoveryCode);
        Assert.NotEqual(originalCode, reset.RecoveryCode);

        Assert.False((await service.AuthenticateAsync("Alice", "password-123", CancellationToken.None)).Accepted);
        Assert.True((await service.AuthenticateAsync("Alice", "password-456", CancellationToken.None)).Accepted);
        Assert.False((await service.ResetPasswordAsync("Alice", originalCode, "password-789", CancellationToken.None)).Accepted);
    }

    /// <summary>内存账号表：只给测试用，语义（唯一性键、更新）与真实存储保持一致。</summary>
    private sealed class FakeAccountStore : IAccountStore
    {
        private readonly Dictionary<string, Account> _byUsernameKey = new(StringComparer.Ordinal);
        private readonly Dictionary<AccountId, Account> _byId = [];
        private int _nextId = 1;

        /// <summary>当前账号集合（测试断言用）。</summary>
        public IReadOnlyCollection<Account> Accounts => _byId.Values;

        /// <inheritdoc />
        public Task<Account?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(_byUsernameKey.GetValueOrDefault(UsernameText.ComparisonKeyOf(username)));

        /// <inheritdoc />
        public Task<Account?> FindByIdAsync(AccountId id, CancellationToken cancellationToken) =>
            Task.FromResult(_byId.GetValueOrDefault(id));

        /// <inheritdoc />
        public Task<Account?> TryCreateAsync(NewAccount account, CancellationToken cancellationToken)
        {
            var key = UsernameText.ComparisonKeyOf(account.Username);
            if (_byUsernameKey.ContainsKey(key))
            {
                return Task.FromResult<Account?>(null);
            }

            var created = new Account
            {
                Id = new AccountId(_nextId++),
                Username = account.Username,
                DisplayName = account.DisplayName,
                PasswordHash = account.PasswordHash,
                RecoveryCodeHash = account.RecoveryCodeHash,
            };
            _byUsernameKey[key] = created;
            _byId[created.Id] = created;
            return Task.FromResult<Account?>(created);
        }

        /// <inheritdoc />
        public Task<bool> TryUpdateAsync(Account account, CancellationToken cancellationToken)
        {
            if (!_byId.ContainsKey(account.Id))
            {
                return Task.FromResult(false);
            }

            _byId[account.Id] = account;
            _byUsernameKey[UsernameText.ComparisonKeyOf(account.Username)] = account;
            return Task.FromResult(true);
        }
    }
}
