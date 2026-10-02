using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 疯狂要求的建模：由能力产生（R-0021），引擎不判定疯狂（R-0003）。
/// </summary>
public sealed class MadnessRequirementTests
{
    /// <summary>要求记录身份、来源与到期日；它不替玩家做任何维度判断。</summary>
    /// <remarks>
    /// 依据 <c>docs/standard/rulings.md</c> R-0021：要求的**产生方是能力**（洗脑师夜晚行动生效时写入），
    /// 是否"疯狂"仍由说书人裁定（R-0003）——因此这条要求必须能指回来源席位 / 角色 / 能力与有效窗口，
    /// 但不得携带"玩家是否疯狂"的结论。
    /// </remarks>
    [Fact]
    public void MadnessRequirement_IsAnchoredToItsAbilitySourceAndWindow()
    {
        var requirement = new MadnessRequirement
        {
            Id = new MadnessRequirementId("sv:night-1:cerenovus:madness"),
            Seat = new SeatId(3),
            ProveToBe = "钟表匠",
            Source = new SeatId(1),
            SourceCharacter = new CharacterId("cerenovus"),
            Ability = new AbilityId("cerenovus.madness"),
            ExpiresAtDay = 2,
        };

        Assert.Equal(new MadnessRequirementId("sv:night-1:cerenovus:madness"), requirement.Id);
        Assert.Equal(new SeatId(3), requirement.Seat);
        Assert.Equal("钟表匠", requirement.ProveToBe);
        Assert.Equal(new SeatId(1), requirement.Source);
        Assert.Equal(new CharacterId("cerenovus"), requirement.SourceCharacter);
        Assert.Equal(new AbilityId("cerenovus.madness"), requirement.Ability);
        Assert.Equal(2, requirement.ExpiresAtDay);
        Assert.False(requirement.IsTerminated);
    }
}
