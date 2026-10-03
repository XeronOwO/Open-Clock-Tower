using System.Text.Json;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 回溯型信息族（卖花女孩 / 城镇公告员 / 神谕者）在真宿主里的链路：
/// 白天的「恶魔投了赞成 + 爪牙发起提名」按**动作时刻的角色快照**落进白天账，次夜三名角色的
/// 说书人裁定提示按记录推演（是 / 是 / 1），信息只到本人、无关玩家零下发。
/// </summary>
/// <remarks>
/// 场景（6 席）：1 诺-达鲺（恶魔，白天投票、当夜击杀）、2 麻脸巫婆（爪牙，白天自我提名、
/// 白天被处决）、3 卖花女孩 / 4 城镇公告员 / 5 神谕者（三张信息面）、6 畸形秀演员（陪跑，零下发对照）。
/// 诺-达鲺的相邻席位是 2 / 6（都不是镇民），因此三名信息角色不被常驻中毒影响。
/// </remarks>
public sealed class RetrospectiveInfoHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task DayFacts_DriveNightPrompts_AndInfoGoesOnlyToOwners()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 6,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats(
                (1, "no-dashii"),
                (2, "pit-hag"),
                (3, "flowergirl"),
                (4, "town-crier"),
                (5, "oracle"),
                (6, "mutant")),
            "test-retro-assign");
        Assert.Equal("Accepted", assigned.Kind);

        OperationRequestDto? demonRequest = null;
        InformationResultDto? flowergirlInfo = null;
        InformationResultDto? crierInfo = null;
        InformationResultDto? oracleInfo = null;

        await using var demon = await host.ConnectSeatAsync(new SeatId(1), asked => demonRequest = asked);
        await using var pitHag = await host.ConnectSeatAsync(new SeatId(2));
        await using var flowergirl = await host.ConnectSeatAsync(
            new SeatId(3),
            onInformation: info => flowergirlInfo = info);
        await using var townCrier = await host.ConnectSeatAsync(
            new SeatId(4),
            onInformation: info => crierInfo = info);
        await using var oracle = await host.ConnectSeatAsync(
            new SeatId(5),
            onInformation: info => oracleInfo = info);
        await using var bystander = await host.ConnectSeatAsync(new SeatId(6));

        // 首夜：六席里没有任何首夜行动者（诺-达鲺 / 麻脸巫婆 / 三名信息角色都不在首夜顺序表上），
        // 配额走完即收口。
        var nightOne = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-retro-night-1");
        Assert.Equal("Accepted", nightOne.Kind);
        var nightOneDone = await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait);
        Assert.True(nightOneDone!.PlanCompleted, "首夜没有自然走完（配额未推进到收口）");

        // 白天：2 号（麻脸巫婆，爪牙）自我提名；1 号（诺-达鲺，恶魔）投赞成 ⇒ 两条白天事实入账。
        var dayStarted = await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-retro-day-start");
        Assert.Equal("Accepted", dayStarted.Kind);
        var dayOpen = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.Day is { Status: "Open", DayNumber: 1 },
            Wait);
        Assert.True(dayOpen!.Day is { Status: "Open", DayNumber: 1 }, "白天没有进入 Open 状态");

        var nominated = await pitHag.InvokeAsync<CommandResultDto>("Nominate", 2, "test-retro-nominate");
        Assert.Equal("Accepted", nominated.Kind);
        var demonVote = await demon.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-retro-vote-demon");
        Assert.Equal("Accepted", demonVote.Kind);
        var crierVote = await townCrier.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-retro-vote-crier");
        Assert.Equal("Accepted", crierVote.Kind);
        var oracleVote = await oracle.InvokeAsync<CommandResultDto>("CastVote", 1, true, "test-retro-vote-oracle");
        Assert.Equal("Accepted", oracleVote.Kind);

        var counted = await storyteller.InvokeAsync<CommandResultDto>("CountVotes", 1, "test-retro-count");
        Assert.Equal("Accepted", counted.Kind);
        var closed = await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-retro-close");
        Assert.Equal("Accepted", closed.Kind);
        var dayClosed = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.Day is { Status: "Closed", Executed: 2 },
            Wait);
        Assert.True(dayClosed!.Day is { Status: "Closed", Executed: 2 }, "白天没有收口到处决 2 号");

        // 次夜：1 号恶魔先行动（顺序表在信息角色之前），击杀 6 号（对推演无影响的善良席位）。
        var nightTwo = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-retro-night-2");
        Assert.Equal("Accepted", nightTwo.Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(() => demonRequest is not null, Wait),
            "恶魔没有收到操作请求");
        var killed = await demon.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            demonRequest!.RequestId,
            "seat:6",
            "test-retro-kill",
            1);
        Assert.Equal("Accepted", killed.Kind);

        // ① 卖花女孩：恶魔白天投过赞成 ⇒ 推演「是」。
        var flowergirlDecision = await WaitForSlotDecisionAsync(storyteller, "flowergirl");
        Assert.Contains("推演：是", flowergirlDecision.AwaitingDecisionContext, StringComparison.Ordinal);
        await ResolveDecisionAsync(storyteller, flowergirlDecision, "恶魔参与了投票", "test-retro-flowergirl");

        // ② 城镇公告员：爪牙发起过提名 ⇒ 推演「是」。
        var crierDecision = await WaitForSlotDecisionAsync(storyteller, "town-crier");
        Assert.Contains("推演：是", crierDecision.AwaitingDecisionContext, StringComparison.Ordinal);
        await ResolveDecisionAsync(storyteller, crierDecision, "有爪牙发起了提名", "test-retro-crier");

        // ③ 神谕者：死亡的邪恶玩家 = 白天被处决的 2 号（当夜死者是善良，不计） ⇒ 推演 1。
        var oracleDecision = await WaitForSlotDecisionAsync(storyteller, "oracle");
        Assert.Contains("推演：1", oracleDecision.AwaitingDecisionContext, StringComparison.Ordinal);
        await ResolveDecisionAsync(storyteller, oracleDecision, "1", "test-retro-oracle-number");

        // ④ 信息只到本人：三名角色的内容原样；「可能为假」与说书人说明不下发。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => flowergirlInfo is not null && crierInfo is not null && oracleInfo is not null,
                Wait),
            "三名角色的信息没有全部推送到位");
        AssertInfo(flowergirlInfo!, "flowergirl", "恶魔参与了投票");
        AssertInfo(crierInfo!, "town-crier", "有爪牙发起了提名");
        AssertInfo(oracleInfo!, "oracle", "1");

        // ⑤ 反方向：无关席位（2 号被处决的爪牙 / 6 号陪跑）零下发。
        Assert.Empty(host.Bundles[new SeatId(2)].View.InformationResults);
        Assert.Empty(host.Bundles[new SeatId(6)].View.InformationResults);
    }

    private static void AssertInfo(InformationResultDto info, string ability, string content)
    {
        Assert.Equal(ability, info.Ability);
        Assert.Equal(content, info.Content);

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

    private static async Task ResolveDecisionAsync(
        GameClient storyteller,
        StorytellerViewDto view,
        string decision,
        string key)
    {
        var resolved = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            view.AwaitingDecisionId,
            decision,
            null,
            key);
        Assert.Equal("Accepted", resolved.Kind);
    }

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] seats) =>
        [.. seats.Select(item => new SeatCharacterAssignmentDto
        {
            Seat = item.Seat,
            Character = item.Character,
        })];
}
