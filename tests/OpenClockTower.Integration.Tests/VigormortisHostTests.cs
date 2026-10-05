using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 亡骨魔的真宿主链路：带它的局开得了夜（不再 <c>plan.contract_missing</c>）→ 它杀死一名爪牙 →
/// 该爪牙死亡但保留能力、说书人选的那一侧最近的镇民中毒 → **下一夜他照样被唤醒行动** →
/// 亡骨魔死亡时保留能力与中毒一起收口。跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 规则来源：百科《亡骨魔》· 2026-10-01 抓取（角色能力 / 规则细节 11–24 / 角色简介）；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0056。
/// </remarks>
public sealed class VigormortisHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private static readonly SeatId Demon = new(1);
    private static readonly SeatId Minion = new(2);
    private static readonly SeatId Artist = new(3);

    /// <summary>
    /// 一局六席：1 号亡骨魔、2 号女巫（爪牙）、3 号艺术家、4 号呆瓜、5 号卜算者、6 号畸形秀演员
    /// ——2 号两侧最近的镇民分别是 3 号（顺时针）与 5 号（逆时针），说书人的选侧因此有实际区别；
    /// 夜间需要玩家 / 说书人操作的只有女巫、亡骨魔与卜算者三处。
    /// </summary>
    [Fact]
    public async Task Vigormortis_KillsMinion_RetainsAbility_AndWakesHimNextNight()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 6, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats(
                (1, "vigormortis"),
                (2, "witch"),
                (3, "artist"),
                (4, "klutz"),
                (5, "oracle"),
                (6, "mutant")),
            "test-vig-assign");
        Assert.Equal("Accepted", assigned.Kind);

        OperationRequestDto? witchRequest = null;
        await using var witch = await host.ConnectSeatAsync(Minion, asked => witchRequest = asked);
        OperationRequestDto? demonRequest = null;
        await using var demon = await host.ConnectSeatAsync(Demon, asked => demonRequest = asked);

        // 首夜：亡骨魔不在首个夜晚顺序表上（「除首个夜晚外」由顺序表表达）；女巫在，她照常下咒。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                1,
                "Original",
                "test-vig-night-1")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => witchRequest is not null && witchRequest.RequestId == "sv:night-1:witch",
                Wait),
            $"女巫没有收到首夜请求（最后：{witchRequest?.RequestId ?? "无"}）");
        await Submit(witch, witchRequest!.RequestId, "seat:4", "test-vig-witch-1");
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "首夜没有自然走完（亡骨魔的局开不了夜？）");

        // 白天 1：开完即关。
        var dayOne = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-vig-day-1");
        Assert.True(dayOne.Kind == "Accepted", $"开白天被拒：{dayOne.RejectionCode} {dayOne.RejectionMessage}");
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-vig-close-day-1")).Kind);

        // 第 2 夜：女巫先行动（其他夜晚表上她排在恶魔段之前），亡骨魔随后杀 2 号——她自己是爪牙。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                2,
                "Original",
                "test-vig-night-2")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => witchRequest is not null && witchRequest.RequestId == "sv:night-2:witch",
                Wait),
            $"女巫没有收到第 2 夜请求（最后：{witchRequest?.RequestId ?? "无"}）");
        await Submit(witch, witchRequest!.RequestId, "seat:6", "test-vig-witch-2");

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => demonRequest is not null && demonRequest.RequestId == "sv:night-2:vigormortis",
                Wait),
            $"亡骨魔没有收到第 2 夜请求（最后：{demonRequest?.RequestId ?? "无"}）");
        await Submit(demon, demonRequest!.RequestId, "seat:2", "test-vig-kill");

        // 说书人的选侧裁定点：选顺时针——2 号顺时针最近的镇民是 3 号艺术家。
        var side = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null && view.AwaitingDecisionSeat == 1,
            Wait);
        Assert.NotNull(side);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                side!.AwaitingDecisionId,
                "clockwise",
                null,
                "test-vig-side")).Kind);

        // 两条效果落账：保留能力窗口落在 2 号（他保持死亡）、中毒落在 3 号。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () =>
                {
                    var effects = host.Session.GetStorytellerView().PersistentEffects;
                    return effects.Any(effect =>
                            effect.Ability == new AbilityId("vigormortis.retention")
                            && effect.Target == Minion
                            && effect.Window == EffectWindowKind.RetainedAbility
                            && !effect.IsTerminated)
                        && effects.Any(effect =>
                            effect.Ability == new AbilityId("vigormortis.retention")
                            && effect.Target == Artist
                            && effect.Dimension == EffectDimension.Poison
                            && !effect.IsTerminated);
                },
                Wait),
            "没有落「保留能力」窗口或邻近镇民中毒");

        Assert.Equal(
            LifeState.Dead,
            host.Session.GetStorytellerView().Seats.Single(entry => entry.Seat == Minion).LifeValue);

        // 无关玩家视角：两条标记只进说书人视图——玩家投影里没有效果通道，
        // 旁观席与当事席都没有信息结果、也没有待响应请求（信息隔离在服务端强制）。
        Assert.Empty(host.Session.GetPlayerView(new SeatId(4)).InformationResults);
        Assert.Null(host.Session.GetPlayerView(new SeatId(4)).PendingRequest);
        Assert.Empty(host.Session.GetPlayerView(Minion).InformationResults);

        // 恶魔段之后是卜算者的信息格：结清它，第 2 夜才会自然走完。
        await ResolveOracle(storyteller, "test-vig-oracle-2");
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "第 2 夜没有自然走完");

        // 白天 2 → 第 3 夜：已经死亡的爪牙**照样被唤醒**（保留能力：他从未失去能力）。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-vig-day-2")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-vig-close-day-2")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                3,
                "Original",
                "test-vig-night-3")).Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => witchRequest is not null && witchRequest.RequestId == "sv:night-3:witch",
                Wait),
            $"死亡的爪牙没有被唤醒使用能力（最后：{witchRequest?.RequestId ?? "无"}）");
        Assert.Equal(
            LifeState.Dead,
            host.Session.GetStorytellerView().Seats.Single(entry => entry.Seat == Minion).LifeValue);

        // 他照常行动：提交选择被受理（死者不再被「配额照走、不唤醒」直接跳过）。
        await Submit(witch, witchRequest!.RequestId, "seat:6", "test-vig-witch-3");

        // 第 3 夜走完 → 白天 3 里亡骨魔死亡：保留能力与中毒一起收口。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => demonRequest is not null && demonRequest.RequestId == "sv:night-3:vigormortis",
                Wait),
            $"亡骨魔没有收到第 3 夜请求（最后：{demonRequest?.RequestId ?? "无"}）");
        await Submit(demon, demonRequest!.RequestId, "seat:3", "test-vig-kill-3");
        await ResolveOracle(storyteller, "test-vig-oracle-3");
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "第 3 夜没有自然走完");

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-vig-day-3")).Kind);
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
                "测试：亡骨魔死亡",
                null,
                "test-vig-demon-death")).Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () =>
                {
                    var effects = host.Session.GetStorytellerView().PersistentEffects;
                    var retain = effects.Single(effect =>
                        effect.Ability == new AbilityId("vigormortis.retention")
                        && effect.Target == Minion
                        && effect.Window == EffectWindowKind.RetainedAbility);
                    return retain.IsTerminated
                        && effects.Where(effect =>
                                effect.Ability == new AbilityId("vigormortis.retention")
                                && effect.Dimension == EffectDimension.Poison)
                            .All(effect => effect.IsTerminated);
                },
                Wait),
            "亡骨魔死亡后保留能力窗口或中毒没有收口");
    }

    /// <summary>结清卜算者的夜间信息格（说书人裁定点）：给出一个数字即可。</summary>
    private static async Task ResolveOracle(GameClient storyteller, string key)
    {
        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.CurrentSlotId == "oracle" && candidate.AwaitingDecisionId is not null,
            Wait);
        Assert.NotNull(view);
        var result = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            view!.AwaitingDecisionId,
            "0 名",
            null,
            key);
        Assert.True(
            result.Kind == "Accepted",
            $"结清卜算者被拒：{result.RejectionCode} {result.RejectionMessage}");
    }

    /// <summary>提交一次操作请求的响应；被拒时把原因码与消息抛出来（否则只看到 "Rejected" 无从查）。</summary>
    private static async Task Submit(GameClient connection, string requestId, string value, string key)
    {
        var result = await connection.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            requestId,
            value,
            key,
            1L);
        if (result.Kind != "Accepted")
        {
            throw new InvalidOperationException(
                $"提交 {requestId} = {value} 被拒：{result.RejectionCode} {result.RejectionMessage}");
        }
    }

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];
}
