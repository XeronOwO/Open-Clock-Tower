using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 分布表：5–15 人逐点、逐行取证等级与结构不变量（口径见 <c>docs/standard/rulings.md</c> R-0041）。
/// </summary>
public sealed class SectsAndVioletsDistributionTests
{
    [Fact]
    public void TableCoversFiveToFifteenWithoutGaps()
    {
        Assert.Equal(11, SectsAndVioletsDistribution.All.Count);
        Assert.Equal(
            Enumerable.Range(5, 11),
            SectsAndVioletsDistribution.All.Select(row => row.PlayerCount));
    }

    [Fact]
    public void EveryRowSumsToItsPlayerCount()
    {
        foreach (var row in SectsAndVioletsDistribution.All)
        {
            Assert.Equal(row.PlayerCount, row.Counts.Total);
            Assert.True(row.Counts.Townsfolk >= 0, $"{row.PlayerCount} 人的镇民数为负");
            Assert.True(row.Counts.Outsiders >= 0, $"{row.PlayerCount} 人的外来者数为负");
            Assert.True(row.Counts.Minions >= 0, $"{row.PlayerCount} 人的爪牙数为负");
            Assert.Equal(1, row.Counts.Demons);
        }
    }

    [Fact]
    public void AttestedRowsMatchTheirWikiSources()
    {
        // 逐行出处见 R-0041「依据」：百科《男爵》/《设置调整》/《无名旅客》（2026-10-04 抓取）。
        Assert.Equal(new SetupCounts(5, 0, 1, 1), SectsAndVioletsDistribution.BaseFor(7));
        Assert.Equal(new SetupCounts(7, 1, 2, 1), SectsAndVioletsDistribution.BaseFor(11));
        Assert.Equal(new SetupCounts(7, 2, 2, 1), SectsAndVioletsDistribution.BaseFor(12));
        Assert.Equal(new SetupCounts(9, 1, 3, 1), SectsAndVioletsDistribution.BaseFor(14));
        Assert.Equal(new SetupCounts(9, 2, 3, 1), SectsAndVioletsDistribution.BaseFor(15));

        foreach (var playerCount in new[] { 7, 11, 12, 14, 15 })
        {
            Assert.Equal(
                SetupRowProvenance.Attested,
                SectsAndVioletsDistribution.RowFor(playerCount)!.Provenance);
        }
    }

    [Fact]
    public void ExtrapolatedRowsStayMarkedOpen()
    {
        foreach (var playerCount in new[] { 5, 6, 8, 9, 10, 13 })
        {
            Assert.Equal(
                SetupRowProvenance.Extrapolated,
                SectsAndVioletsDistribution.RowFor(playerCount)!.Provenance);
        }
    }

    [Fact]
    public void RowsFollowTheAttestedBandStructure()
    {
        // 有据行呈现的结构（R-0041「处理」第 1 条）：镇民 3/5/7/9 分档、外来者在档内 0→1→2、
        // 爪牙 1/2/3、恶魔恒 1。改表时若打破它，先回 R-0041 核对来源，再决定改数据还是改本测试。
        Assert.Equal(
            new[] { 3, 3, 5, 5, 5, 7, 7, 7, 9, 9, 9 },
            SectsAndVioletsDistribution.All.Select(row => row.Counts.Townsfolk));
        Assert.Equal(
            new[] { 0, 1, 0, 1, 2, 0, 1, 2, 0, 1, 2 },
            SectsAndVioletsDistribution.All.Select(row => row.Counts.Outsiders));
        Assert.Equal(
            new[] { 1, 1, 1, 1, 1, 2, 2, 2, 3, 3, 3 },
            SectsAndVioletsDistribution.All.Select(row => row.Counts.Minions));
    }

    [Fact]
    public void UnknownPlayerCountIsNotGuessed()
    {
        Assert.Null(SectsAndVioletsDistribution.RowFor(4));
        Assert.Null(SectsAndVioletsDistribution.RowFor(16));
        Assert.Null(SectsAndVioletsDistribution.BaseFor(4));
        Assert.Null(SectsAndVioletsDistribution.BaseFor(16));
    }
}
