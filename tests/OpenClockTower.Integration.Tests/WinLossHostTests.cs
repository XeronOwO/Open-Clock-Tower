using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 胜败判定与游戏结束在真实宿主里的行为（R-0024 / R-0026 / R-0027）：
/// 涡流黄昏无人被处决 → 邪恶获胜；呆瓜死亡选择选中邪恶 → 其阵营落败；结束后一切命令被拒、结束面同源下发。
/// </summary>
/// <remarks>
/// 真宿主 + 真 SignalR + 真 SQLite；夜晚用强推走完（说书人兜底，D-0014），只真正结算与角色有关的那一步。
/// </remarks>
public sealed class WinLossHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>涡流在场、白天无人被处决 → 黄昏邪恶获胜；结束后说书人命令也被拒（R-0026）。</summary>
    [Fact]
    public async Task VortoxNoExecution_EvilWins_AndEndedGameRejectsCommands()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "vortox" },
                new() { Seat = 2, Character = "clockmaker" },
                new() { Seat = 3, Character = "dreamer" },
                new() { Seat = 4, Character = "witch" },
                new() { Seat = 5, Character = "klutz" },
            },
            "test-winloss-vortox-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-winloss-vortox-night-1");
        Assert.Equal("Accepted", night.Kind);
        await CompleteNightAsync(storyteller, "vortox-1");

        var day = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-winloss-vortox-day-1");
        Assert.Equal("Accepted", day.Kind);

        // 不处决任何人 → 结束白天即黄昏 → 涡流条件成立。
        var closed = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-winloss-vortox-close");
        Assert.Equal("Accepted", closed.Kind);

        var ended = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Outcome is not null,
            Wait);
        Assert.NotNull(ended);
        Assert.Equal("Evil", ended!.Outcome!.Winner);
        Assert.Equal("VortoxNoExecution", ended.Outcome.Condition);

        // 结束后一切命令被拒（含说书人）：不能再开夜。
        var rejected = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-winloss-vortox-night-2");
        Assert.Equal("Rejected", rejected.Kind);
        Assert.Equal("phase.game_ended", rejected.RejectionCode);

        // 结束面同源：玩家视图与重连快照都带同一份结论。
        var playerView = host.Session.GetPlayerView(new SeatId(2));
        Assert.NotNull(playerView.Outcome);
        Assert.Equal(Alignment.Evil, playerView.Outcome!.Winner);

        var bundle = await host.Session.GetReconnectBundleAsync(new SeatId(2), 0, CancellationToken.None);
        Assert.NotNull(bundle.View.Outcome);
        Assert.Equal(Alignment.Evil, bundle.View.Outcome!.Winner);
    }

    /// <summary>呆瓜被处决 → 死亡即时公告并开出公开选择；选中邪恶 → 呆瓜阵营落败、游戏结束（R-0027）。</summary>
    [Fact]
    public async Task KlutzExecuted_ChoiceEndsTheGame_WithPublicChoiceSurface()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "klutz" },
                new() { Seat = 2, Character = "clockmaker" },
                new() { Seat = 3, Character = "dreamer" },
                new() { Seat = 4, Character = "no-dashii" },
                new() { Seat = 5, Character = "witch" },
            },
            "test-winloss-klutz-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        await using var two = await host.ConnectSeatAsync(new SeatId(2));
        await using var three = await host.ConnectSeatAsync(new SeatId(3));
        await using var five = await host.ConnectSeatAsync(new SeatId(5));

        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-winloss-klutz-night-1");
        Assert.Equal("Accepted", night.Kind);
        await CompleteNightAsync(storyteller, "klutz-1");

        var day = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-winloss-klutz-day-1");
        Assert.Equal("Accepted", day.Kind);

        // 处决 1 号（呆瓜）：3 票 ≥ 5 名存活的一半。
        Assert.Equal(
            "Accepted",
            (await two.InvokeAsync<CommandResultDto>("Nominate", 1, "test-winloss-klutz-nominate")).Kind);
        await VoteSweepTestDriver.StartAsync(host, 1, "test-winloss-klutz-sweep:start");
        Assert.Equal(
            "Accepted",
            (await two.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-winloss-klutz-vote-2")).Kind);
        Assert.Equal(
            "Accepted",
            (await three.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-winloss-klutz-vote-3")).Kind);
        Assert.Equal(
            "Accepted",
            (await five.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-winloss-klutz-vote-5")).Kind);
        await VoteSweepTestDriver.CollectAllAsync(host, 1, 5, "test-winloss-klutz-sweep");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-winloss-klutz-count")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-winloss-klutz-close")).Kind);

        // 呆瓜死亡即时公告 → 当场开出一条**触发来源**的公开选择（没有槽位）。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(new SeatId(1)).PendingRequest is not null,
                Wait),
            "呆瓜没有收到死亡选择请求");
        var pending = host.Session.GetPlayerView(new SeatId(1)).PendingRequest!;
        Assert.Equal(OperationRequestOriginKind.Trigger, pending.Origin.Kind);
        Assert.Contains("呆瓜", pending.Prompt.Context, StringComparison.Ordinal);
        Assert.DoesNotContain("seat:1", pending.Prompt.Options.Select(option => option.Value));

        // 选中存活中的邪恶方（4 号恶魔）→ 呆瓜阵营（善良）落败 → 邪恶获胜。
        var answered = await one.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            pending.Id.Value,
            "seat:4",
            "test-winloss-klutz-answer",
            1L);
        Assert.Equal("Accepted", answered.Kind);

        var ended = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Outcome is not null,
            Wait);
        Assert.NotNull(ended);
        Assert.Equal("Evil", ended!.Outcome!.Winner);
        Assert.Equal("KlutzChoiceFactionLoses", ended.Outcome.Condition);

        // 公开选择与结束面对旁观玩家同源（呆瓜的选择本身就是公开事实）。
        var bystander = host.Session.GetPlayerView(new SeatId(2));
        Assert.Contains(bystander.KlutzChoices, record => record.Target == new SeatId(4));
        Assert.NotNull(bystander.Outcome);
        Assert.Equal(Alignment.Evil, bystander.Outcome!.Winner);

        // 请求在结束前已正常了结（Answered）→ 结束批次不产生多余作废事件（票据矩阵行 3）。
        // 只比较**答题之后**的区段：答完即结束的那一批不许再补作废；
        // 此前的夜晚槽位由强推越过，强推自身的作废（StorytellerTakeover）不在此列。
        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var answeredSequence = Assert.Single(stored, item => item.Event is OperationRequestAnsweredEvent).Sequence;
        Assert.DoesNotContain(
            stored,
            item => item.Event is OperationRequestVoidedEvent && item.Sequence > answeredSequence);

        // 结束后开夜被拒。
        var rejected = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-winloss-klutz-night-2");
        Assert.Equal("Rejected", rejected.Kind);
        Assert.Equal("phase.game_ended", rejected.RejectionCode);
    }

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：本用例只真正结算与角色有关的那一步。</summary>
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
                $"test-winloss-force-{tag}-{attempt}");
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
}
