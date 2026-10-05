using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 死亡触发族 E24 残余行的针对性运行证据（真宿主 + 真 SignalR + 真 SQLite）：
/// 行 12「心上人离场（换角）→ 醉酒立即解除」、行 16「含新字段真重启」，
/// 以及残余点名的「醉酒目标可为已死亡玩家」（R-0039 第 3 / 4 条）。
/// </summary>
/// <remarks>
/// <para>
/// 夹具与 <see cref="DeathTriggerHostTests"/> 同源（5 席：1 诺-达鲺 / 2 贤者 / 3 心上人 / 4 呆瓜 / 5 理发师），
/// 三个场景都从「白天处决 3 号心上人 → 触发型裁定 → 指定醉酒」出发（走真提名 / 投票 / 处决，不是上报调用）：
/// </para>
/// <list type="bullet">
/// <item>行 12：次夜由恶魔击杀 5 号理发师 → 理发师格唤醒恶魔 → 恶魔把 3 / 4 号角色互换（心上人离场）→
/// 效果由折叠推导终止（SourceLostAbility）、醉酒维度由对账解除，且只动醉酒一维；</item>
/// <item>行 16：裁定挂起（归属 3 号、触发能力非空）时同库真重启 → 判定点 / 归属 / 上下文等价、
/// 事件流不重算（恢复不追加事件）、开夜闸门仍在，重启后仍能完成裁定（新字段穿过重建仍可用）；</item>
/// <item>选已死亡玩家：裁定目标指到已死亡的 3 号本人 → 效果照常落账、死亡席位进入醉酒维度
/// （六维独立；百科《心上人》「任一玩家」无存活限制，R-0039 第 3 条）。</item>
/// </list>
/// <para>
/// 恶魔换用诺-达鲺而不是方古：方古首次击杀外来者即侵染，而本场景要杀的理发师正是外来者
/// （侵染链由 E17 覆盖，这里只借一次无副作用的夜杀）。夹具也避开提前终局：白天处决后 4 人存活，
/// 次夜击杀理发师后仍有 3 人存活。
/// </para>
/// </remarks>
public sealed class DeathTriggerResidueHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private const int DemonSeat = 1;
    private const int SageSeat = 2;
    private const int SweetheartSeat = 3;
    private const int KlutzSeat = 4;
    private const int BarberSeat = 5;

    /// <summary>心上人死亡触发的能力标识（效果归因，R-0039）。</summary>
    private const string SweetheartAbilityId = "sweetheart";

    /// <summary>本局唯一的心上人醉酒效果标识：来源 3 号、目标 4 号。</summary>
    private const string SweetheartDrunkEffectId = "sweetheart:3:drunk";

    /// <summary>
    /// 行 12：心上人离场（理发师换角）→ 持续醉酒立即解除。
    /// 效果先于换角处于生效态（来源已死亡但效果不终止），换角后由折叠推导终止 + 维度对账解除，
    /// 且只动醉酒一维（生死 / 阵营不被牵连）。
    /// </summary>
    [Fact]
    public async Task SweetheartLeavesPlay_ReleasesDrunkEffect()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        await using var demon = await host.ConnectSeatAsync(new SeatId(DemonSeat));
        await using var sage = await host.ConnectSeatAsync(new SeatId(SageSeat));
        await using var sweetheart = await host.ConnectSeatAsync(new SeatId(SweetheartSeat));
        await using var klutz = await host.ConnectSeatAsync(new SeatId(KlutzSeat));
        await using var barber = await host.ConnectSeatAsync(new SeatId(BarberSeat));

        await AssignAsync(host, storyteller, "release");
        await FinishFirstNightAsync(host, storyteller, "release");
        await ExecuteSweetheartAsync(host, storyteller, demon, sage, sweetheart, klutz, barber, "release");

        var decision = await WaitForTriggerDecisionAsync(storyteller, "心上人");
        Assert.Equal(SweetheartSeat, decision.AwaitingDecisionSeat);
        var sting = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            decision.AwaitingDecisionId,
            $"seat:{KlutzSeat}",
            null,
            "test-death-trigger-residue-release-sting");
        AssertAccepted(host, "release", "心上人裁定（醉 4 号）", sting);
        await WaitForDrunkAsync(storyteller, KlutzSeat);

        // 来源（3 号心上人）已经死亡，但效果是在死亡之后落账的既成事实：此刻仍生效、未终止（R-0039 第 4 条）。
        var before = host.Session.GetStorytellerView();
        var live = Assert.Single(before.PersistentEffects, effect => effect.Id == new EffectId(SweetheartDrunkEffectId));
        Assert.False(live.IsTerminated);
        Assert.Equal(DrunkState.Drunk, before.Seats.Single(seat => seat.Seat == new SeatId(KlutzSeat)).DrunkValue);

        // 第二夜：1 号恶魔击杀 5 号理发师；理发师格随后唤醒恶魔，由恶魔把 3 / 4 号角色互换。
        var nightTwo = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight", 2, "Original", "test-death-trigger-residue-release-night-2");
        AssertAccepted(host, "release", "开第 2 夜", nightTwo);

        var kill = await WaitForRequestAsync(host, new SeatId(DemonSeat), $"seat:{BarberSeat}", "恶魔击杀请求");
        var killed = await demon.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            kill.Id.Value,
            $"seat:{BarberSeat}",
            "test-death-trigger-residue-release-kill",
            1L);
        AssertAccepted(host, "release", "提交恶魔击杀", killed);
        Assert.Equal(LifeState.Dead, LifeOf(host, BarberSeat));

        var swap = await WaitForRequestAsync(host, new SeatId(DemonSeat), "pair:3+4", "理发师交换请求");
        Assert.Equal(
            "Accepted",
            (await demon.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                swap.Id.Value,
                "pair:3+4",
                "test-death-trigger-residue-release-swap",
                1L)).Kind);

        // 换角落账：心上人离开 3 号席位（角色只换，生死 / 阵营不动）。
        Assert.Equal("klutz", CharacterOf(host, SweetheartSeat));
        Assert.Equal("sweetheart", CharacterOf(host, KlutzSeat));
        Assert.Equal(Alignment.Good, AlignmentOf(host, SweetheartSeat));
        Assert.Equal(Alignment.Good, AlignmentOf(host, KlutzSeat));

        // 来源换角 → 效果终止（SourceLostAbility）→ 维度对账解除醉酒。
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => DrunkReleased(host), Wait),
            "心上人离场后醉酒没有解除（效果未终止或醉酒维度未解除）");
        var after = host.Session.GetStorytellerView();
        var terminated = Assert.Single(
            after.PersistentEffects,
            effect => effect.Id == new EffectId(SweetheartDrunkEffectId));
        Assert.Equal(EffectTerminationKind.SourceLostAbility, terminated.Termination?.Kind);
        Assert.Equal(new AbilityId(SweetheartAbilityId), terminated.Ability);
        Assert.Equal(new SeatId(KlutzSeat), terminated.Target);
        Assert.Equal(new CharacterId("sweetheart"), terminated.SourceCharacter);
        Assert.NotEqual(DrunkState.Drunk, after.Seats.Single(seat => seat.Seat == new SeatId(KlutzSeat)).DrunkValue);
        // 解除只动醉酒一维：生死不被牵连（六维独立）。
        Assert.Equal(LifeState.Alive, LifeOf(host, KlutzSeat));

        // 事件流证据：解除是一笔带原效果链接的可归因状态变化（引擎义务，D-0015 推论 1）。
        var stored = (await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None)).ToArray();
        Assert.Contains(
            stored.Select(item => item.Event).OfType<SeatStateChangedEvent>(),
            changed => changed.Seat == new SeatId(KlutzSeat)
                && changed.Drunk == DrunkState.Sober
                && changed.EffectId == new EffectId(SweetheartDrunkEffectId)
                && changed.Reason.Contains("醉酒解除", StringComparison.Ordinal));

        // 换角之后夜晚照常收口（触发格交互不挡住剩余槽位）。
        await WaitForViewAsync(storyteller, view => view.PlanCompleted, "第二夜在换角后没有自然收口");
    }

    /// <summary>
    /// 行 16：触发型裁定挂起时**真重启**（同库第二宿主）——E24 / E25 新增字段（触发能力 / 归属席位）
    /// 非空时穿过重建：判定点等价、事件流不重算、开夜闸门仍在，重启后仍能完成裁定。
    /// </summary>
    [Fact]
    public async Task PendingTriggerDecision_WithNewFields_SurvivesRealRestart()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-death-trigger-residue-{Guid.NewGuid():N}.db");
        try
        {
            string decisionId;
            long eventsBefore;
            await using (var first = new TestServerHost(
                slotQuotaSeconds: 0.05,
                seatCount: 5,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false,
                autoStartTestNight: false))
            {
                await using var storyteller = await first.ConnectStorytellerAsync();
                await using var demon = await first.ConnectSeatAsync(new SeatId(DemonSeat));
                await using var sage = await first.ConnectSeatAsync(new SeatId(SageSeat));
                await using var sweetheart = await first.ConnectSeatAsync(new SeatId(SweetheartSeat));
                await using var klutz = await first.ConnectSeatAsync(new SeatId(KlutzSeat));
                await using var barber = await first.ConnectSeatAsync(new SeatId(BarberSeat));

                await AssignAsync(first, storyteller, "restart");
                await FinishFirstNightAsync(first, storyteller, "restart");
                await ExecuteSweetheartAsync(first, storyteller, demon, sage, sweetheart, klutz, barber, "restart");

                var pending = await WaitForTriggerDecisionAsync(storyteller, "心上人");
                Assert.Equal(SweetheartSeat, pending.AwaitingDecisionSeat);
                decisionId = pending.AwaitingDecisionId!;

                // 冻结前的账：挂起的那笔裁定事件本身就带着新字段（触发能力 + 归属席位）。
                // 裁定挂起时白天计划已收口、没有在跑的配额头，事件流是冻结的，可直接比较条数。
                var before = (await first.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None))
                    .ToArray();
                eventsBefore = before.Length;
                var raised = Assert.Single(
                    before.Select(item => item.Event).OfType<DecisionPointRaisedEvent>(),
                    gameEvent => gameEvent.DecisionPoint.Id == new DecisionPointId(decisionId));
                Assert.Equal(new SeatId(SweetheartSeat), raised.AttributionSeat);
                Assert.NotNull(raised.TriggerAbility);
            }

            // 真重启：同一个数据库、新宿主从事件流恢复。
            await using var restarted = new TestServerHost(
                slotQuotaSeconds: 0.05,
                seatCount: 5,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false,
                autoStartTestNight: false);
            await using var storytellerAfter = await restarted.ConnectStorytellerAsync();

            var restored = await WaitForViewAsync(
                storytellerAfter,
                view => view.AwaitingDecisionId == decisionId,
                "重启后触发型裁定没有恢复");
            Assert.Equal(SweetheartSeat, restored.AwaitingDecisionSeat);
            Assert.Contains("心上人", restored.AwaitingDecisionContext ?? string.Empty, StringComparison.Ordinal);

            // 恢复不重算：事件流条数原样。
            var eventsAfter = await restarted.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
            Assert.Equal(eventsBefore, eventsAfter.Count);

            // 闸门随状态一起恢复：裁定未了结前开夜仍被拒（R-0039 第 6 条）。
            var blocked = await storytellerAfter.InvokeAsync<CommandResultDto>(
                "StartNight", 2, "Original", "test-death-trigger-residue-restart-blocked");
            Assert.Equal("Rejected", blocked.Kind);
            Assert.Equal("phase.trigger_choice_pending", blocked.RejectionCode);

            // 重启后仍可裁定：归属 3 号、触发能力 sweetheart 都还认得回来。
            var sting = await storytellerAfter.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                decisionId,
                $"seat:{KlutzSeat}",
                null,
                "test-death-trigger-residue-restart-sting");
            AssertAccepted(restarted, "restart", "重启后裁定（醉 4 号）", sting);
            await WaitForDrunkAsync(storytellerAfter, KlutzSeat);

            // 效果事件里 E24 新字段非空：既成事实类（来源死亡不终止）效果。
            var applied = Assert.Single(
                (await restarted.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None))
                    .Select(item => item.Event)
                    .OfType<PersistentEffectAppliedEvent>(),
                gameEvent => gameEvent.Effect.Source == new SeatId(SweetheartSeat));
            Assert.Equal(new EffectId(SweetheartDrunkEffectId), applied.Effect.Id);
            Assert.Equal(new SeatId(KlutzSeat), applied.Effect.Target);
            Assert.Equal(new CharacterId("sweetheart"), applied.Effect.SourceCharacter);
            Assert.Equal(EffectDimension.Drunk, applied.Effect.Dimension);
            Assert.True(applied.Effect.SourceStateIndependent);
        }
        finally
        {
            DeleteFiles(databasePath);
        }
    }

    /// <summary>
    /// 残余「选已死亡玩家」：裁定目标指到已死亡的 3 号本人——效果照常落账、死亡席位进入醉酒维度
    /// （百科《心上人》「任一玩家」无存活限制；六维独立，死亡不挡醉酒）。
    /// </summary>
    [Fact]
    public async Task SweetheartDrunkTarget_MayBeDeadSeat()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        await using var demon = await host.ConnectSeatAsync(new SeatId(DemonSeat));
        await using var sage = await host.ConnectSeatAsync(new SeatId(SageSeat));
        await using var sweetheart = await host.ConnectSeatAsync(new SeatId(SweetheartSeat));
        await using var klutz = await host.ConnectSeatAsync(new SeatId(KlutzSeat));
        await using var barber = await host.ConnectSeatAsync(new SeatId(BarberSeat));

        await AssignAsync(host, storyteller, "dead-target");
        await FinishFirstNightAsync(host, storyteller, "dead-target");
        await ExecuteSweetheartAsync(host, storyteller, demon, sage, sweetheart, klutz, barber, "dead-target");

        var decision = await WaitForTriggerDecisionAsync(storyteller, "心上人");
        var sting = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            decision.AwaitingDecisionId,
            $"seat:{SweetheartSeat}",
            null,
            "test-death-trigger-residue-dead-target-sting");
        AssertAccepted(host, "dead-target", "心上人裁定（醉死亡席位）", sting);
        await WaitForDrunkAsync(storyteller, SweetheartSeat);

        var view = host.Session.GetStorytellerView();
        var effect = Assert.Single(
            view.PersistentEffects,
            item => item.Id == new EffectId(SweetheartDrunkEffectId));
        Assert.Equal(new SeatId(SweetheartSeat), effect.Target);
        Assert.Equal(EffectDimension.Drunk, effect.Dimension);
        Assert.False(effect.IsTerminated);
        var dead = view.Seats.Single(seat => seat.Seat == new SeatId(SweetheartSeat));
        Assert.Equal(LifeState.Dead, dead.LifeValue);
        Assert.Equal(DrunkState.Drunk, dead.DrunkValue);
        // 效果只落在裁定目标上：4 号没有被顺带标醉酒。
        Assert.NotEqual(DrunkState.Drunk, view.Seats.Single(seat => seat.Seat == new SeatId(KlutzSeat)).DrunkValue);
    }

    /// <summary>分配 5 席固定花名册（夹具口径见类注释）。</summary>
    private static async Task AssignAsync(TestServerHost host, GameClient storyteller, string tag)
    {
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats(
                (DemonSeat, "no-dashii"),
                (SageSeat, "sage"),
                (SweetheartSeat, "sweetheart"),
                (KlutzSeat, "klutz"),
                (BarberSeat, "barber")),
            $"test-death-trigger-residue-{tag}-assign");
        AssertAccepted(host, tag, "分配花名册", assigned);
    }

    /// <summary>首夜：五席都没有首夜行动格 → 配额走完即自然收口。</summary>
    private static async Task FinishFirstNightAsync(TestServerHost host, GameClient storyteller, string tag)
    {
        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            $"test-death-trigger-residue-{tag}-night-1");
        AssertAccepted(host, tag, "开首夜", night);
        await WaitForViewAsync(storyteller, view => view.PlanCompleted, "首夜没有自然走完");
    }

    /// <summary>白天 1：3 号自我提名 → 四票赞成 → 计票 → 结束白天（处决 3 号心上人，触发裁定点）。</summary>
    private static async Task ExecuteSweetheartAsync(
        TestServerHost host,
        GameClient storyteller,
        GameClient demon,
        GameClient sage,
        GameClient sweetheart,
        GameClient klutz,
        GameClient barber,
        string tag)
    {
        var day = await storyteller.InvokeAsync<CommandResultDto>(
            "StartDay",
            $"test-death-trigger-residue-{tag}-day-start");
        AssertAccepted(host, tag, "开白天", day);
        await WaitForViewAsync(
            storyteller,
            view => view.Day is { Status: "Open", DayNumber: 1 },
            "白天没有进入 Open 状态");

        var nominated = await sweetheart.InvokeAsync<CommandResultDto>(
            "Nominate",
            SweetheartSeat,
            $"test-death-trigger-residue-{tag}-nominate");
        AssertAccepted(host, tag, "心上人自我提名", nominated);
        await VoteSweepTestDriver.StartAsync(host, 1, $"test-death-trigger-residue-{tag}-sweep:start");

        var voters = new[] { demon, sage, klutz, barber };
        var voterSeats = new[] { DemonSeat, SageSeat, KlutzSeat, BarberSeat };
        for (var index = 0; index < voters.Length; index++)
        {
            var voted = await voters[index].InvokeAsync<CommandResultDto>(
                "CastVote",
                1,
                true,
                $"test-death-trigger-residue-{tag}-vote-{index}");
            AssertAccepted(host, tag, $"{voterSeats[index]} 号投票", voted);
        }

        await VoteSweepTestDriver.CollectAllAsync(host, 1, 5, $"test-death-trigger-residue-{tag}-sweep");

        var counted = await storyteller.InvokeAsync<CommandResultDto>(
            "CountVotes",
            1,
            $"test-death-trigger-residue-{tag}-count");
        AssertAccepted(host, tag, "计票", counted);

        var closed = await storyteller.InvokeAsync<CommandResultDto>(
            "CloseDay",
            $"test-death-trigger-residue-{tag}-close");
        AssertAccepted(host, tag, "结束白天", closed);
    }

    /// <summary>
    /// 断言一条命令被受理；不被受理时把**服务端给的原因与宿主日志**一起打出来
    /// （本用例曾在全量并行下偶发一次 `Kind = Failed`，当时的输出只有 `Failed` 三个字，
    /// 查不出是哪一步、为什么——这条断言把那次的取证缺口补上）。
    /// </summary>
    private static void AssertAccepted(
        TestServerHost host,
        string tag,
        string step,
        CommandResultDto result)
    {
        if (result.Kind == "Accepted")
        {
            return;
        }

        var logs = string.Join(
            Environment.NewLine,
            host.Logs.TakeLast(30));
        Assert.Fail(
            $"{tag}：{step}被拒 → Kind={result.Kind}"
            + $" / 拒绝码={result.RejectionCode ?? "无"} / 拒绝说明={result.RejectionMessage ?? "无"}"
            + $" / 失败说明={result.Failure ?? "无"} / 序号={result.Sequence}"
            + $"{Environment.NewLine}--- 宿主日志（最近 30 行）---{Environment.NewLine}{logs}");
    }

    private static async Task<StorytellerViewDto> WaitForTriggerDecisionAsync(GameClient storyteller, string token)
    {
        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.AwaitingDecisionId is not null
                && (candidate.AwaitingDecisionContext ?? string.Empty).Contains(token, StringComparison.Ordinal),
            Wait);
        Assert.True(
            view is { AwaitingDecisionId: not null }
                && (view.AwaitingDecisionContext ?? string.Empty).Contains(token, StringComparison.Ordinal),
            $"没有等到含「{token}」的触发型裁定（最后视图挂起={view?.AwaitingDecisionId ?? "无"}）");
        return view!;
    }

    private static async Task WaitForDrunkAsync(GameClient storyteller, int seat)
    {
        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.Seats.Any(item => item.Seat == seat
                && item.Facts.Any(fact => fact.Dimension == "Drunk" && fact.Value == "Drunk")),
            Wait);
        Assert.True(
            view is not null && view.Seats.Any(item => item.Seat == seat
                && item.Facts.Any(fact => fact.Dimension == "Drunk" && fact.Value == "Drunk")),
            $"{seat} 号没有进入醉酒状态（维度对账未落地）");
    }

    /// <summary>效果已终止（SourceLostAbility）且目标席位的醉酒维度已解除。</summary>
    private static bool DrunkReleased(TestServerHost host)
    {
        var view = host.Session.GetStorytellerView();
        var effect = view.PersistentEffects.SingleOrDefault(item => item.Id == new EffectId(SweetheartDrunkEffectId));
        var target = view.Seats.SingleOrDefault(item => item.Seat == new SeatId(KlutzSeat));
        return effect is { IsTerminated: true, Termination.Kind: EffectTerminationKind.SourceLostAbility }
            && target is not null
            && target.DrunkValue != DrunkState.Drunk;
    }

    /// <summary>轮询某席位收到**指定选项**的请求（触发格请求与恶魔夜杀请求共用一个连接，必须按选项区分）。</summary>
    private static async Task<OperationRequest> WaitForRequestAsync(
        TestServerHost host,
        SeatId seat,
        string expectedOption,
        string label)
    {
        OperationRequest? request = null;
        var arrived = await TestServerHost.WaitUntilAsync(
            () =>
            {
                var pending = host.Session.GetPlayerView(seat).PendingRequest;
                if (pending is null)
                {
                    return false;
                }

                request = pending;
                return pending.Prompt.Options.Any(option => option.Value == expectedOption);
            },
            Wait);
        if (!arrived)
        {
            var view = host.Session.GetStorytellerView();
            Assert.Fail(
                $"{label}（{expectedOption}）没有到达 {seat.Value} 号：阶段={view.Phase} "
                    + $"槽位={view.CurrentSlotId?.Value ?? "（无）"}（{view.SlotIndex}/{view.SlotCount}）"
                    + $" 计划走完={view.PlanCompleted} 待裁定={view.AwaitingDecision?.Id.Value ?? "（无）"}");
        }

        return request!;
    }

    /// <summary>轮询说书人视图直到条件成立；超时后**复查谓词**，避免「返回最后视图」变成恒真断言。</summary>
    private static async Task<StorytellerViewDto> WaitForViewAsync(
        GameClient storyteller,
        Func<StorytellerViewDto, bool> predicate,
        string because)
    {
        var view = await TestServerHost.WaitForViewAsync(storyteller, predicate, Wait);
        Assert.True(view is not null && predicate(view), because);
        return view!;
    }

    private static string? CharacterOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .CharacterValue?.Value;

    private static LifeState? LifeOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .LifeValue;

    private static Alignment? AlignmentOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .Alignment?.Value;

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] seats) =>
        [.. seats.Select(item => new SeatCharacterAssignmentDto
        {
            Seat = item.Seat,
            Character = item.Character,
        })];

    private static void DeleteFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
