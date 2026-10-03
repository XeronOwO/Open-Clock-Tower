using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 理发师在真实宿主里的整条链路（平台口径见 <c>docs/standard/rulings.md</c> R-0033）：
/// 以理发师身份死亡（白天处决 / 夜里被杀）→ 立即记「今晚理发」→ 当夜理发师格唤醒幸存恶魔 →
/// 恶魔选择两名玩家交换角色（或摇头）→ 尚未进入的槽位重绑给新持有者；错过理发师格则「过时不候」。
/// </summary>
/// <remarks>
/// 真宿主 + 真 SignalR + 真 SQLite；其他夜晚用 0.05 秒配额自动推进，只真正结算与理发师有关的几步。
/// </remarks>
public sealed class BarberHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 第 1 个白天处决理发师 → 当夜理发师格唤醒诺-达鲺 → 诺-达鲺把 3/4 号角色互换（阵营不变）→
    /// 筑梦师槽位尚未进入，重绑给新持有者 4 号并在当夜唤醒它。
    /// </summary>
    [Fact]
    public async Task BarberExecutedDuringDay_OpensSwapAtNight_AndRebindsLaterSlot()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "barber"), (2, "clockmaker"), (3, "dreamer"), (4, "no-dashii"), (5, "klutz")),
            "test-barber-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var four = await host.ConnectSeatAsync(new SeatId(4));

        // 第 1 夜：与理发师无关，强推走完（本组用例只真正结算当夜交互）。
        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-barber-night-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "barber-1");

        // 第 1 个白天：说书人上报理发师被处决（死亡即时落账 → 立即记「今晚理发」）。
        var day = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-barber-day-1");
        Assert.True(
            day.Kind == "Accepted",
            $"StartDay 没被接受：kind={day.Kind} code={day.RejectionCode} msg={day.RejectionMessage}");

        var executed = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            "Dead",
            null,
            null,
            null,
            null,
            "测试：处决理发师",
            null,
            "test-barber-executed");
        Assert.Equal("Accepted", executed.Kind);
        Assert.Equal(LifeState.Dead, LifeOf(host, 1));

        var closedDay = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-barber-close-day");
        Assert.Equal("Accepted", closedDay.Kind);

        // 第 2 夜：诺-达鲺先击杀 2 号（无关槽位），随后走到理发师格。
        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-barber-night-2");
        Assert.True(
            night.Kind == "Accepted",
            $"StartNight(2) 没被接受：kind={night.Kind} code={night.RejectionCode} msg={night.RejectionMessage}");

        var kill = await WaitForRequestAsync(host, new SeatId(4));
        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                kill.Id.Value,
                "seat:2",
                "test-barber-kill",
                1L)).Kind);
        Assert.Equal(LifeState.Dead, LifeOf(host, 2));

        // 理发师格：请求发给 4 号恶魔（诺-达鲺），一次原子选择 = 玩家对 / 不交换。
        var swap = await WaitForRequestAsync(host, new SeatId(4));
        Assert.Contains(swap.Prompt.Options, option => option.Value == "pair:3+4");
        Assert.Contains(swap.Prompt.Options, option => option.Value == "decline");

        // 交换 3 号（筑梦师）与 4 号（诺-达鲺自己）：角色互换、阵营不变。
        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                swap.Id.Value,
                "pair:3+4",
                "test-barber-swap",
                1L)).Kind);
        Assert.Equal("no-dashii", CharacterOf(host, 3));
        Assert.Equal("dreamer", CharacterOf(host, 4));
        Assert.Equal(Alignment.Good, AlignmentOf(host, 3));
        Assert.Equal(Alignment.Evil, AlignmentOf(host, 4));

        // 筑梦师槽位尚未进入：重绑给新持有者 4 号，当夜由 4 号被唤醒；旧持有者 3 号不再收请求。
        var dreamer = await WaitForRequestAsync(host, new SeatId(4));
        Assert.Equal(new SeatId(4), dreamer.Addressee);
        Assert.Null(host.Session.GetPlayerView(new SeatId(3)).PendingRequest);
        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                dreamer.Id.Value,
                "seat:1",
                "test-barber-dream",
                1L)).Kind);

        await CompleteNightAsync(storyteller, "barber-2");

        // 事件流证据：两条角色变化只写角色维度、带「变化前角色」；事实以「交换」说明关闭。
        var stored = (await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None)).ToArray();
        var swaps = stored
            .Select(item => item.Event)
            .OfType<SeatStateChangedEvent>()
            .Where(gameEvent => gameEvent.Character is not null
                && gameEvent.Reason.Contains("理发师", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(
            swaps,
            change => change.Seat == new SeatId(3)
                && change.Character == new CharacterId("no-dashii")
                && change.PreviousCharacter == new CharacterId("dreamer"));
        Assert.Contains(
            swaps,
            change => change.Seat == new SeatId(4)
                && change.Character == new CharacterId("dreamer")
                && change.PreviousCharacter == new CharacterId("no-dashii"));
        Assert.All(swaps, change => Assert.Null(change.Alignment));
        Assert.Contains(
            stored.Select(item => item.Event).OfType<BarberNightClosedEvent>(),
            gameEvent => gameEvent.Note.Contains("交换", StringComparison.Ordinal));
        Assert.Empty(stored.Select(item => item.Event).OfType<BarberNightSkippedEvent>());

        // 触发格的应答不推进计划：答完后重进本格（重绑在同一批里先落地、下一格之后才进入），
        // 所以"barber"格在事件流里进入两次——这是 R-0032 重绑不漏掉紧邻下一格的证据。
        Assert.Equal(
            2,
            stored.Select(item => item.Event).OfType<SlotEnteredEvent>()
                .Count(gameEvent => gameEvent.SlotId == new StepSlotId("barber")));
    }

    /// <summary>夜里被恶魔杀死 → 同一夜的理发师格立即向恶魔开请求；恶魔摇头 → 不交换、事实显式关闭。</summary>
    [Fact]
    public async Task BarberKilledAtNight_OpensSwapSameNight_AndDeclineClosesFact()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "barber"), (2, "clockmaker"), (3, "dreamer"), (4, "no-dashii"), (5, "klutz")),
            "test-barber-night-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var four = await host.ConnectSeatAsync(new SeatId(4));

        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-barber-night-death-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "barber-night-death-1");

        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-barber-night-death-2");
        Assert.True(
            night.Kind == "Accepted",
            $"StartNight(2) 没被接受：kind={night.Kind} code={night.RejectionCode} msg={night.RejectionMessage}");

        // 诺-达鲺击杀理发师本人：死亡触发立即记「今晚理发」。
        var kill = await WaitForRequestAsync(host, new SeatId(4));
        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                kill.Id.Value,
                "seat:1",
                "test-barber-night-kill",
                1L)).Kind);
        Assert.Equal(LifeState.Dead, LifeOf(host, 1));

        // 「今晚理发」事实进说书人投影（R-0033）：面板据此显示待处理交互。
        var pendingView = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.NotNull(pendingView.BarberNight);
        Assert.Equal(1, pendingView.BarberNight!.Source);

        // 同一夜的理发师格：恶魔（4 号）收到交换请求，摇头 → 不交换。
        var swap = await WaitForRequestAsync(host, new SeatId(4));
        Assert.Contains(swap.Prompt.Options, option => option.Value == "decline");
        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                swap.Id.Value,
                "decline",
                "test-barber-night-decline",
                1L)).Kind);

        Assert.Equal("dreamer", CharacterOf(host, 3));
        Assert.Equal("no-dashii", CharacterOf(host, 4));

        // 摇头收口 → 事实从说书人投影消失（不残留幽灵事实）。
        var closedView = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Null(closedView.BarberNight);

        await CompleteNightAsync(storyteller, "barber-night-death-2");

        var stored = (await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None)).ToArray();
        Assert.Contains(
            stored.Select(item => item.Event).OfType<BarberNightOpenedEvent>(),
            gameEvent => gameEvent.Source == new SeatId(1));
        Assert.Contains(
            stored.Select(item => item.Event).OfType<BarberNightClosedEvent>(),
            gameEvent => gameEvent.Note.Contains("不交换", StringComparison.Ordinal));
        Assert.DoesNotContain(
            stored.Select(item => item.Event).OfType<SeatStateChangedEvent>(),
            gameEvent => gameEvent.Reason.Contains("理发师", StringComparison.Ordinal));
    }

    /// <summary>理发师格已经走过才死亡 → 过时不候：不开事实、不发请求，只留一条可归因的跳过记录。</summary>
    [Fact]
    public async Task BarberDiedAfterBarberSlot_DoesNotOpen_AndRecordsExpirySkip()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "barber"), (2, "clockmaker"), (3, "dreamer"), (4, "no-dashii"), (5, "klutz")),
            "test-barber-late-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var three = await host.ConnectSeatAsync(new SeatId(3));
        await using var four = await host.ConnectSeatAsync(new SeatId(4));

        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-barber-late-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "barber-late-1");

        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-barber-late-2");
        Assert.True(
            night.Kind == "Accepted",
            $"StartNight(2) 没被接受：kind={night.Kind} code={night.RejectionCode} msg={night.RejectionMessage}");

        // 诺-达鲺先击杀 2 号；随后等到筑梦师请求（理发师格之后），说明理发师格已经走过去。
        var kill = await WaitForRequestAsync(host, new SeatId(4));
        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                kill.Id.Value,
                "seat:2",
                "test-barber-late-kill",
                1L)).Kind);

        var dreamer = await WaitForRequestAsync(host, new SeatId(3));

        // 此刻才上报理发师死亡：已错过当夜理发师格 → 过时不候。
        var late = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            "Dead",
            null,
            null,
            null,
            null,
            "测试：错过时机的理发师死亡",
            null,
            "test-barber-late-death");
        Assert.Equal("Accepted", late.Kind);
        Assert.Equal(LifeState.Dead, LifeOf(host, 1));
        Assert.Null(host.Session.GetPlayerView(new SeatId(4)).PendingRequest);

        Assert.Equal(
            "Accepted",
            (await three.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                dreamer.Id.Value,
                "seat:1",
                "test-barber-late-dream",
                1L)).Kind);
        await CompleteNightAsync(storyteller, "barber-late-2");

        var stored = (await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None)).ToArray();
        var skipped = Assert.Single(stored.Select(item => item.Event).OfType<BarberNightSkippedEvent>());
        Assert.Equal(new SeatId(1), skipped.Seat);
        Assert.Contains("过时不候", skipped.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(stored.Select(item => item.Event).OfType<BarberNightOpenedEvent>(), _ => true);
    }

    /// <summary>
    /// 触发格应答落在配额到点**之后**：重进本格必须为本次进入重新起算配额，不能与上一次配额输入
    /// 共用幂等键——否则第二次「配额到点」会被当成重复命令回放，夜晚永久停在理发师格
    /// （「换手后尚未进入的格重绑」请求时有时无的根因）。
    /// </summary>
    [Fact]
    public async Task BarberSwapAnsweredAfterQuotaElapsed_PlanStillAdvances()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "barber"), (2, "clockmaker"), (3, "dreamer"), (4, "no-dashii"), (5, "klutz")),
            "test-barber-late-quota-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var four = await host.ConnectSeatAsync(new SeatId(4));

        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-barber-late-quota-night-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "late-quota-1");

        // 第 1 个白天：处决理发师（死亡即时落账 → 立即记「今晚理发」）。
        var day = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-barber-late-quota-day-1");
        Assert.True(
            day.Kind == "Accepted",
            $"StartDay 没被接受：kind={day.Kind} code={day.RejectionCode} msg={day.RejectionMessage}");
        var executed = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            "Dead",
            null,
            null,
            null,
            null,
            "测试：处决理发师",
            null,
            "test-barber-late-quota-executed");
        Assert.Equal("Accepted", executed.Kind);
        var closedDay = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-barber-late-quota-close-day");
        Assert.Equal("Accepted", closedDay.Kind);

        // 第 2 夜：诺-达鲺击杀 2 号，随后理发师格向恶魔开交换请求。
        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-barber-late-quota-night-2");
        Assert.True(
            night.Kind == "Accepted",
            $"StartNight(2) 没被接受：kind={night.Kind} code={night.RejectionCode} msg={night.RejectionMessage}");

        var kill = await WaitForRequestAsync(host, new SeatId(4));
        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                kill.Id.Value,
                "seat:2",
                "test-barber-late-quota-kill",
                1L)).Kind);

        var swap = await WaitForRequestAsync(host, new SeatId(4));
        Assert.Contains(swap.Prompt.Options, option => option.Value == "pair:3+4");

        // 关键时序：配额（0.05s / 50ms 节拍）在应答**之前**到点——真实世界里玩家不可能比节拍器还快，
        // 这也正是并行套件下偶发卡死的窗口。等 300ms 足以让节拍器送出并落库那条配额输入。
        await Task.Delay(300);

        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                swap.Id.Value,
                "pair:3+4",
                "test-barber-late-quota-swap",
                1L)).Kind);
        Assert.Equal("no-dashii", CharacterOf(host, 3));
        Assert.Equal("dreamer", CharacterOf(host, 4));

        // 换手后尚未进入的筑梦师格重绑给 4 号并开请求：能等到它就证明重进本格的配额再次生效。
        var dreamer = await WaitForRequestAsync(host, new SeatId(4));
        Assert.Equal(new SeatId(4), dreamer.Addressee);
        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                dreamer.Id.Value,
                "seat:1",
                "test-barber-late-quota-dream",
                1L)).Kind);

        await CompleteNightAsync(storyteller, "late-quota-2");
    }

    /// <summary>席位与角色名称的分配请求形状。</summary>
    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：本组用例只真正结算与理发师有关的几步。</summary>
    private static async Task CompleteNightAsync(GameClient storyteller, string tag)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            if (view.PlanCompleted)
            {
                return;
            }

            var forced = await storyteller.InvokeAsync<CommandResultDto>(
                "ForceAdvance",
                "测试：越过无关槽位",
                $"test-barber-force-{tag}-{attempt}");
            if (forced.Kind == "Rejected" && forced.RejectionCode == "kernel.PlanAlreadyCompleted")
            {
                // 计划在「查视图」与「强推」之间被自动推进走完（0.05s 配额档下的固有竞态）：
                // 目标已经达成，不算失败。
                return;
            }

            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夜晚在 24 次强推内没有走完");
    }

    private static string? CharacterOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .CharacterValue?.Value;

    private static Alignment? AlignmentOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .Alignment?.Value;

    private static LifeState? LifeOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .LifeValue;

    private static async Task<OperationRequest> WaitForRequestAsync(TestServerHost host, SeatId seat)
    {
        var arrived = await TestServerHost.WaitUntilAsync(
            () => host.Session.GetPlayerView(seat).PendingRequest is not null,
            Wait);
        if (!arrived)
        {
            // 并行套件下 0.05s 配额档的夹具曾出现"没人收到请求"：把现场打进失败信息，别让下次只剩超时。
            var view = host.Session.GetStorytellerView();
            Assert.Fail(
                $"席位 {seat.Value} 没有收到操作请求；阶段={view.Phase} "
                + $"槽位={view.CurrentSlotId?.Value ?? "（无）"}（{view.SlotIndex}/{view.SlotCount}）"
                + $" 计划走完={view.PlanCompleted} 待裁定={view.AwaitingDecision?.Id.Value ?? "（无）"}"
                + $" 阻塞={view.BlockedReason ?? "（无）"}");
        }

        return host.Session.GetPlayerView(seat).PendingRequest!;
    }
}
