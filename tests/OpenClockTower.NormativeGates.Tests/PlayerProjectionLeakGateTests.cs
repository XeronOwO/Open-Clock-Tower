namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：发给玩家的投影里**不得出现轮次 / 进度 / 他人活动信息**。
/// </summary>
/// <remarks>
/// 依据 D-0013 §5：夜晚是统一界面，不显示"轮到谁 / 还有几步 / 进度条 / 谁在思考"。
/// 信息隔离在服务端投影强制（D-0012 §4.3）——这里把"玩家能收到的字段名"本身锁死，
/// 防止将来有人顺手把内部状态塞进玩家投影。
/// </remarks>
public sealed class PlayerProjectionLeakGateTests
{
    private static readonly string[] ScannedFiles =
    [
        Path.Combine("src", "OpenClockTower.Application", "PlayerView.cs"),
        Path.Combine("src", "OpenClockTower.Application", "PlayerEvent.cs"),
        Path.Combine("src", "OpenClockTower.Contracts", "PlayerViewDto.cs"),
        Path.Combine("src", "OpenClockTower.Contracts", "OperationRequestDto.cs"),
        Path.Combine("src", "OpenClockTower.Contracts", "ReconnectBundleDto.cs"),
        Path.Combine("src", "OpenClockTower.Contracts", "PlayerEventDto.cs"),
    ];

    private static readonly string[] ForbiddenTokens =
    [
        "Slot",
        "Step",
        "Progress",
        "Round",
        "Turn",
        "Remaining",
    ];

    /// <summary>玩家投影 / 玩家请求 DTO 里不得出现进度类字段。</summary>
    [Fact]
    public void PlayerFacingContracts_DeclareNoProgressOrOthersActivity()
    {
        var violations = new List<string>();
        foreach (var relativePath in ScannedFiles)
        {
            var code = SourceText.StripCommentsAndLiterals(File.ReadAllText(RepositoryLayout.PathOf(relativePath)));
            foreach (var token in ForbiddenTokens)
            {
                if (code.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath} → {token}");
                }
            }
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            violations.Count == 0,
            "玩家投影里出现了轮次 / 进度类字段（依据 D-0013 §5：夜晚只有统一界面）。"
            + Environment.NewLine
            + report);
    }
}
