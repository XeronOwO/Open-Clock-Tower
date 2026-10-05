using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 哲学家在真实宿主里的整条链路（口径见 <c>docs/standard/rulings.md</c> R-0036）：
/// 第 1 夜选择获得在场的筑梦师的能力（不变身）→ 筑梦师持有者醉酒、但**照常被唤醒**（能力不生效）→
/// 第 2 夜在**他自己的格**上代行获得的能力（提示按他重算），持有者依旧醉酒、依旧照常被唤醒。
/// </summary>
/// <remarks>
/// 真宿主 + 真 SignalR + 真 SQLite；其他槽位用 0.05 秒配额自动推进，只真正结算与哲学家有关的几步。
/// </remarks>
public sealed class PhilosopherHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>获得能力 → 持有者醉酒 → 自己的格代行被获得的能力（生效），醉酒者的使用记「不生效」。</summary>
    [Fact]
    public async Task Grant_MakesHolderDrunk_AndDelegatesAbilityNextNight()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "philosopher"), (2, "klutz"), (3, "dreamer"), (4, "mutant"), (5, "barber")),
            "test-philosopher-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        await using var three = await host.ConnectSeatAsync(new SeatId(3));

        // 第 1 夜：哲学家选择获得筑梦师的能力（筑梦师在场 → 它的持有者醉酒）。
        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-philosopher-night-1");
        Assert.Equal("Accepted", firstNight.Kind);

        var grant = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Contains(grant.Prompt.Options, option => option.Value == "dreamer");
        Assert.Contains(grant.Prompt.Options, option => option.Value == "decline");
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                grant.Id.Value,
                "dreamer",
                "test-philosopher-grant",
                1L)).Kind);

        // 真实账：获得能力的事实落账（带被获得的角色），**没有变身**；持有者（3 号）醉酒。
        Assert.Equal("philosopher", CharacterOf(host, 1));
        Assert.Equal("dreamer", CharacterOf(host, 3));
        Assert.Equal(DrunkState.Drunk, DrunkOf(host, 3));

        // 本人视图出口（R-0059）的负向：哲学家「代行」**不写角色维度**（R-0036 第 1 条：不变身），
        // 所以他自己那份视图里的角色必须仍是哲学家——出错了就会变成"代行即变身"。
        var philosopher = host.Session.GetPlayerView(new SeatId(1));
        Assert.Equal("philosopher", philosopher.Character?.Value);
        Assert.Equal(Alignment.Good, philosopher.Alignment);
        var afterGrant = host.Session.GetStorytellerView().PersistentEffects;
        Assert.Contains(
            afterGrant,
            effect => effect.Ability == new AbilityId("philosopher.grant")
                && effect.Source == new SeatId(1)
                && effect.GrantedCharacter == new CharacterId("dreamer"));
        Assert.Contains(
            afterGrant,
            effect => effect.Ability == new AbilityId("philosopher.grant.drunk")
                && effect.Source == new SeatId(1)
                && effect.Target == new SeatId(3)
                && effect.Dimension == EffectDimension.Drunk);

        // 醉酒者**照常被唤醒**：不因为被醉酒就不发请求（那会把"我被醉酒了"泄漏给本人）。
        var drunkDream = await WaitForRequestAsync(host, new SeatId(3));
        Assert.Equal(
            "Accepted",
            (await three.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                drunkDream.Id.Value,
                "seat:2",
                "test-philosopher-drunk-dream",
                1L)).Kind);
        await ResolveAwaitingDecisionAsync(storyteller, "test-philosopher-drunk-info");
        await CompleteNightAsync(storyteller, "philosopher-1");

        // 第 1 个白天：不提名、直接收口。
        var day = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-philosopher-day-1");
        Assert.True(
            day.Kind == "Accepted",
            $"StartDay 没被接受：kind={day.Kind} code={day.RejectionCode} msg={day.RejectionMessage}");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-philosopher-close-day-1")).Kind);

        // 第 2 夜：获得的能力在**他自己的格**上执行——提示是筑梦师的（不能选自己 → 不含 seat:1，也没有摇头）。
        var nightTwo = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-philosopher-night-2");
        Assert.True(
            nightTwo.Kind == "Accepted",
            $"StartNight(2) 没被接受：kind={nightTwo.Kind} code={nightTwo.RejectionCode} msg={nightTwo.RejectionMessage}");

        var delegated = await WaitForRequestAsync(host, new SeatId(1));
        var values = delegated.Prompt.Options.Select(option => option.Value).ToArray();
        Assert.DoesNotContain("seat:1", values);
        Assert.DoesNotContain("decline", values);
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                delegated.Id.Value,
                "seat:2",
                "test-philosopher-delegated",
                1L)).Kind);
        await ResolveAwaitingDecisionAsync(storyteller, "test-philosopher-delegated-info");

        // 醉酒者这一夜同样照常被唤醒（持有者不变 → 醉酒标记仍跟着它）。
        await WaitForRequestAsync(host, new SeatId(3));
        Assert.Equal(DrunkState.Drunk, DrunkOf(host, 3));
        await CompleteNightAsync(storyteller, "philosopher-2");

        // 事件流证据：获得的能力由哲学家**生效**执行；醉酒的筑梦师照常使用但**不生效**（使用照记）。
        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var resolved = stored.Select(item => item.Event).OfType<AbilityResolvedEvent>().ToArray();
        Assert.Contains(
            resolved,
            entry => entry.Actor == new SeatId(1)
                && entry.Ability == new AbilityId("dreamer")
                && entry.Effective);
        Assert.Contains(
            resolved,
            entry => entry.Actor == new SeatId(3)
                && entry.Ability == new AbilityId("dreamer")
                && !entry.Effective);

        var applied = stored.Select(item => item.Event).OfType<PersistentEffectAppliedEvent>().ToArray();
        Assert.Contains(
            applied,
            entry => entry.Effect.Ability == new AbilityId("philosopher.grant")
                && entry.Effect.GrantedCharacter == new CharacterId("dreamer"));
        Assert.Contains(
            applied,
            entry => entry.Effect.Ability == new AbilityId("philosopher.grant.drunk")
                && entry.Effect.Target == new SeatId(3)
                && entry.Effect.Dimension == EffectDimension.Drunk);
    }

    /// <summary>席位与角色名称的分配请求形状。</summary>
    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：本组用例只真正结算与哲学家有关的几步。</summary>
    private static async Task CompleteNightAsync(GameClient storyteller, string tag)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            if (view.PlanCompleted)
            {
                return;
            }

            var forced = await storyteller.InvokeAsync<CommandResultDto>(
                "ForceAdvance",
                "测试：越过无关槽位",
                $"test-philosopher-force-{tag}-{attempt}");
            if (forced.Kind == "Rejected" && forced.RejectionCode == "kernel.PlanAlreadyCompleted")
            {
                return;
            }

            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夜晚在 64 次强推内没有走完");
    }

    /// <summary>等一个裁定点出现并就地裁定（信息内容由说书人裁定，D-0002）。</summary>
    private static async Task ResolveAwaitingDecisionAsync(GameClient storyteller, string key)
    {
        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.AwaitingDecisionId is not null,
            Wait);
        Assert.NotNull(view);
        Assert.NotNull(view!.AwaitingDecisionId);

        var decision = view.AwaitingDecisionOptions is { Length: > 0 } options
            ? options[0].Value
            : "测试：说书人裁定的信息内容";
        var resolved = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            view.AwaitingDecisionId!,
            decision,
            null,
            key);
        Assert.Equal("Accepted", resolved.Kind);
    }

    private static string? CharacterOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .CharacterValue?.Value;

    private static DrunkState? DrunkOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .DrunkValue;

    private static async Task<OperationRequest> WaitForRequestAsync(TestServerHost host, SeatId seat)
    {
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(seat).PendingRequest is not null,
                Wait),
            $"席位 {seat.Value} 没有收到操作请求");

        return host.Session.GetPlayerView(seat).PendingRequest!;
    }
}
