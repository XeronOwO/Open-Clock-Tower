using System.Text.Json;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 数学家（批次 E21）在真宿主里的链路：说书人裁定提示按**入槽时刻**的账本推演窗口数字
/// （当夜更早槽位的失效计入），数字只下发给数学家本人，无关玩家零下发。
/// </summary>
/// <remarks>
/// 场景（6 席）：1 号诺-达鲺常驻中毒 → 6 号钟表匠与 2 号筑梦师在首夜结算时各留一条
/// `Poisoned`；数学家在它们之后入槽，提示的推演必须是 2——计划期快照会是 0，因此这一行
/// 同时证明「入槽实时重建」真的生效。
/// </remarks>
public sealed class MathematicianHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Mathematician_SeesWindowDeduction_AndOnlyRecipientGetsNumber()
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
                (2, "dreamer"),
                (3, "mathematician"),
                (4, "mutant"),
                (5, "klutz"),
                (6, "clockmaker")),
            "test-mathematician-assign");
        Assert.Equal("Accepted", assigned.Kind);

        // 常驻中毒先落地：2 号（筑梦师）与 6 号（钟表匠）——两者首夜结算都会留下失效记录。
        var poisoned = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.Seats.Any(seat => seat.Seat == 2
                    && seat.Facts.Any(fact => fact.Dimension == "Poison" && fact.Value == "Poisoned"))
                && view.Seats.Any(seat => seat.Seat == 6
                    && seat.Facts.Any(fact => fact.Dimension == "Poison" && fact.Value == "Poisoned")),
            Wait);
        Assert.NotNull(poisoned);

        OperationRequestDto? dreamerRequest = null;
        InformationResultDto? mathematicianInfo = null;
        await using var mathematician = await host.ConnectSeatAsync(
            new SeatId(3),
            onInformation: information => mathematicianInfo = information);
        await using var dreamer = await host.ConnectSeatAsync(
            new SeatId(2),
            request => dreamerRequest = request);

        var started = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-mathematician-night-1");
        Assert.Equal("Accepted", started.Kind);

        // ① 钟表匠（6 号，中毒）先入槽：说书人裁定 → 结算留下 Poisoned。
        var clockmakerDecision = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "clockmaker",
            Wait);
        Assert.NotNull(clockmakerDecision);
        await ResolveDecisionAsync(storyteller, clockmakerDecision!, "距离 1。", "test-mathematician-clockmaker");

        // ② 筑梦师（2 号，中毒）：玩家选择 → 未生效的自由信息 → 结算留下 Poisoned。
        var asked = await TestServerHost.WaitUntilAsync(() => dreamerRequest is not null, Wait);
        Assert.True(asked, "筑梦师在超时前没有收到操作请求");
        var submitted = await dreamer.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            dreamerRequest!.RequestId,
            "seat:1",
            "test-mathematician-dreamer-answer",
            1);
        Assert.Equal("Accepted", submitted.Kind);

        var dreamerDecision = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "dreamer",
            Wait);
        Assert.NotNull(dreamerDecision);
        await ResolveDecisionAsync(
            storyteller,
            dreamerDecision!,
            "1 号玩家的信息由我裁定。",
            "test-mathematician-dreamer-info");

        // ③ 数学家（3 号）入槽：提示按入槽时刻的账本重建——窗口内两名玩家（2 / 6）。
        var mathematicianDecision = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "mathematician",
            Wait);
        Assert.NotNull(mathematicianDecision);
        Assert.Contains("推演：2", mathematicianDecision!.AwaitingDecisionContext, StringComparison.Ordinal);

        await ResolveDecisionAsync(storyteller, mathematicianDecision!, "2", "test-mathematician-number");

        // ④ 数字只推给数学家本人，内容原样；「可能为假」与说书人说明不下发。
        var pushed = await TestServerHost.WaitUntilAsync(() => mathematicianInfo is not null, Wait);
        Assert.True(pushed, "数学家的信息结果没有推送");
        var info = mathematicianInfo!;
        Assert.Equal("mathematician", info.Ability);
        Assert.Equal("2", info.Content);
        var wire = JsonSerializer.Serialize(info);
        Assert.DoesNotContain("MayBeFalse", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Note", wire, StringComparison.OrdinalIgnoreCase);

        // ⑤ 反方向：无关玩家（4 号）零下发。
        await using var bystander = await host.ConnectSeatAsync(new SeatId(4));
        Assert.Empty(host.Bundles[new SeatId(4)].View.InformationResults);

        // ⑥ 说书人视图：失效账本两条都在（窗口事实），玩家端不需要看见。
        var afterNight = await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait);
        Assert.NotNull(afterNight);
        Assert.Contains(afterNight!.Malfunctions, item => item.Seat == 2 && item.Kind == "Poisoned");
        Assert.Contains(afterNight!.Malfunctions, item => item.Seat == 6 && item.Kind == "Poisoned");
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
