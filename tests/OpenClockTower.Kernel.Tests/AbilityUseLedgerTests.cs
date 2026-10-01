using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 能力使用账本：使用过 ≠ 生效过；醉酒/中毒期间使用一次性能力 = 已浪费。
/// </summary>
/// <remarks>依据：百科《重要细节》三-3。</remarks>
public sealed class AbilityUseLedgerTests
{
    private static readonly SeatId SeatOne = new(1);
    private static readonly SeatId SeatTwo = new(2);
    private static readonly AbilityId Slayer = new("slayer");
    private static readonly AbilityId Oracle = new("oracle");

    /// <summary>验收矩阵行 6：中毒期间使用 → used=true、effective=false。</summary>
    [Fact]
    public void WastedUse_IsRecordedAsUsedButNotEffective()
    {
        var ledger = new AbilityUseLedger();

        var afterUse = ledger.RecordUse(SeatOne, Slayer, effective: false);

        Assert.True(afterUse.WasUsed(SeatOne, Slayer));
        Assert.False(afterUse.WasEffective(SeatOne, Slayer));

        var entry = Assert.Single(afterUse.Entries);
        Assert.False(entry.Effective);
    }

    /// <summary>验收矩阵行 5：已浪费的一次性能力，来源恢复后也不可再用。</summary>
    /// <remarks>
    /// 百科《重要细节》三-3：「即便如此，如果他已经使用了自己的"每局游戏限一次"能力，
    /// 很不幸，他仍无法再次使用这项能力。」——账本里的「用过了」不随席位状态恢复而消失。
    /// </remarks>
    [Fact]
    public void WastedOncePerGameAbility_CannotBeUsedAgain()
    {
        var afterUse = new AbilityUseLedger().RecordUse(SeatOne, Slayer, effective: false);

        Assert.True(afterUse.WasUsed(SeatOne, Slayer));
        Assert.False(afterUse.WasEffective(SeatOne, Slayer));
    }

    /// <summary>正常生效的使用同样计入「用过」，并且 effective=true。</summary>
    [Fact]
    public void EffectiveUse_IsRecordedAsUsedAndEffective()
    {
        var afterUse = new AbilityUseLedger().RecordUse(SeatOne, Slayer, effective: true);

        Assert.True(afterUse.WasUsed(SeatOne, Slayer));
        Assert.True(afterUse.WasEffective(SeatOne, Slayer));
    }

    /// <summary>记录按席位与能力隔离：1 号用过不代表 2 号用过，也不代表另一个能力用过。</summary>
    [Fact]
    public void Uses_AreScopedPerSeatAndAbility()
    {
        var ledger = new AbilityUseLedger()
            .RecordUse(SeatOne, Slayer, effective: false)
            .RecordUse(SeatTwo, Oracle, effective: true);

        Assert.True(ledger.WasUsed(SeatOne, Slayer));
        Assert.False(ledger.WasUsed(SeatTwo, Slayer));
        Assert.False(ledger.WasUsed(SeatOne, Oracle));

        Assert.False(ledger.WasEffective(SeatOne, Slayer));
        Assert.True(ledger.WasEffective(SeatTwo, Oracle));
    }

    /// <summary>两个问题各自独立：同一能力多次使用时，两种记录可以并存。</summary>
    /// <remarks>
    /// 账本本身不假设「每局游戏限一次」——限次由调用方查 <see cref="AbilityUseLedger.WasUsed"/> 判定；
    /// 可用多次的能力失败一次、成功后，仍应能回答「生效过」。
    /// </remarks>
    [Fact]
    public void MalfunctionThenSuccess_ReportsBothFactsIndependently()
    {
        var ledger = new AbilityUseLedger()
            .RecordUse(SeatOne, Slayer, effective: false)
            .RecordUse(SeatOne, Slayer, effective: true);

        Assert.True(ledger.WasUsed(SeatOne, Slayer));
        Assert.True(ledger.WasEffective(SeatOne, Slayer));
    }
}
