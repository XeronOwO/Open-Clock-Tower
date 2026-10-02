using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 舞蛇人在真实宿主里的整条链路（口径见 <c>docs/standard/rulings.md</c> R-0031 / R-0032）：
/// 命中恶魔 → 双方交换角色与阵营 → 原恶魔永久中毒（来源状态无关）→ 恶魔槽位当夜重绑给新持有者、
/// 新恶魔照常行动；另一条入口（说书人上报换角）同样绑定尚未进入的槽位，已进入的不重绑（过时不候）。
/// </summary>
/// <remarks>
/// 真宿主 + 真 SignalR + 真 SQLite；其他夜晚用 0.05 秒配额自动推进，只真正结算与舞蛇人有关的几步。
/// </remarks>
public sealed class SnakeCharmerHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 第 2 夜：1 号舞蛇人命中 4 号涡流 → 交换角色与阵营、4 号永久中毒；
    /// 本来绑给 4 号的恶魔槽位重绑给 1 号，新恶魔当夜被唤醒并击杀。
    /// </summary>
    [Fact]
    public async Task SnakeCharmerSwapsWithDemon_NewDemonActsSameNight_AndFormerDemonIsPoisoned()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "snake-charmer"), (2, "clockmaker"), (3, "artist"), (4, "vortox"), (5, "klutz")),
            "test-snake-charmer-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        await using var four = await host.ConnectSeatAsync(new SeatId(4));

        // 第 1 夜：先选一个非恶魔（2 号钟表匠）→ 无事发生，角色不变。
        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-snake-charmer-night-1");
        Assert.Equal("Accepted", firstNight.Kind);

        var miss = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                miss.Id.Value,
                "seat:2",
                "test-snake-charmer-miss",
                1L)).Kind);
        Assert.Equal("snake-charmer", CharacterOf(host, 1));
        await CompleteNightAsync(storyteller, "snake-charmer-1");

        // 第 2 夜：命中恶魔。
        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-snake-charmer-night-2");
        Assert.True(
            night.Kind == "Accepted",
            $"StartNight(2) 没被接受：kind={night.Kind} code={night.RejectionCode} msg={night.RejectionMessage}");

        var charmer = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                charmer.Id.Value,
                "seat:4",
                "test-snake-charmer-swap",
                1L)).Kind);

        // 交换：角色与阵营同时换手；原恶魔（现舞蛇人）永久中毒。
        Assert.Equal("vortox", CharacterOf(host, 1));
        Assert.Equal("snake-charmer", CharacterOf(host, 4));
        Assert.Equal(Alignment.Evil, AlignmentOf(host, 1));
        Assert.Equal(Alignment.Good, AlignmentOf(host, 4));
        Assert.Equal(PoisonState.Poisoned, PoisonOf(host, 4));

        var poison = host.Session.GetStorytellerView().PersistentEffects
            .Single(effect => effect.Ability == new AbilityId("snake-charmer.poison"));
        Assert.Equal(new SeatId(4), poison.Source);
        Assert.Equal(new SeatId(4), poison.Target);
        Assert.Equal(EffectDimension.Poison, poison.Dimension);
        Assert.Equal(new CharacterId("snake-charmer"), poison.SourceCharacter);
        Assert.True(poison.SourceStateIndependent);

        // 新恶魔（1 号）当夜在恶魔槽位被唤醒：请求发给 1 号，旧持有者 4 号不再收请求。
        var kill = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Null(host.Session.GetPlayerView(new SeatId(4)).PendingRequest);
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                kill.Id.Value,
                "seat:3",
                "test-snake-charmer-kill",
                1L)).Kind);
        Assert.Equal(LifeState.Dead, LifeOf(host, 3));

        await CompleteNightAsync(storyteller, "snake-charmer-2");

        // 说书人视图里两席的变化与回归口径一致（重放只折事件：直接回读事件流里的两条角色变化）。
        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var swaps = stored
            .Select(item => item.Event)
            .OfType<SeatStateChangedEvent>()
            .Where(gameEvent => gameEvent.Character is not null
                && gameEvent.Reason.Contains("舞蛇人", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(swaps, change => change.Seat == new SeatId(1) && change.Character == new CharacterId("vortox"));
        Assert.Contains(swaps, change => change.Seat == new SeatId(4) && change.Character == new CharacterId("snake-charmer"));
        Assert.All(swaps, change => Assert.NotNull(change.Alignment));
    }

    /// <summary>
    /// 说书人上报换角（另一条入口）：尚未进入的槽位跟随上报绑定（上报创造的涡流当夜被唤醒）；
    /// 已经进入过的槽位不重绑（过时不候）。
    /// </summary>
    [Fact]
    public async Task ReportedCharacterChange_BindsPendingSlot_AndDoesNotRebindEnteredSlot()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "clockmaker"), (2, "artist"), (3, "dreamer"), (4, "no-dashii"), (5, "klutz")),
            "test-manual-bind-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var four = await host.ConnectSeatAsync(new SeatId(4));

        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-manual-bind-night-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "manual-bind-1");

        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-manual-bind-night-2");
        Assert.True(
            night.Kind == "Accepted",
            $"StartNight(2) 没被接受：kind={night.Kind} code={night.RejectionCode} msg={night.RejectionMessage}");

        // 等 4 号诺-达鲺的击杀请求挂起：此刻下标已越过舞蛇人槽位（过时不候），涡流槽位还在后面。
        var noDashii = await WaitForRequestAsync(host, new SeatId(4));
        var slotIndexBefore = host.Session.GetStorytellerView().SlotIndex;

        // 上报 2 号变成涡流：尚未进入的涡流槽位绑定给 2 号（事件流留痕）。
        var reported = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            2,
            null,
            "vortox",
            null,
            null,
            null,
            "测试：上报创造涡流",
            null,
            "test-manual-bind-report");
        Assert.Equal("Accepted", reported.Kind);

        var storedAfterReport = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var binding = storedAfterReport
            .Select(item => item.Event)
            .OfType<SlotActivatedEvent>()
            .Single(gameEvent => gameEvent.SlotId == new StepSlotId("vortox"));
        Assert.Equal(new SeatId(2), binding.Actor);
        Assert.True(binding.SlotIndex > slotIndexBefore);

        // 放行当前的恶魔击杀 → 机器推进到被绑定的涡流槽位：请求发给新持有者 2 号（不是旧持有者）。
        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                noDashii.Id.Value,
                "seat:3",
                "test-manual-bind-kill",
                1L)).Kind);
        var vortoxKill = await WaitForRequestAsync(host, new SeatId(2));
        Assert.Equal(new SeatId(2), vortoxKill.Addressee);

        // 此刻舞蛇人槽位已经走过：上报 5 号变成舞蛇人不应再绑定（过时不候），但事实照记。
        var late = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            5,
            null,
            "snake-charmer",
            null,
            null,
            null,
            "测试：错过时机的换角",
            null,
            "test-manual-bind-late");
        Assert.Equal("Accepted", late.Kind);
        Assert.Equal("snake-charmer", CharacterOf(host, 5));
        Assert.Null(host.Session.GetPlayerView(new SeatId(5)).PendingRequest);

        var storedAfterLate = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        Assert.DoesNotContain(
            storedAfterLate.Select(item => item.Event).OfType<SlotActivatedEvent>(),
            gameEvent => gameEvent.SlotId == new StepSlotId("snake-charmer"));
    }

    /// <summary>席位与角色名称的分配请求形状。</summary>
    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：本组用例只真正结算与舞蛇人有关的几步。</summary>
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
                $"test-snake-charmer-force-{tag}-{attempt}");
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

    private static PoisonState? PoisonOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .PoisonValue;

    private static LifeState? LifeOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .LifeValue;

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
