using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 失效账本：每次「能力未正常生效」都留一条带原因分类的记录（数学家口径，R-0004）。
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
}
