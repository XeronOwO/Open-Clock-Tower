using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 疯狂要求的建模：只由裁定写入，引擎不判定疯狂（R-0003）。
/// </summary>
public sealed class MadnessRequirementTests
{
    /// <summary>要求以产生它的裁定点溯源；没有裁定就没有这条要求。</summary>
    /// <remarks>
    /// 依据 <c>docs/standard/rulings.md</c> R-0003：引擎负责产生要求、承接裁定结果，
    /// 但**不**判断玩家是否疯狂——因此这条要求必须能指回写入它的裁定点。
    /// </remarks>
    [Fact]
    public void MadnessRequirement_IsAnchoredToTheRulingThatIssuedIt()
    {
        var issuedBy = new DecisionPointId("cerenovus-night-1-madness");

        var requirement = new MadnessRequirement
        {
            Seat = new SeatId(3),
            ProveToBe = "clockmaker",
            IssuedBy = issuedBy,
        };

        Assert.Equal(issuedBy, requirement.IssuedBy);
        Assert.Equal(new SeatId(3), requirement.Seat);
        Assert.Equal("clockmaker", requirement.ProveToBe);
    }
}
