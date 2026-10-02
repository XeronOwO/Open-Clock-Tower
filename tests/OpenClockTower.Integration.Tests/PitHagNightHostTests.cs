using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 麻脸巫婆之夜在真实宿主里的整条链路（口径见 <c>docs/standard/rulings.md</c> R-0030）：
/// 造出恶魔 → 它当夜被唤醒并行动 → 窗口内的恶魔击杀成为**待定死亡** →
/// 说书人裁定（阻止 / 追加死亡）→ 越过恶魔段后窗口收口并拒绝后续命令。
/// </summary>
/// <remarks>
/// 真宿主 + 真 SignalR + 真 SQLite；其他夜晚用 0.05 秒配额自动推进，只真正结算与本票有关的几步。
/// </remarks>
public sealed class PitHagNightHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 1 号麻脸巫婆、4 号诺-达鲺；2 / 3 / 5 号都不在其他夜晚的顺序表上（少几个挂起点）。
    /// </summary>
    [Fact]
    public async Task PitHagCreatesDemon_AdjudicatesDeaths_AndClosesWindow()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "pit-hag" },
                new() { Seat = 2, Character = "clockmaker" },
                new() { Seat = 3, Character = "artist" },
                new() { Seat = 4, Character = "no-dashii" },
                new() { Seat = 5, Character = "klutz" },
            },
            "test-pithag-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        await using var three = await host.ConnectSeatAsync(new SeatId(3));
        await using var four = await host.ConnectSeatAsync(new SeatId(4));

        // 先走完第 1 夜：本局还没开过阶段时首夜必须是第 1 夜，而麻脸巫婆只在**其他夜晚**行动。
        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-pithag-night-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "pithag-1");

        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-pithag-night-2");
        Assert.True(
            night.Kind == "Accepted",
            $"StartNight(2) 没被接受：kind={night.Kind} code={night.RejectionCode} msg={night.RejectionMessage} failure={night.Failure}");

        // 麻脸巫婆把 3 号（艺术家）变成**涡流**——一个不在场的恶魔（阵营不变：他仍是善良）。
        var pitHag = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Contains("角色", pitHag.Prompt.Context, StringComparison.Ordinal);
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                pitHag.Id.Value,
                "seat:3|vortox",
                "test-pithag-transform",
                1L)).Kind);

        // 角色变更 + 当夜激活 + 窗口开启。
        var opened = await WaitForViewAsync(
            storyteller,
            view => view.PitHagNight is not null,
            "麻脸巫婆创造恶魔后没有开窗");
        Assert.Equal(1, opened.PitHagNight!.Source);
        Assert.Empty(opened.PitHagNight.Deferred);
        Assert.Equal("vortox", CharacterOf(host, 3));
        Assert.Equal(LifeState.Alive, LifeOf(host, 2));

        // 说书人在窗口内追加死亡：归因为麻脸巫婆。
        var casualty = await storyteller.InvokeAsync<CommandResultDto>(
            "PitHagCasualty",
            2,
            "说书人平衡：让旧线索断掉",
            "test-pithag-casualty-2");
        Assert.Equal("Accepted", casualty.Kind);
        Assert.Equal(LifeState.Dead, LifeOf(host, 2));

        // 4 号诺-达鲺（原本的恶魔）击杀 5 号 → 窗口内成为待定死亡（不直接致死）。
        var noDashii = await WaitForRequestAsync(host, new SeatId(4));
        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                noDashii.Id.Value,
                "seat:5",
                "test-pithag-kill-5",
                1L)).Kind);

        var deferred = await WaitForViewAsync(
            storyteller,
            view => view.PitHagNight?.Deferred.Length == 1,
            "恶魔击杀没有变成待定死亡");
        Assert.Equal(5, deferred.PitHagNight!.Deferred[0].Target);
        Assert.Equal(4, deferred.PitHagNight.Deferred[0].Source);
        Assert.Equal(LifeState.Alive, LifeOf(host, 5));

        // 说书人阻止这次死亡（免死）：目标保持存活，窗口继续开着。
        var prevented = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDeferredDeath",
            5,
            false,
            "免死",
            "test-pithag-prevent-5");
        Assert.Equal("Accepted", prevented.Kind);
        Assert.Empty(host.Session.GetStorytellerView().PitHagNight!.Deferred);
        Assert.Equal(LifeState.Alive, LifeOf(host, 5));

        // 当夜被创造的涡流（善良恶魔）同样在这一夜行动：它击杀 4 号，待定，随后按默认结果生效。
        var vortox = await WaitForRequestAsync(host, new SeatId(3));
        Assert.Equal(
            "Accepted",
            (await three.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                vortox.Id.Value,
                "seat:4",
                "test-pithag-kill-4",
                1L)).Kind);

        // 越过最后一个恶魔槽位 → 窗口收口；未裁定的待定死亡按恶魔攻击的自然结果生效。
        var closed = await WaitForViewAsync(
            storyteller,
            view => view.PitHagNight is null,
            "窗口没有在恶魔段之后收口");
        Assert.Null(closed.PitHagNight);
        Assert.Equal(LifeState.Dead, LifeOf(host, 4));

        // 收口之后两条命令都被显式拒绝（不是静默忽略）。
        var late = await storyteller.InvokeAsync<CommandResultDto>(
            "PitHagCasualty",
            5,
            "太晚了",
            "test-pithag-late-casualty");
        Assert.Equal("Rejected", late.Kind);
        Assert.Equal("kernel.NoPitHagNight", late.RejectionCode);
    }

    /// <summary>
    /// E15 判定行 12（残余修复）：麻脸巫婆创造**镜像双子** → 开「选择对立双子」裁定 →
    /// 落 <c>evil-twin.pair</c> 配对效果与双向互认（与首夜同源）。
    /// </summary>
    /// <remarks>
    /// 来源：百科《镜像双子》· 2026-10-01 抓取 · 提示标记（「放置时机：……或有新的镜像双子被创造出来时」
    /// 「选择与当前的镜像双子对立阵营的任意一名玩家」）与提示与技巧（「如果麻脸巫婆在最后一夜创造了一个
    /// 镜像双子，你可以选择恶魔或一名死亡的玩家作为对立双子」——候选按「除新双子外、阵营与之相对的玩家」
    /// 列出，不看生死，已死亡的恶魔玩家同样在列）；配对效果的语义与阻断 / 触发口径见 R-0025。
    /// </remarks>
    [Fact]
    public async Task PitHagCreatesEvilTwin_OpensPairingDecision_AndPersistsPairing()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "pit-hag" },
                new() { Seat = 2, Character = "clockmaker" },
                new() { Seat = 3, Character = "artist" },
                new() { Seat = 4, Character = "no-dashii" },
                new() { Seat = 5, Character = "klutz" },
            },
            "test-pithag-twin-assign");
        Assert.Equal("Accepted", assigned.Kind);

        InformationResultDto? twinInformation = null;
        InformationResultDto? oppositeInformation = null;
        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        await using var three = await host.ConnectSeatAsync(
            new SeatId(3),
            onInformation: information => twinInformation = information);
        await using var four = await host.ConnectSeatAsync(
            new SeatId(4),
            onInformation: information => oppositeInformation = information);

        // 首夜：这五个角色都不在首夜顺序表上，强推走完即可。
        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-pithag-twin-night-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "twin-1");

        // 第二夜：麻脸巫婆把 3 号（艺术家，善良）变成镜像双子（爪牙）——该角色不在场。
        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-pithag-twin-night-2");
        Assert.True(
            night.Kind == "Accepted",
            $"StartNight(2) 没被接受：kind={night.Kind} code={night.RejectionCode} msg={night.RejectionMessage}");

        var pitHag = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                pitHag.Id.Value,
                "seat:3|evil-twin",
                "test-pithag-twin-transform",
                1L)).Kind);

        // 行 12 的核心：创造镜像双子必须开「选择对立双子」裁定。
        // 新双子是善良（3 号原本是镇民），候选 = 邪恶玩家（1 号爪牙、4 号恶魔），不含善良玩家。
        var decision = await WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null,
            "创造镜像双子后没有出现「选择对立双子」裁定");
        Assert.NotNull(decision.AwaitingDecisionOptions);
        Assert.Contains("对立双子", decision.AwaitingDecisionContext, StringComparison.Ordinal);
        Assert.Contains(decision.AwaitingDecisionOptions!, option => option.Value == "seat:1");
        Assert.Contains(decision.AwaitingDecisionOptions!, option => option.Value == "seat:4");
        Assert.DoesNotContain(decision.AwaitingDecisionOptions!, option => option.Value == "seat:2");
        Assert.DoesNotContain(decision.AwaitingDecisionOptions!, option => option.Value == "seat:5");

        // 说书人选择 4 号（诺-达鲺）作为对立双子。
        var resolved = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            decision.AwaitingDecisionId,
            "seat:4",
            null,
            "test-pithag-twin-pair");
        Assert.Equal("Accepted", resolved.Kind);

        // 角色变更 + 配对效果（与首夜同源：evil-twin.pair，来源 = 镜像双子席，目标 = 对立双子席）。
        var paired = await WaitForViewAsync(
            storyteller,
            view => view.Seats.Any(seat =>
                seat.Seat == 3
                && seat.Facts.Any(fact => fact.Dimension == "Character" && fact.Value == "evil-twin")),
            "3 号没有变成镜像双子");
        Assert.Contains(
            paired.Effects,
            effect => effect.Kind == "Persistent"
                && effect.Ability == "evil-twin.pair"
                && effect.Source == 3
                && effect.Target == 4
                && effect.SourceCharacter == "evil-twin"
                && !effect.Terminated);

        // 双向互认：两名双子各自得知对方的角色（信息只发给本人）。
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => twinInformation is not null, Wait),
            "镜像双子没有收到互认信息");
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => oppositeInformation is not null, Wait),
            "对立双子没有收到互认信息");
        Assert.Contains("诺-达鲺", twinInformation!.Content, StringComparison.Ordinal);
        Assert.Contains("镜像双子", oppositeInformation!.Content, StringComparison.Ordinal);
    }

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：本用例只真正结算与本票有关的那几步。</summary>
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
                $"test-pithag-force-{tag}-{attempt}");
            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夜晚在 24 次强推内没有走完");
    }

    private static string? CharacterOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .CharacterValue?.Value;

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

    private static async Task<StorytellerViewDto> WaitForViewAsync(
        GameClient storyteller,
        Func<StorytellerViewDto, bool> predicate,
        string because)
    {
        var view = await TestServerHost.WaitForViewAsync(storyteller, predicate, Wait);
        // WaitForViewAsync 超时会返回**最后一次**视图；这里复查谓词，失败信息才指向真正在等的东西。
        Assert.True(view is not null && predicate(view), because);
        return view!;
    }
}
