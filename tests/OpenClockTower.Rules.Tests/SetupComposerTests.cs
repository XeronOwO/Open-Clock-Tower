using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 配板求解：可重放、角色唯一、净分布口径（先加总 / 钳制 / 显式披露）与失败面（R-0041 / R-0042）。
/// </summary>
public sealed class SetupComposerTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void ComposeFillsTheSeatCountWithUniqueRoles(int playerCount)
    {
        var result = SetupComposer.Compose(playerCount, $"seed-{playerCount}");

        Assert.True(result.Ok, result.Error?.Message);
        var proposal = result.Proposal!;
        Assert.Equal(playerCount, proposal.Bag.Count);
        Assert.Equal(playerCount, proposal.Bag.Distinct().Count());
        Assert.Equal(playerCount, proposal.Counts.Total);

        foreach (var character in proposal.Bag)
        {
            Assert.NotNull(SectsAndVioletsRoster.TypeOf(character));
        }
    }

    [Fact]
    public void SameSeedProducesTheSameBag()
    {
        var first = SetupComposer.Compose(9, "replay-seed");
        var second = SetupComposer.Compose(9, "replay-seed");

        Assert.True(first.Ok);
        Assert.Equal(first.Proposal!.Bag, second.Proposal!.Bag);
        Assert.Equal(first.Proposal.Counts, second.Proposal.Counts);
        Assert.Equal(first.Proposal.Notes, second.Proposal.Notes);
    }

    [Fact]
    public void DifferentSeedsCanProduceDifferentBags()
    {
        var bags = Enumerable
            .Range(0, 8)
            .Select(index => string.Join(",", SetupComposer.Compose(9, $"seed-{index}").Proposal!.Bag.Select(c => c.Value)))
            .Distinct()
            .Count();

        Assert.True(bags > 1, "八个种子里只出现了一种配板：抽取没有真的用到显式随机输入");
    }

    [Fact]
    public void PositiveAdjustmentMovesOneTownsfolkToOutsider()
    {
        var script = Script(demonAdjustments: [new SetupAdjustment(CharacterType.Outsider, 1)]);
        var result = SetupComposer.Compose(script, 8, "s");

        Assert.True(result.Ok, result.Error?.Message);
        // 8 人基线 5/1/1/1；《设置调整》默认由镇民补偿：外来者 1 → 2、镇民 5 → 4。
        Assert.Equal(new SetupCounts(4, 2, 1, 1), result.Proposal!.Counts);
        Assert.Contains(result.Proposal.Notes, note => note.Contains("外来者 +1", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeAdjustmentIsClampedAtZeroAndDisclosed()
    {
        var script = Script(demonAdjustments: [new SetupAdjustment(CharacterType.Outsider, -1)]);
        var result = SetupComposer.Compose(script, 7, "s");

        Assert.True(result.Ok, result.Error?.Message);
        // 7 人基线外来者为 0（R-0041）；−1 没有可移除的量（《亡骨魔》运作方式）→ 保持 0。
        Assert.Equal(new SetupCounts(5, 0, 1, 1), result.Proposal!.Counts);
        Assert.Contains(
            result.Proposal.Notes,
            note => note.Contains("钳制", StringComparison.Ordinal) && note.Contains("期望 -1", StringComparison.Ordinal));
    }

    [Fact]
    public void OvershootIsClampedToTheScriptPoolAndDisclosed()
    {
        var script = Script(demonAdjustments: [new SetupAdjustment(CharacterType.Outsider, 3)]);
        var result = SetupComposer.Compose(script, 12, "s");

        Assert.True(result.Ok, result.Error?.Message);
        // R-0042 依据里的百科例：12 人（7/2/2/1）中 +3，剧本只有 4 名外来者 →
        // 最终外来者 4、镇民 5（实际效果 +2 / −2），且必须显式披露。
        Assert.Equal(new SetupCounts(5, 4, 2, 1), result.Proposal!.Counts);
        Assert.Contains(
            result.Proposal.Notes,
            note => note.Contains("期望 5", StringComparison.Ordinal) && note.Contains("实际 4", StringComparison.Ordinal));
    }

    [Fact]
    public void OppositeAdjustmentsAreSummedBeforeClamping()
    {
        // R-0042 依据里的百科例：+1 与 −1 同时在场 → 先加总得 0 → 分布不变（不是先后各钳一次）。
        // 11 人基线要 2 名爪牙，池子就放 2 名（两条都会被抽到），修正挂在第一条上。
        var script = Script(
            demonAdjustments: [new SetupAdjustment(CharacterType.Outsider, 1)],
            minionAdjustments: [new SetupAdjustment(CharacterType.Outsider, -1)],
            minions: 2);
        var result = SetupComposer.Compose(script, 11, "s");

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(new SetupCounts(7, 1, 2, 1), result.Proposal!.Counts);
        Assert.DoesNotContain(result.Proposal.Notes, note => note.Contains("钳制", StringComparison.Ordinal));
    }

    [Fact]
    public void PlayerCountOutsideTheTableFailsExplicitly()
    {
        foreach (var playerCount in new[] { 4, 16 })
        {
            var result = SetupComposer.Compose(playerCount, "s");

            Assert.False(result.Ok);
            Assert.Equal(SetupComposeResult.FailureCode.PlayerCountUnsupported, result.Error!.Code);
        }
    }

    [Fact]
    public void ScriptTooSmallForTheBaselineFailsExplicitly()
    {
        var script = Script(townsfolk: 3, outsiders: 0, minions: 1, demons: 1);
        var result = SetupComposer.Compose(script, 7, "s");

        Assert.False(result.Ok);
        Assert.Equal(SetupComposeResult.FailureCode.PoolExhausted, result.Error!.Code);
    }

    [Fact]
    public void TownsfolkTargetedAdjustmentIsRejectedExplicitly()
    {
        var script = Script(demonAdjustments: [new SetupAdjustment(CharacterType.Townsfolk, 1)]);
        var result = SetupComposer.Compose(script, 8, "s");

        Assert.False(result.Ok);
        Assert.Equal(SetupComposeResult.FailureCode.DistributionConflict, result.Error!.Code);
    }

    [Fact]
    public void OscillatingAdjustmentsFailInsteadOfReturningAnUnclearBag()
    {
        // 合成剧本：恶魔 +1 外来者、唯一的外来者 −1 外来者。7 人基线外来者为 0 →
        // 抽取会「加上那名外来者 → 又必须撤下」地来回，迭代上限内不收敛 → 显式失败（R-0042 第 3 条）。
        var script = Script(
            demonAdjustments: [new SetupAdjustment(CharacterType.Outsider, 1)],
            outsiderAdjustments: [new SetupAdjustment(CharacterType.Outsider, -1)],
            outsiders: 1);
        var result = SetupComposer.Compose(script, 7, "s");

        Assert.False(result.Ok);
        Assert.Equal(SetupComposeResult.FailureCode.DistributionConflict, result.Error!.Code);
    }

    [Fact]
    public void EmptySeedIsRejectedBecauseRandomMustBeExplicit()
    {
        Assert.Throws<ArgumentException>(() => SetupComposer.Compose(8, " "));
    }

    /// <summary>
    /// 合成剧本：默认池子够 15 人用；把修正挂在池子的第一名角色上（单元素池 = 必然被抽到）。
    /// </summary>
    private static SetupScript Script(
        IReadOnlyList<SetupAdjustment>? demonAdjustments = null,
        IReadOnlyList<SetupAdjustment>? minionAdjustments = null,
        IReadOnlyList<SetupAdjustment>? outsiderAdjustments = null,
        IReadOnlyList<SetupAdjustment>? townsfolkAdjustments = null,
        int townsfolk = 13,
        int outsiders = 4,
        int minions = 4,
        int demons = 1)
    {
        return new SetupScript(
            [.. Enumerable.Range(0, townsfolk).Select(index => Entry($"tf-{index}", index == 0 ? townsfolkAdjustments : null))],
            [.. Enumerable.Range(0, outsiders).Select(index => Entry($"outsider-{index}", index == 0 ? outsiderAdjustments : null))],
            [.. Enumerable.Range(0, minions).Select(index => Entry($"minion-{index}", index == 0 ? minionAdjustments : null))],
            [.. Enumerable.Range(0, demons).Select(index => Entry($"demon-{index}", index == 0 ? demonAdjustments : null))]);
    }

    private static SetupPoolEntry Entry(string slug, IReadOnlyList<SetupAdjustment>? adjustments) =>
        new(new CharacterId(slug), adjustments ?? []);
}
