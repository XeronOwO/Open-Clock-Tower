using System.Text.Json;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 说书人注记在真实宿主里的链路（D-0019）：增 / 改 / 删进说书人视图、重启后按事件流还在、
/// 玩家侧零下发（反方向断言）、身份与合法性闸拦下越界输入。
/// </summary>
public sealed class AnnotationHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>增 → 改 → 删的完整链路；玩家连接既收不到推送、重连包里也没有注记字段。</summary>
    [Fact]
    public async Task AddUpdateRemove_FlowThroughStorytellerView_AndStayOutOfPlayerPayloads()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        await using var storyteller = await host.ConnectStorytellerAsync();
        var playerViews = 0;
        await using var seatOne = await host.ConnectSeatAsync(
            new SeatId(1),
            onStorytellerView: _ => Interlocked.Increment(ref playerViews));

        var added = await storyteller.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation",
            2,
            "18 不共边",
            "test-annotation-add-1");
        Assert.Equal("Accepted", added.Kind);

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        var note = Assert.Single(view.Annotations);
        Assert.Equal(1, note.Id);
        Assert.Equal(2, note.Seat);
        Assert.Equal("18 不共边", note.Text);

        var updated = await storyteller.InvokeAsync<CommandResultDto>(
            "UpdateSeatAnnotation",
            note.Id,
            "18 与 5 不共边",
            "test-annotation-update-1");
        Assert.Equal("Accepted", updated.Kind);

        view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        var updatedNote = Assert.Single(view.Annotations);
        Assert.Equal(note.Id, updatedNote.Id);
        Assert.Equal(2, updatedNote.Seat);
        Assert.Equal("18 与 5 不共边", updatedNote.Text);

        // 反方向（D-0012 §4.3 / D-0013 §5）：注记是"读时状态"，重连包与推送都不该带上它。
        await using var rejoined = await host.ConnectSeatAsync(new SeatId(1));
        var bundleJson = JsonSerializer.Serialize(host.Bundles[new SeatId(1)]);
        Assert.DoesNotContain("不共边", bundleJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Annotation", bundleJson, StringComparison.Ordinal);

        var leaked = await TestServerHost.WaitUntilAsync(
            () => Volatile.Read(ref playerViews) > 0,
            TimeSpan.FromMilliseconds(300));
        Assert.False(leaked, "玩家连接不该收到说书人视图（注记随说书人视图推送，D-0019）");

        var removed = await storyteller.InvokeAsync<CommandResultDto>(
            "RemoveSeatAnnotation",
            note.Id,
            "test-annotation-remove-1");
        Assert.Equal("Accepted", removed.Kind);

        view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Empty(view.Annotations);

        // 已删除的标识不再接受改 / 删（改 / 删都要按"还存在"判定）。
        var stale = await storyteller.InvokeAsync<CommandResultDto>(
            "UpdateSeatAnnotation",
            note.Id,
            "幽灵",
            "test-annotation-stale-1");
        Assert.Equal("Rejected", stale.Kind);
        Assert.Equal("legality.annotation_unknown", stale.RejectionCode);
    }

    /// <summary>重启 = 重放事件流：注记还在，且签发水位也恢复（新注记拿到更大的标识）。</summary>
    [Fact]
    public async Task Notes_SurviveHostRestart_AndKeepTheIssuedWatermark()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-annotation-{Guid.NewGuid():N}.db");

        await using (var first = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            databasePath: databasePath,
            deleteDatabaseOnDispose: false))
        {
            await using var storyteller = await first.ConnectStorytellerAsync();
            var result = await storyteller.InvokeAsync<CommandResultDto>(
                "AddSeatAnnotation",
                2,
                "重启后仍在",
                "test-annotation-restart-1");
            Assert.Equal("Accepted", result.Kind);
        }

        await using var restarted = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            databasePath: databasePath,
            deleteDatabaseOnDispose: true);

        await using var storytellerAfterRestart = await restarted.ConnectStorytellerAsync();
        var view = await TestServerHost.WaitForViewAsync(
            storytellerAfterRestart,
            candidate => candidate.Annotations.Length > 0,
            Wait);

        Assert.NotNull(view);
        var note = Assert.Single(view.Annotations);
        Assert.Equal(1, note.Id);
        Assert.Equal(2, note.Seat);
        Assert.Equal("重启后仍在", note.Text);

        // 水位一起恢复：第二条拿到 2，而不是复用 1。
        var second = await storytellerAfterRestart.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation",
            3,
            "第二条",
            "test-annotation-restart-2");
        Assert.Equal("Accepted", second.Kind);

        view = await storytellerAfterRestart.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Equal([1, 2], view.Annotations.Select(annotation => annotation.Id));
    }

    /// <summary>身份闸：玩家的连接级凭据发不了注记命令（D-0012 §4.2）。</summary>
    [Fact]
    public async Task PlayerCredential_CannotWriteAnnotations()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        await using var seatOne = await host.ConnectSeatAsync(new SeatId(1));

        var result = await seatOne.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation",
            2,
            "玩家写的",
            "test-annotation-player-1");

        Assert.Equal("Rejected", result.Kind);
        Assert.Equal("identity.storyteller_only", result.RejectionCode);
    }

    /// <summary>合法性闸：空 / 超长 / 控制字符 / 幽灵席位 / 每席超限都被拒，且不产生事件。</summary>
    [Fact]
    public async Task InvalidTextSeatAndCapacity_AreRejected()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var empty = await storyteller.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation",
            1,
            "   ",
            "test-annotation-empty");
        Assert.Equal("Rejected", empty.Kind);
        Assert.Equal("legality.annotation_empty", empty.RejectionCode);

        var tooLong = await storyteller.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation",
            1,
            new string('甲', 121),
            "test-annotation-too-long");
        Assert.Equal("Rejected", tooLong.Kind);
        Assert.Equal("legality.annotation_too_long", tooLong.RejectionCode);

        var control = await storyteller.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation",
            1,
            "甲\u0000乙",
            "test-annotation-control");
        Assert.Equal("Rejected", control.Kind);
        Assert.Equal("legality.annotation_control", control.RejectionCode);

        var ghostSeat = await storyteller.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation",
            9,
            "越界",
            "test-annotation-ghost-seat");
        Assert.Equal("Rejected", ghostSeat.Kind);
        Assert.Equal("legality.seat_unknown", ghostSeat.RejectionCode);

        for (var index = 1; index <= SeatAnnotationText.MaxPerSeat; index++)
        {
            var accepted = await storyteller.InvokeAsync<CommandResultDto>(
                "AddSeatAnnotation",
                1,
                $"第 {index} 条",
                $"test-annotation-limit-{index}");
            Assert.Equal("Accepted", accepted.Kind);
        }

        var overLimit = await storyteller.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation",
            1,
            "第六条",
            "test-annotation-limit-6");
        Assert.Equal("Rejected", overLimit.Kind);
        Assert.Equal("legality.annotation_limit", overLimit.RejectionCode);

        // 被拒的命令一条事件都不产生：本局注记仍是前 5 条。
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Equal(SeatAnnotationText.MaxPerSeat, view.Annotations.Length);
    }

    /// <summary>文本归一化在服务端做：换行折成单个空格后再落库（D-0019 的有界化）。</summary>
    [Fact]
    public async Task MultilineText_IsCollapsedBeforeStoring()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var result = await storyteller.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation",
            2,
            "  甲\r\n\r\n乙\t丙  ",
            "test-annotation-multiline-1");
        Assert.Equal("Accepted", result.Kind);

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        var note = Assert.Single(view.Annotations);
        Assert.Equal("甲 乙 丙", note.Text);
    }

    /// <summary>幂等闸：同一幂等键重复投递只生效一次，第二次返回首次回执。</summary>
    [Fact]
    public async Task SameIdempotencyKey_IsAppliedOnce()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var first = await storyteller.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation",
            2,
            "只此一次",
            "test-annotation-idempotent-1");
        var second = await storyteller.InvokeAsync<CommandResultDto>(
            "AddSeatAnnotation",
            2,
            "只此一次",
            "test-annotation-idempotent-1");

        Assert.Equal("Accepted", first.Kind);
        Assert.Equal("Duplicate", second.Kind);

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Single(view.Annotations);
    }

    /// <summary>游戏结束后一切命令被拒（R-0024）：注记命令走的是同一条闸。</summary>
    [Fact]
    public void AnnotationCommands_AreRejectedAfterGameEnded()
    {
        var machine = new StepMachineState
        {
            Plan = new StepPlan
            {
                Label = "test:night-1",
                Phase = GamePhase.FirstNight,
                Slots = [StepSlot.DawnWait(new StepSlotId("dawn"))],
            },
            SlotIndex = 0,
            Quota = SlotQuotaState.Running,
            Control = ControlMode.Automatic,
            Outcome = new GameOutcome
            {
                Winner = Alignment.Good,
                Condition = OutcomeCondition.DemonsAllDead,
                Detail = "测试：恶魔全死",
            },
        };

        var decision = CommandGatePipeline.Evaluate(
            new CommandEnvelope
            {
                Command = new AddSeatAnnotationCommand { Seat = new SeatId(1), Text = "甲" },
                Actor = Actor.Storyteller(),
                IdempotencyKey = "test-annotation-ended-1",
            },
            machine,
            receipt: null);

        Assert.Equal(GateDecisionKind.Reject, decision.Kind);
        Assert.Equal("phase.game_ended", decision.Rejection!.Code);
    }
}
