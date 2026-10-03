using System.Text.Json;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 死亡触发族（心上人 / 贤者）在真宿主里的链路：
/// 白天处决心上人 → **触发型裁定**挂起（推进命令被拒）→ 说书人指定 4 号醉酒（持续效果落账）；
/// 次夜恶魔击杀贤者 → 当夜贤者格裁定 → 展示两名玩家 → 信息只到贤者本人、无关席位零下发。
/// </summary>
/// <remarks>
/// 场景（5 席）：1 方古（恶魔，次夜击杀）、2 贤者、3 心上人（白天被处决）、
/// 4 呆瓜 / 5 理发师（陪跑与零下发对照）；五席首夜都没有行动格，首夜自然走完。
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0038 / R-0039。
/// </remarks>
public sealed class DeathTriggerHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task SweetheartAndSage_RunEndToEnd_WithIsolation()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 5,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "fang-gu"), (2, "sage"), (3, "sweetheart"), (4, "klutz"), (5, "barber")),
            "test-death-trigger-assign");
        Assert.Equal("Accepted", assigned.Kind);

        OperationRequestDto? demonRequest = null;
        InformationResultDto? sageInfo = null;

        await using var demon = await host.ConnectSeatAsync(new SeatId(1), asked => demonRequest = asked);
        await using var sage = await host.ConnectSeatAsync(new SeatId(2), onInformation: info => sageInfo = info);
        await using var sweetheart = await host.ConnectSeatAsync(new SeatId(3));
        await using var klutz = await host.ConnectSeatAsync(new SeatId(4));
        await using var barber = await host.ConnectSeatAsync(new SeatId(5));

        // 首夜：五席都没有首夜行动格 → 配额走完即收口。
        var nightOne = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-death-trigger-night-1");
        Assert.Equal("Accepted", nightOne.Kind);
        var nightOneDone = await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait);
        Assert.True(nightOneDone!.PlanCompleted, "首夜没有自然走完（配额未推进到收口）");

        // 白天 1：处决 3 号（心上人）——触发型裁定应立即挂起。
        var dayStarted = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-death-trigger-day-start");
        Assert.Equal("Accepted", dayStarted.Kind);
        var dayOpen = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.Day is { Status: "Open", DayNumber: 1 },
            Wait);
        Assert.True(dayOpen!.Day is { Status: "Open", DayNumber: 1 }, "白天没有进入 Open 状态");

        var nominated = await sweetheart.InvokeAsync<CommandResultDto>("Nominate", 3, "test-death-trigger-nominate");
        Assert.Equal("Accepted", nominated.Kind);
        var demonVote = await demon.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-death-trigger-vote-demon");
        Assert.Equal("Accepted", demonVote.Kind);
        var sageVote = await sage.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-death-trigger-vote-sage");
        Assert.Equal("Accepted", sageVote.Kind);
        var klutzVote = await klutz.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-death-trigger-vote-klutz");
        Assert.Equal("Accepted", klutzVote.Kind);
        var barberVote = await barber.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-death-trigger-vote-barber");
        Assert.Equal("Accepted", barberVote.Kind);

        var counted = await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-death-trigger-count");
        Assert.Equal("Accepted", counted.Kind);
        var closedDay = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-death-trigger-close");
        Assert.Equal("Accepted", closedDay.Kind);

        // ① 心上人触发：无槽位的触发型裁定（提示含「心上人」）。
        var sweetheartDecision = await WaitForTriggerDecisionAsync(storyteller, "心上人");
        Assert.Null(sweetheartDecision.CurrentSlotId);

        // 触发型裁定没有槽位：归属由内核显式给出（死亡的心上人 = 3 号）——说书人圆环据此定位。
        Assert.Equal(3, sweetheartDecision.AwaitingDecisionSeat);

        // ② 推进命令被拒：裁定未了结前不许开夜（R-0039 第 6 条）。
        var blockedNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-death-trigger-night-blocked");
        Assert.Equal("Rejected", blockedNight.Kind);
        Assert.Equal("phase.trigger_choice_pending", blockedNight.RejectionCode);

        // ③ 说书人指定 4 号醉酒 → 效果落账、维度对账把 4 号改醉酒。
        var sting = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            sweetheartDecision.AwaitingDecisionId,
            "seat:4",
            null,
            "test-death-trigger-sting");
        Assert.Equal("Accepted", sting.Kind);
        await WaitForDrunkAsync(storyteller, 4);

        // 次夜：1 号方古行动 → 击杀 2 号贤者（恶魔击杀在贤者格之前）。
        var nightTwo = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-death-trigger-night-2");
        Assert.Equal("Accepted", nightTwo.Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(() => demonRequest is not null, Wait),
            "方古没有收到操作请求");
        var killed = await demon.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            demonRequest!.RequestId,
            "seat:2",
            "test-death-trigger-kill",
            1);
        Assert.Equal("Accepted", killed.Kind);

        // ④ 贤者触发：当夜贤者格裁定，提示按击杀记录推演真恶魔 = 1 号。
        var sageDecision = await WaitForSlotDecisionAsync(storyteller, "sage");
        Assert.Contains("推演", sageDecision.AwaitingDecisionContext, StringComparison.Ordinal);
        Assert.Contains("1 号", sageDecision.AwaitingDecisionContext, StringComparison.Ordinal);

        // 触发格没有行动者：归属 = 死亡时点以贤者身份落账的席位（2 号）。
        Assert.Equal(2, sageDecision.AwaitingDecisionSeat);

        // ⑤ 说书人展示 1 号与 4 号 → 信息只到贤者本人。
        var shown = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            sageDecision.AwaitingDecisionId,
            "pair:1+4",
            null,
            "test-death-trigger-show");
        Assert.Equal("Accepted", shown.Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => sageInfo is not null, Wait),
            "贤者没有收到信息结果");
        AssertSageInfo(sageInfo!);

        // ⑥ 反方向零下发：无关席位（4 号醉酒陪跑 / 5 号理发师）没有信息结果。
        Assert.Empty(host.Bundles[new SeatId(4)].View.InformationResults);
        Assert.Empty(host.Bundles[new SeatId(5)].View.InformationResults);

        // ⑦ 裁定结清后不再挂起，且**当夜继续自动推进到收口**——
        //    触发型 / 触发格裁定结清后的续推回归（独立对抗性复核 H-1：此前结清后夜晚会停在原地）。
        var settled = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is null && view.PlanCompleted,
            Wait);
        Assert.True(
            settled is { AwaitingDecisionId: null, AwaitingDecisionSeat: null, PlanCompleted: true },
            $"贤者裁定结清后夜晚没有继续推进：slot={settled?.SlotIndex}/{settled?.SlotCount} "
                + $"completed={settled?.PlanCompleted}；归属残留={settled?.AwaitingDecisionSeat}");
    }

    private static void AssertSageInfo(InformationResultDto info)
    {
        Assert.Equal("sage", info.Ability);
        Assert.Contains("1 号", info.Content, StringComparison.Ordinal);
        Assert.Contains("4 号", info.Content, StringComparison.Ordinal);

        // 玩家投影 DTO 的字段面必须**恰好**是这三项：说书人专属字段（可能为假 / 说明）
        // 不进 DTO（D-0012）——键集合断言给"将来有人加字段"留下回归保护。
        // 序列化口径与宿主一致（Web defaults → camelCase）。
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(info, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var keys = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(["ability", "content", "sequence"], keys);
    }

    private static async Task<StorytellerViewDto> WaitForTriggerDecisionAsync(
        GameClient storyteller,
        string token)
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

    private static async Task<StorytellerViewDto> WaitForSlotDecisionAsync(GameClient storyteller, string slotId)
    {
        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.AwaitingDecisionId is not null && candidate.CurrentSlotId == slotId,
            Wait);
        Assert.True(
            view is { AwaitingDecisionId: not null } && view.CurrentSlotId == slotId,
            $"没有等到槽位 {slotId} 上的裁定（最后视图 slot={view?.CurrentSlotId ?? "无"} "
                + $"挂起={view?.AwaitingDecisionId ?? "无"}）");
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

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] seats) =>
        [.. seats.Select(item => new SeatCharacterAssignmentDto
        {
            Seat = item.Seat,
            Character = item.Character,
        })];
}
