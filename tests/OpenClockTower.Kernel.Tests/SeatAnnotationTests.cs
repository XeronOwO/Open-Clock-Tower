using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 说书人注记（D-0019）：独立注记账的折叠、标识签发与文本口径。
/// </summary>
/// <remarks>
/// 依据 D-0019（本局级 + 进事件流 + 独立注记账）与 D-0015（自由文本不进状态账）。
/// </remarks>
public sealed class SeatAnnotationTests
{
    /// <summary>增 → 改 → 删按发生顺序折叠；改是原地更新、删之后列表里没有它。</summary>
    [Fact]
    public void Fold_AddUpdateRemove_KeepsOrderAndReplacesInPlace()
    {
        var first = new SeatAnnotation(new SeatAnnotationId(1), new SeatId(2), "18 不共边");
        var second = new SeatAnnotation(new SeatAnnotationId(2), new SeatId(3), "被哲学家获得");

        var ledger = SeatAnnotationMachine.Fold(
        [
            new SeatAnnotationAddedEvent { Annotation = first },
            new SeatAnnotationAddedEvent { Annotation = second },
            new SeatAnnotationUpdatedEvent { Annotation = first with { Text = "18 与 5 不共边" } },
            new SeatAnnotationRemovedEvent { Annotation = second },
        ]);

        var remaining = Assert.Single(ledger.Annotations);
        Assert.Equal(1, remaining.Id.Value);
        Assert.Equal(new SeatId(2), remaining.Seat);
        Assert.Equal("18 与 5 不共边", remaining.Text);
        Assert.Equal(2, ledger.LastIssuedId);
        Assert.Equal(3, ledger.NextId.Value);
    }

    /// <summary>删除不回收标识：再次新增拿到更大的标识，旧 id 不会被复用（D-0019）。</summary>
    [Fact]
    public void RemovedId_IsNotReissued()
    {
        var first = new SeatAnnotation(new SeatAnnotationId(1), new SeatId(1), "甲");

        var ledger = SeatAnnotationLedger.Empty
            .Add(first)
            .Remove(first.Id)
            .Add(new SeatAnnotation(new SeatAnnotationId(2), new SeatId(1), "乙"));

        Assert.Equal(2, Assert.Single(ledger.Annotations).Id.Value);
        Assert.Equal(3, ledger.NextId.Value);
    }

    /// <summary>顺序损坏（重复标识 / 改删不存在的注记）显式抛错，不静默继续。</summary>
    [Fact]
    public void CorruptedStream_Throws()
    {
        var note = new SeatAnnotation(new SeatAnnotationId(1), new SeatId(1), "甲");
        var ledger = SeatAnnotationLedger.Empty.Add(note);

        Assert.Throws<InvalidOperationException>(() => ledger.Add(note));
        Assert.Throws<InvalidOperationException>(
            () => ledger.Update(new SeatAnnotation(new SeatAnnotationId(9), new SeatId(1), "幽灵")));
        Assert.Throws<InvalidOperationException>(() => ledger.Remove(new SeatAnnotationId(9)));
    }

    /// <summary>注记事件不改状态账、也不把步骤机从"还没开始"变成"已开始"。</summary>
    [Fact]
    public void AnnotationEvents_DoNotTouchTheStateLedgerOrTheStepMachine()
    {
        var note = new SeatAnnotation(new SeatAnnotationId(1), new SeatId(1), "甲");
        GameEvent[] events =
        [
            new SeatAnnotationAddedEvent { Annotation = note },
            new SeatAnnotationUpdatedEvent { Annotation = note with { Text = "乙" } },
            new SeatAnnotationRemovedEvent { Annotation = note },
        ];

        Assert.Same(GameState.Empty, GameStateMachine.Fold(events));
        Assert.Null(StepMachine.Fold(events));
        Assert.Equal(1, SeatAnnotationMachine.Fold(events).LastIssuedId);
    }

    /// <summary>每席计数只数仍然存在的那一席（每席上限的判据）。</summary>
    [Fact]
    public void CountOn_CountsOnlyLiveAnnotationsOfThatSeat()
    {
        var ledger = SeatAnnotationLedger.Empty
            .Add(new SeatAnnotation(new SeatAnnotationId(1), new SeatId(2), "甲"))
            .Add(new SeatAnnotation(new SeatAnnotationId(2), new SeatId(3), "乙"))
            .Add(new SeatAnnotation(new SeatAnnotationId(3), new SeatId(2), "丙"))
            .Remove(new SeatAnnotationId(2));

        Assert.Equal(2, ledger.CountOn(new SeatId(2)));
        Assert.Equal(0, ledger.CountOn(new SeatId(3)));
    }

    /// <summary>文本口径：折叠空白（含换行 / 制表）与首尾空格；空 / 控制字符拒绝。</summary>
    [Theory]
    [InlineData("  18 不共边  ", "18 不共边", null)]
    [InlineData("甲\n\n乙\t丙", "甲 乙 丙", null)]
    [InlineData("甲\r\n乙", "甲 乙", null)]
    [InlineData("   ", null, "empty")]
    [InlineData(null, null, "empty")]
    [InlineData("甲\u0000乙", null, "control")]
    public void TryNormalize_AppliesTheTextRule(string? raw, string? expected, string? expectedFailure)
    {
        var ok = SeatAnnotationText.TryNormalize(raw, out var normalized, out var failure);

        if (expectedFailure is null)
        {
            Assert.True(ok);
            Assert.Equal(expected, normalized);
            Assert.Empty(failure);
        }
        else
        {
            Assert.False(ok);
            Assert.Equal(expectedFailure, failure);
            Assert.Empty(normalized);
        }
    }

    /// <summary>长度上限按归一化后的文本算（D-0019：单条最多 120 字符）。</summary>
    [Fact]
    public void TryNormalize_RejectsTextLongerThanTheLimit()
    {
        var atLimit = new string('甲', SeatAnnotationText.MaxLength);
        var tooLong = new string('甲', SeatAnnotationText.MaxLength + 1);

        Assert.True(SeatAnnotationText.TryNormalize(atLimit, out _, out _));
        Assert.False(SeatAnnotationText.TryNormalize(tooLong, out _, out var failure));
        Assert.Equal("too_long", failure);
    }
}
