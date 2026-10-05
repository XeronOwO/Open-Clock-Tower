using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 集骨者重获**白天能力**的真宿主链路（`BoneCollectorHostTests` 的白天族，2026-10-05 按单文件
/// 600 行门禁拆出）：死亡但被重获能力的杂耍艺人当夜在自己的格上被唤醒（说书人的报数裁定点），
/// 窗口存续的那个白天重新拿到公开猜测入口、命令被受理，下个黄昏到期后收回。
/// 跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 规则来源：百科《集骨者》· 2026-10-04 抓取（角色能力 / 角色简介 2，「即使先前已经用过」）、
/// 百科《杂耍艺人》· 2026-10-01 抓取（角色能力：首个白天公开猜测、当晚得知数量）；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0054 第 6 条与 R-0057-B。
/// </remarks>
public sealed class BoneCollectorDayEntryHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 重获的是**白天**能力（杂耍艺人的公开猜测，R-0057-B 第 4 条）：死亡但被重获能力的杂耍艺人
    /// 在窗口存续的那个白天**在真宿主链路上**也能再猜一次——他的入口重新出现、命令被受理；
    /// 当夜那一格也必须真的被唤醒（说书人裁定点，不是静默停住）。
    /// </summary>
    /// <remarks>
    /// 内核（<c>JugglerGuessMachine</c>）与投影（<c>DayProjection</c>）两侧各有单测，本用例补的是
    /// **命令面到白天账**这一整条真宿主链路：票据
    /// `done/bone-collector-regained-juggler-day-entry.md` 的界面级取证由
    /// `tools/verify-bone-collector-juggler.mjs` 判，这里护住"受理"与"当夜被唤醒"这两件事。
    /// </remarks>
    [Fact]
    public async Task BoneCollector_RegainingDayAbility_LetTheDeadJugglerGuessAgainOnThatDay()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        // 1 号杂耍艺人（白天族）/ 2 号钟表匠（首夜行动，用来走完首夜）/ 3 号畸形秀演员 /
        // 4 号呆瓜 / 5 号方古（其他夜晚击杀）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "AssignCharacters",
                Seats((1, "juggler"), (2, "clockmaker"), (3, "mutant"), (4, "klutz"), (5, "fang-gu")),
                "test-bone-juggler-assign")).Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "bone-collector",
            "Good",
            null,
            "test-bone-juggler-join");
        Assert.Equal("Accepted", joined.Kind);

        OperationRequestDto? collectorRequest = null;
        await using var collector = await host.ConnectSeatAsync(new SeatId(6), asked => collectorRequest = asked);
        await using var juggler = await host.ConnectSeatAsync(new SeatId(1));

        // 恶魔击杀格要有人提交：第 2 夜的击杀格排在重获格之后，不答它就永远停在"等他说"上
        // （那不是集骨者这一族的问题，见下面的自然走完断言）。
        OperationRequestDto? demonRequest = null;
        await using var demon = await host.ConnectSeatAsync(new SeatId(5), asked => demonRequest = asked);

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                1,
                "Original",
                "test-bone-juggler-night-1")).Kind);
        var clockmakerInfo = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.CurrentSlotId == "clockmaker" && view.AwaitingDecisionId is not null,
            Wait);
        Assert.NotNull(clockmakerInfo);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                clockmakerInfo!.AwaitingDecisionId,
                "恶魔离最近的爪牙有 1 名玩家",
                null,
                "test-bone-juggler-night-1-info")).Kind);
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "首夜没有自然走完");

        // 白天 1：杂耍艺人活着猜一次（这次持有用掉了）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-bone-juggler-day-1")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(new SeatId(1)).Day?.CanMakeJugglerGuesses == true,
                Wait),
            "白天 1：杂耍艺人没有拿到猜测入口");
        Assert.Equal(
            "Accepted",
            (await juggler.InvokeAsync<CommandResultDto>(
                "MakeJugglerGuesses",
                new[] { new JugglerGuessDto { Seat = 2, Character = "clockmaker" } },
                "test-bone-juggler-guess-day-1")).Kind);

        // 白天 1：让 1 号死亡（死因与本用例无关，只要集骨者行动前他已死亡——
        // 与 `BoneCollectorHostTests` 用 ReportSeatState 让卖花女孩先死是同一手法）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ReportSeatState",
                1,
                "Dead",
                null,
                null,
                null,
                null,
                "测试：让杂耍艺人在集骨者行动前死亡",
                null,
                "test-bone-juggler-kill")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-bone-juggler-close-day-1")).Kind);

        // 第 2 夜：集骨者把能力重获给已死亡的 1 号（黄昏格在恶魔之前）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                2,
                "Original",
                "test-bone-juggler-night-2")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => collectorRequest?.RequestId == "sv:night-2:bone-collector",
                Wait),
            $"集骨者没有收到黄昏行动请求（最后：{collectorRequest?.RequestId ?? "无"}）");
        var regain = await collector.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            collectorRequest!.RequestId,
            "seat:1",
            "test-bone-juggler-regain",
            1L);
        Assert.True(
            regain.Kind == "Accepted",
            $"集骨者的重获被拒：{regain.RejectionCode} {regain.RejectionMessage}；"
                + $"候选={string.Join('/', collectorRequest.Options.Select(option => option.Value))}");
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetStorytellerView().PersistentEffects.Any(effect =>
                    effect.Ability == new AbilityId("bone-collector.regain")
                    && effect.Target == new SeatId(1)
                    && !effect.IsTerminated),
                Wait),
            "没有落重获能力窗口");

        // 第 2 夜：恶魔击杀格（排在重获格之后）照常提交——同一夜的其余格子必须不受那一格影响。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => demonRequest?.RequestId.Contains("fang-gu", StringComparison.Ordinal) == true,
                Wait),
            $"恶魔没有收到第 2 夜的击杀请求（最后：{demonRequest?.RequestId ?? "无"}）");
        var kill = await demon.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            demonRequest!.RequestId,
            "seat:2",
            "test-bone-juggler-kill-2",
            1L);
        Assert.True(
            kill.Kind == "Accepted",
            $"方古的击杀被拒：{kill.RejectionCode} {kill.RejectionMessage}");

        // 那一格是"死者被重获的白天能力格"：说书人照常被唤醒（挂裁定点比划数量），
        // 回答之后夜晚必须继续推进——**卡死**才是缺陷（R-0054 第 6 条）。
        var jugglerSlot = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.CurrentSlotId == "juggler" && view.AwaitingDecisionId is not null,
            Wait);
        Assert.True(
            jugglerSlot?.AwaitingDecisionId is not null,
            "重获的杂耍艺人那一格没有挂出说书人裁定点（死者被重获后必须被唤醒，R-0054 第 6 条）");
        Assert.True(
            jugglerSlot!.AwaitingDecisionSeat == 1,
            $"裁定点归属不是 1 号（实际 {jugglerSlot.AwaitingDecisionSeat?.ToString() ?? "（无）"}）");
        var jugglerInfo = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            jugglerSlot.AwaitingDecisionId,
            "1",
            null,
            "test-bone-juggler-juggler-info");
        Assert.True(
            jugglerInfo.Kind == "Accepted",
            $"重获的杂耍艺人那一格的裁定被拒：{jugglerInfo.RejectionCode} {jugglerInfo.RejectionMessage}");

        // 第 2 夜必须**自己**走完：那一格最差也应当像「本步无行动」那样显式跳过并继续推进，
        // 绝不能把整夜停住（`done/regained-day-ability-night-slot-stall.md` 的验收矩阵 1 / 2）。
        var nightTwo = await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait);
        if (nightTwo?.PlanCompleted != true)
        {
            var stalled = host.Session.GetStorytellerView();
            var events = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
            var gameEvents = events.Select(item => item.Event).ToArray();
            Assert.Fail(
                $"第 2 夜没有自然走完：槽位={stalled.CurrentSlotId?.Value ?? "（无）"}"
                + $"（{stalled.SlotIndex}/{stalled.SlotCount}）行动者={stalled.CurrentSlotActor?.Value.ToString() ?? "（无）"} "
                + $"阻塞={stalled.BlockedReason ?? "（无）"} 请求={stalled.Pending?.RequestId.Value ?? "（无）"} "
                + $"待裁定={stalled.AwaitingDecision?.Id.Value ?? "（无）"}；"
                + $"激活={gameEvents.OfType<SlotActivatedEvent>().Count()} "
                + $"跳过={gameEvents.OfType<PromptSkippedEvent>().Count()} "
                + $"裁定点={gameEvents.OfType<DecisionPointRaisedEvent>().Count()} "
                + $"阻塞事件={gameEvents.OfType<SlotBlockedEvent>().Count()}；事件尾部："
                + string.Join(
                    " | ",
                    events.TakeLast(30).Select(item => $"{item.Sequence}:{item.Event.GetType().Name}")));
        }

        // 白天 2（窗口存续）：入口重新出现，命令被受理 —— 本用例的正题。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-bone-juggler-day-2")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(new SeatId(1)).Day?.CanMakeJugglerGuesses == true,
                Wait),
            "白天 2：死亡但重获能力的杂耍艺人没有拿到猜测入口");
        var second = await juggler.InvokeAsync<CommandResultDto>(
            "MakeJugglerGuesses",
            new[] { new JugglerGuessDto { Seat = 4, Character = "klutz" } },
            "test-bone-juggler-guess-day-2");
        Assert.True(
            second.Kind == "Accepted",
            $"白天 2 的公开猜测被拒：{second.RejectionCode} {second.RejectionMessage}");
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                // 公开面只带**当天**的记录（第 1 天那条留在第 1 天的账里），所以这里是 1 而不是 2。
                () => host.Session.GetPlayerView(new SeatId(1)).Day?.PublicView.JugglerGuesses.Count == 1,
                Wait),
            "第二次猜测没有进当天账");
        Assert.False(host.Session.GetPlayerView(new SeatId(1)).Day?.CanMakeJugglerGuesses ?? false);

        // 同一天第二次照旧拒绝：放宽的是起算点，不是次数。
        var again = await juggler.InvokeAsync<CommandResultDto>(
            "MakeJugglerGuesses",
            new[] { new JugglerGuessDto { Seat = 4, Character = "klutz" } },
            "test-bone-juggler-guess-day-2-again");
        Assert.Equal("juggler.already_guessed", again.RejectionCode);

        // 收尾：关账 → 第 3 夜（下个黄昏）→ 窗口终止；再猜回到原口径（不是首个白天）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-bone-juggler-close-day-2")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                3,
                "Original",
                "test-bone-juggler-night-3")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetStorytellerView().PersistentEffects
                    .Where(effect => effect.Ability == new AbilityId("bone-collector.regain"))
                    .All(effect => effect.IsTerminated),
                Wait),
            "重获窗口没有在下个黄昏收口");
    }

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];
}
