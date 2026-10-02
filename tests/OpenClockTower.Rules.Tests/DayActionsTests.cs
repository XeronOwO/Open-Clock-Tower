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

    /// <summary>
    /// 已实现的白天契约只有女巫（诅咒在下个白天触发）；其余白天相关角色仍不覆盖——
    /// 在场时开白天显式拒绝，不许"白天照跑、能力静默不发生"。
    /// </summary>
    [Fact]
    public void OnlyImplementedDayContractsAreCovered()
    {
        Assert.True(DayActions.IsCovered(new OpenClockTower.Kernel.CharacterId("witch")));

        foreach (var character in SectsAndVioletsRoster.All)
        {
            if (character.Value == "witch")
            {
                continue;
            }

            Assert.False(DayActions.IsCovered(character));
        }
    }
}
