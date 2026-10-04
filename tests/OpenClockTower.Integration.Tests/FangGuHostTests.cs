using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 方古在真实宿主里的整条链路（口径见 <c>docs/standard/rulings.md</c> R-0029 / R-0034）：
/// 除首夜外每夜击杀；首次成功命中外来者 → 外来者变成新的邪恶方古、原方古死亡、「限一次」标记落下；
/// 标记整局不复用；新方古在重绑后的方古格行动（旧标记留在已死亡席位上）；
/// 夜晚变化到黎明才公开（R-0022）。
/// </summary>
/// <remarks>
/// 真宿主 + 真 SignalR + 真 SQLite；其他夜晚用 0.05 秒配额自动推进，只真正结算与方古有关的几步。
/// </remarks>
public sealed class FangGuHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 第 2 夜方古击杀 3 号呆瓜（外来者）→ 侵染：3 号变成邪恶方古、1 号死亡、标记落下；
    /// 同一夜不再唤醒新方古（它的格已经走过）；第 3 夜标记已用 → 外来者（5 号）正常死亡。
    /// </summary>
    [Fact]
    public async Task FirstOutsiderKill_Converts_AndMarkerBlocksSecondConversion()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "fang-gu"), (2, "clockmaker"), (3, "klutz"), (4, "dreamer"), (5, "mutant")),
            "test-fang-gu-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        await using var three = await host.ConnectSeatAsync(new SeatId(3));

        // 第 1 夜：方古不在首夜顺序表上（没有它的请求），强推走完其余槽位。
        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-fang-gu-night-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "fang-gu-1");

        // 第 1 个白天：不提名、直接收口（为第 2 夜的公开生死面留一个黄昏基准）。
        var dayOne = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-fang-gu-day-1");
        Assert.True(
            dayOne.Kind == "Accepted",
            $"StartDay 没被接受：kind={dayOne.Kind} code={dayOne.RejectionCode} msg={dayOne.RejectionMessage}");
        var closedOne = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-fang-gu-close-day-1");
        Assert.Equal("Accepted", closedOne.Kind);

        // 第 2 夜：方古选择 3 号呆瓜（外来者）→ 首次成功 → 侵染。
        var nightTwo = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-fang-gu-night-2");
        Assert.True(
            nightTwo.Kind == "Accepted",
            $"StartNight(2) 没被接受：kind={nightTwo.Kind} code={nightTwo.RejectionCode} msg={nightTwo.RejectionMessage}");

        var infect = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                infect.Id.Value,
                "seat:3",
                "test-fang-gu-infect",
                1L)).Kind);

        // 真实账：3 号变成邪恶方古且存活（被攻击者**不死亡**）；1 号死亡但角色标记仍留在魔典上。
        Assert.Equal("fang-gu", CharacterOf(host, 3));
        Assert.Equal(Alignment.Evil, AlignmentOf(host, 3));
        Assert.Equal(LifeState.Alive, LifeOf(host, 3));
        Assert.Equal(LifeState.Dead, LifeOf(host, 1));
        Assert.Equal("fang-gu", CharacterOf(host, 1));
        Assert.Null(host.Session.GetPlayerView(new SeatId(1)).PendingRequest);

        // 公开生死面（R-0022）：夜晚的变化到黎明才公告——无关玩家此刻仍看到 1 号存活。
        Assert.Equal(LifeState.Alive, PublicLifeOf(host, viewer: 2, seat: 1));

        // 新方古的格就是刚结算的这一格（过时不候）：当夜不会有人再唤醒它；
        // 下一格是筑梦师（4 号），请求发给它而不是 3 号。
        await WaitForRequestAsync(host, new SeatId(4));
        Assert.Null(host.Session.GetPlayerView(new SeatId(3)).PendingRequest);

        await CompleteNightAsync(storyteller, "fang-gu-2");

        // 第 2 个白天：黎明公告里出现「1 号死亡」，且公告不含死因。
        var dayTwo = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-fang-gu-day-2");
        Assert.True(
            dayTwo.Kind == "Accepted",
            $"StartDay(2) 没被接受：kind={dayTwo.Kind} code={dayTwo.RejectionCode} msg={dayTwo.RejectionMessage}");
        var dayTwoView = host.Session.GetPlayerView(new SeatId(2)).Day;
        Assert.NotNull(dayTwoView);
        Assert.Contains(
            dayTwoView!.Announcements,
            entry => entry.Seat == new SeatId(1) && entry.State == LifeState.Dead);
        var closedTwo = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-fang-gu-close-day-2");
        Assert.Equal("Accepted", closedTwo.Kind);

        // 第 3 夜：「限一次」已用 → 新方古（3 号）击杀 5 号外来者，5 号正常死亡、不再侵染。
        // 这一夜也验证建表器：两个同名标记（已死亡的原方古 + 存活的新方古）只把格绑给存活者。
        var nightThree = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            3,
            "Original",
            "test-fang-gu-night-3");
        Assert.True(
            nightThree.Kind == "Accepted",
            $"StartNight(3) 没被接受：kind={nightThree.Kind} code={nightThree.RejectionCode} msg={nightThree.RejectionMessage}");

        var secondKill = await WaitForRequestAsync(host, new SeatId(3));
        Assert.Equal(
            "Accepted",
            (await three.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                secondKill.Id.Value,
                "seat:5",
                "test-fang-gu-second-kill",
                1L)).Kind);

        Assert.Equal(LifeState.Dead, LifeOf(host, 5));
        Assert.Equal("fang-gu", CharacterOf(host, 3));
        Assert.Equal(LifeState.Alive, LifeOf(host, 3));
        Assert.Equal("mutant", CharacterOf(host, 5));

        // 「限一次」整局事实进说书人投影（R-0034）：面板在魔典中心显示标记。
        var infectedView = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.NotNull(infectedView.FangGuInfection);
        Assert.Equal(3, infectedView.FangGuInfection!.Seat);
        Assert.Equal(1, infectedView.FangGuInfection.Source);

        await CompleteNightAsync(storyteller, "fang-gu-3");

        // 事件流证据：标记只有一条；侵染的两次变化带「变化前角色」与阵营；被攻击者没有死亡。
        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var infection = Assert.Single(stored.Select(item => item.Event).OfType<FangGuInfectionRecordedEvent>());
        Assert.Equal(new SeatId(3), infection.Seat);
        Assert.Equal(new SeatId(1), infection.Source);

        var conversion = stored
            .Select(item => item.Event)
            .OfType<SeatStateChangedEvent>()
            .Single(change => change.Seat == new SeatId(3) && change.Character == new CharacterId("fang-gu"));
        Assert.Equal(new CharacterId("klutz"), conversion.PreviousCharacter);
        Assert.Equal(Alignment.Evil, conversion.Alignment);
        Assert.Null(conversion.Life);
        Assert.Contains("方古侵染", conversion.Reason, StringComparison.Ordinal);

        var originalDeath = stored
            .Select(item => item.Event)
            .OfType<SeatStateChangedEvent>()
            .Single(change => change.Seat == new SeatId(1) && change.Life == LifeState.Dead);
        Assert.Null(originalDeath.Character);
        Assert.Equal(new SeatId(1), originalDeath.CausedBy);
    }

    /// <summary>席位与角色名称的分配请求形状。</summary>
    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：本组用例只真正结算与方古有关的几步。</summary>
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
                $"test-fang-gu-force-{tag}-{attempt}");
            if (forced.Kind == "Rejected" && forced.RejectionCode == "kernel.PlanAlreadyCompleted")
            {
                // 计划在「查视图」与「强推」之间被自动推进走完（0.05s 配额档下的固有竞态）：
                // 目标已经达成，不算失败。
                return;
            }

            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夜晚在 64 次强推内没有走完");
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

    /// <summary>某名玩家此刻看到的公开生死（R-0022 的公开面；未开过白天时为 null）。</summary>
    private static LifeState? PublicLifeOf(TestServerHost host, int viewer, int seat) =>
        host.Session.GetPlayerView(new SeatId(viewer)).Day?.Lives
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .State;

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
