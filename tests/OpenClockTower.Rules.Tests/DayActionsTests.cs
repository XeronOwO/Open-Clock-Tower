using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 白天契约名单：哪些角色与白天相关、实现状态如何。
/// </summary>
/// <remarks>
/// 依据 <c>docs/backlog/done/day-phase.md</c> 的机制清点（百科各角色页 · 2026-10-01 抓取）。
/// 名单是"未实现就拒绝开白天"的输入，必须锁死：漏一个角色 = 白天静默跳过一条规则。
/// </remarks>
public sealed class DayActionsTests
{
    [Theory]
    [InlineData("savant")]
    [InlineData("artist")]
    [InlineData("juggler")]
    [InlineData("mutant")]
    [InlineData("cerenovus")]
    [InlineData("witch")]
    [InlineData("sweetheart")]
    [InlineData("barber")]
    [InlineData("klutz")]
    [InlineData("evil-twin")]
    [InlineData("vortox")]
    public void KnownDayRelevantCharacters_AreListed(string character)
    {
        Assert.True(DayActions.IsDayRelevant(new OpenClockTower.Kernel.CharacterId(character)));
    }

    [Theory]
    [InlineData("clockmaker")]
    [InlineData("dreamer")]
    [InlineData("no-dashii")]
    [InlineData("oracle")]
    [InlineData("sage")]
    [InlineData("flowergirl")]
    [InlineData("town-crier")]
    public void NightOnlyCharacters_AreNotDayRelevant(string character)
    {
        Assert.False(DayActions.IsDayRelevant(new OpenClockTower.Kernel.CharacterId(character)));
    }

    [Fact]
    public void NoDayContractIsImplementedYet()
    {
        // 本票只落地通用白天流程：相关角色的契约尚未实现，一律不覆盖（开白天时显式拒绝）。
        foreach (var character in SectsAndVioletsRoster.All)
        {
            Assert.False(DayActions.IsCovered(character));
        }
    }
}
