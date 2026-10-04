namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：连接级凭据的**秘密姿态**（D-0012 §4.1）——密码学随机、只存哈希、固定时间比较、不进日志、不落盘。
/// </summary>
/// <remarks>
/// 这些约束一旦被"顺手改回去"（用 Guid 造凭据、把明文存进字典、把凭据塞进日志模板），
/// 编译与普通测试都不会自然变红——所以把它们写成会失败的形状检查。
/// </remarks>
public sealed class CredentialSecurityGateTests
{
    /// <summary>凭据必须是密码学随机值：可猜的 Guid / 时间戳都不合格。</summary>
    [Fact]
    public void Credential_IsCryptographicallyRandom_NotGuessable()
    {
        // 随机生成收在共享零件里：连接凭据与账号会话凭据都只做委托，不许自己造随机。
        var factory = SourceText.StripCommentsAndLiterals(
            File.ReadAllText(RepositoryLayout.PathOf("src", "OpenClockTower.Server", "SecretToken.cs")));
        Assert.Contains("RandomNumberGenerator.GetBytes", factory, StringComparison.Ordinal);

        foreach (var file in new[] { "ConnectionCredential.cs", "AccountSessionCredential.cs" })
        {
            var code = SourceText.StripCommentsAndLiterals(
                File.ReadAllText(RepositoryLayout.PathOf("src", "OpenClockTower.Server", file)));
            Assert.DoesNotContain("Guid.NewGuid", code, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime", code, StringComparison.Ordinal);
        }
    }

    /// <summary>服务端只留哈希，比较必须固定时间（不泄露"比到第几位"）。</summary>
    [Fact]
    public void CredentialStore_KeepsHashOnly_AndComparesInFixedTime()
    {
        var code = SourceText.StripCommentsAndLiterals(
            File.ReadAllText(RepositoryLayout.PathOf("src", "OpenClockTower.Server", "ConnectionCredentialRecord.cs")));

        Assert.Contains("CryptographicOperations.FixedTimeEquals", code, StringComparison.Ordinal);
        Assert.Contains("Hash", code, StringComparison.Ordinal);

        // 账号会话（D-0021）同一把尺子：只存哈希、固定时间比较。
        var sessions = SourceText.StripCommentsAndLiterals(
            File.ReadAllText(RepositoryLayout.PathOf("src", "OpenClockTower.Server", "AccountSessionRegistry.cs")));
        Assert.Contains("CryptographicOperations.FixedTimeEquals", sessions, StringComparison.Ordinal);
        Assert.Contains("Hash", sessions, StringComparison.Ordinal);
    }

    /// <summary>日志模板不得出现秘密占位符：结构化日志会把它原样写出来，等于泄密。</summary>
    [Fact]
    public void ServerSources_NeverLogSecretPlaceholders()
    {
        var forbidden = new[] { "{Credential}", "{AccountSession}", "{Password}", "{RecoveryCode}" };
        var violations = RepositoryLayout
            .EnumerateSourceFiles("src", "OpenClockTower.Server")
            .SelectMany(path => forbidden
                .Where(token => File.ReadAllText(RepositoryLayout.PathOf(path)).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{path} → {token}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "日志里出现了秘密占位符：凭据与口令是秘密，日志只允许写短指纹（D-0012 / D-0021）。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>前端网关不得把凭据落盘（票据才持久化）或塞进 DOM（innerHTML）。</summary>
    [Fact]
    public void FrontendGateways_DoNotPersistOrRenderCredentials()
    {
        var violations = new List<string>();
        foreach (var relativePath in new[]
                 {
                     Path.Combine("web", "src", "services", "playerGateway.ts"),
                     Path.Combine("web", "src", "services", "storytellerGateway.ts"),
                 })
        {
            var code = File.ReadAllText(RepositoryLayout.PathOf(relativePath));
            foreach (var token in new[] { "localStorage", "sessionStorage", "innerHTML" })
            {
                if (code.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath} → {token}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "前端网关碰了凭据不该碰的东西：凭据只在内存里，不落盘、不进 DOM（D-0012）。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }
}
