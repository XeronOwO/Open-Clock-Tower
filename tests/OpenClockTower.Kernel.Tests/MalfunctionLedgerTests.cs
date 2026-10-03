using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 失效账本：每次「能力未正常生效」按原因逐条记录（数学家口径 R-0004：按玩家去重、相克与能力自身设定不计入）。
/// </summary>
public sealed class MalfunctionLedgerTests
{
    /// <summary>验收矩阵行 8：一次失败的能力结算 → 一条带原因分类的记录。</summary>
    [Fact]
    public void FailedAbility_IsRecordedWithItsReason()
    {
        var ledger = new MalfunctionLedger();

        var afterFailure = ledger.Record(new SeatId(4), new AbilityId("oracle"), MalfunctionKind.Poisoned);

        var entry = Assert.Single(afterFailure.Entries);
        Assert.Equal(new SeatId(4), entry.Seat);
        Assert.Equal(new AbilityId("oracle"), entry.Ability);
        Assert.Equal(MalfunctionKind.Poisoned, entry.Kind);
        Assert.Equal(1, afterFailure.Count);
    }

    /// <summary>口径未定的路径记 Open，并进入待核对清单（R-0004）。</summary>
    [Fact]
    public void UnverifiedPath_IsRecordedAsOpen_AndListedForFollowUp()
    {
        var ledger = new MalfunctionLedger()
            .Record(new SeatId(1), new AbilityId("savant"), MalfunctionKind.Open);

        var pending = Assert.Single(ledger.Unclassified);
        Assert.Equal(MalfunctionKind.Open, pending.Kind);
    }

    /// <summary>已核对到依据的记录不进入待核对清单。</summary>
    [Fact]
    public void VerifiedReason_IsNotListedAsUnclassified()
    {
        var ledger = new MalfunctionLedger()
            .Record(new SeatId(1), new AbilityId("savant"), MalfunctionKind.Drunk)
            .Record(new SeatId(2), new AbilityId("oracle"), MalfunctionKind.Jinx);

        Assert.Equal(2, ledger.Count);
        Assert.Empty(ledger.Unclassified);
    }

    /// <summary>R-0004：数学家的数字按玩家去重——同一玩家的多条记录只算一次，不计入的分类不进数字。</summary>
    [Fact]
    public void CountedSeats_DeduplicatesPlayers_AndExcludesNonCountedKinds()
    {
        var ledger = new MalfunctionLedger()
            .Record(new SeatId(2), new AbilityId("clockmaker"), MalfunctionKind.Vortox)
            .Record(new SeatId(2), new AbilityId("clockmaker"), MalfunctionKind.Poisoned)
            .Record(new SeatId(3), new AbilityId("oracle"), MalfunctionKind.Jinx)
            .Record(new SeatId(4), new AbilityId("savant"), MalfunctionKind.AbilityDesign)
            .Record(new SeatId(5), new AbilityId("artist"), MalfunctionKind.Barista)
            .Record(new SeatId(6), new AbilityId("juggler"), MalfunctionKind.StorytellerRuling);

        Assert.Equal(6, ledger.Count);
        Assert.Equal(new[] { new SeatId(2) }, ledger.CountedSeats);
    }

    /// <summary>首夜（还没有黎明）：窗口 = 全账（R-0004 补充：首夜窗口下界取开局）。</summary>
    [Fact]
    public void CountedSeatsSinceDawn_FirstNight_CoversTheWholeLedger()
    {
        var ledger = new MalfunctionLedger()
            .Record(new SeatId(2), new AbilityId("dreamer"), MalfunctionKind.Poisoned)
            .Record(new SeatId(3), new AbilityId("oracle"), MalfunctionKind.Jinx);

        Assert.Equal(0, ledger.SinceDawnStart);
        Assert.Equal(new[] { new SeatId(2) }, ledger.CountedSeatsSinceDawn);
    }

    /// <summary>黎明把窗口起点推进到当时末尾：旧记录留在账上、但不进新窗口（R-0004 第 2 / 4 条）。</summary>
    [Fact]
    public void AdvanceDawn_KeepsEntries_ButMovesTheWindow()
    {
        var ledger = new MalfunctionLedger()
            .Record(new SeatId(2), new AbilityId("dreamer"), MalfunctionKind.Poisoned);

        var afterDawn = ledger.AdvanceDawn();

        Assert.Equal(1, afterDawn.SinceDawnStart);
        Assert.Single(afterDawn.Entries);
        Assert.Empty(afterDawn.CountedSeatsSinceDawn);
        Assert.Equal(new[] { new SeatId(2) }, afterDawn.CountedSeats);
    }

    /// <summary>新窗口内仍按玩家去重、仍按 R-0004 对表剔除不计入的分类。</summary>
    [Fact]
    public void CountedSeatsSinceDawn_DeduplicatesAndExcludesNonCountedKinds()
    {
        var ledger = new MalfunctionLedger()
            .Record(new SeatId(1), new AbilityId("dreamer"), MalfunctionKind.Drunk)
            .AdvanceDawn()
            .Record(new SeatId(4), new AbilityId("clockmaker"), MalfunctionKind.Poisoned)
            .Record(new SeatId(4), new AbilityId("clockmaker"), MalfunctionKind.Drunk)
            .Record(new SeatId(5), new AbilityId("oracle"), MalfunctionKind.Jinx)
            .Record(new SeatId(6), new AbilityId("savant"), MalfunctionKind.AbilityDesign);

        Assert.Equal(new[] { new SeatId(4) }, ledger.CountedSeatsSinceDawn);
        Assert.Equal(new[] { new SeatId(1), new SeatId(4) }, ledger.CountedSeats);
    }

    /// <summary>接连两个黎明：窗口起点各自推进到当时末尾，记录一条不丢。</summary>
    [Fact]
    public void ConsecutiveDawns_MoveTheWindowEachTime()
    {
        var ledger = new MalfunctionLedger()
            .Record(new SeatId(2), new AbilityId("dreamer"), MalfunctionKind.Poisoned)
            .AdvanceDawn()
            .Record(new SeatId(3), new AbilityId("oracle"), MalfunctionKind.Drunk)
            .AdvanceDawn()
            .Record(new SeatId(4), new AbilityId("savant"), MalfunctionKind.Vortox);

        Assert.Equal(2, ledger.SinceDawnStart);
        Assert.Equal(3, ledger.Count);
        Assert.Equal(new[] { new SeatId(4) }, ledger.CountedSeatsSinceDawn);
    }

    /// <summary>R-0004 逐条对表：未核对的一律不计入（含"未定"仍计入——它只表示原因待核对）。</summary>
    [Theory]
    [InlineData(MalfunctionKind.Poisoned, true)]
    [InlineData(MalfunctionKind.Drunk, true)]
    [InlineData(MalfunctionKind.Vortox, true)]
    [InlineData(MalfunctionKind.Open, true)]
    [InlineData(MalfunctionKind.Jinx, false)]
    [InlineData(MalfunctionKind.AbilityDesign, false)]
    [InlineData(MalfunctionKind.Barista, false)]
    [InlineData(MalfunctionKind.StorytellerRuling, false)]
    public void CountsForMathematician_MatchesRuling(MalfunctionKind kind, bool counts) =>
        Assert.Equal(counts, kind.CountsForMathematician());
}
