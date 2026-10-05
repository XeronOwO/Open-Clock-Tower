using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 杂耍艺人的真宿主链路（R-0057-B）：带他的局开得了白天 → 首个白天公开猜测（**全体可见**）→
/// 当天不能猜第二次 → 当晚说书人给出猜对数（**只到本人**）。跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 规则来源：百科《杂耍艺人》· 2026-10-01 抓取（角色能力 / 角色简介 / 运作方式）；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0057-B。
/// </remarks>
public sealed class JugglerHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private static readonly SeatId Juggler = new(1);
    private static readonly SeatId Bystander = new(2);
    private static readonly SeatId Demon = new(5);

    /// <summary>
    /// 五席：1 号杂耍艺人、2 号艺术家、3 号呆瓜、4 号畸形秀演员、5 号方古（首夜不行动）。
    /// 首个白天他猜「2 号是艺术家」（对）与「3 号是贤者」（错）——当晚应得到「1」。
    /// </summary>
    [Fact]
    public async Task Juggler_GuessesPublicly_ThenLearnsTheCountAtNight()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "AssignCharacters",
                Seats((1, "juggler"), (2, "artist"), (3, "klutz"), (4, "mutant"), (5, "fang-gu")),
                "test-juggler-assign")).Kind);

        await using var juggler = await host.ConnectSeatAsync(Juggler);
        await using var bystander = await host.ConnectSeatAsync(Bystander);
        await using var demon = await host.ConnectSeatAsync(Demon);

        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                1,
                "Original",
                "test-juggler-night-1")).Kind);
        Assert.True(
            (await TestServerHost.WaitForViewAsync(storyteller, view => view.PlanCompleted, Wait))!.PlanCompleted,
            "首夜没有自然走完");

        // 开白天：带杂耍艺人的局不再被 legality.day_contract_missing 拒绝；入口只发给本人。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("StartDay", "test-juggler-day-1")).Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(Juggler).Day?.CanMakeJugglerGuesses == true,
                Wait),
            "杂耍艺人没有拿到公开猜测的入口");
        Assert.False(host.Session.GetPlayerView(Bystander).Day?.CanMakeJugglerGuesses ?? false);

        // 公开猜测：受理后进事件流与**当天公开面**——旁观席位也看得到（R-0057-B 第 2 条）。
        Assert.Equal(
            "Accepted",
            (await juggler.InvokeAsync<CommandResultDto>(
                "MakeJugglerGuesses",
                new[]
                {
                    Guess(2, "artist"),
                    Guess(3, "sage"),
                },
                "test-juggler-guess-1")).Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(Bystander).Day?.PublicView.JugglerGuesses.Count == 1,
                Wait),
            "公开猜测没有进旁观席位的白天视图");
        var publicGuesses = host.Session.GetPlayerView(Bystander).Day!.PublicView.JugglerGuesses;
        Assert.Equal(Juggler, publicGuesses[0].Seat);
        Assert.Equal(2, publicGuesses[0].Guesses.Count);
        Assert.Equal(new CharacterId("artist"), publicGuesses[0].Guesses[0].Character);
        Assert.Equal(new CharacterId("sage"), publicGuesses[0].Guesses[1].Character);

        // 这次持有已经用过：入口消失，同一天再来一次被拒。
        Assert.False(host.Session.GetPlayerView(Juggler).Day?.CanMakeJugglerGuesses ?? false);
        var again = await juggler.InvokeAsync<CommandResultDto>(
            "MakeJugglerGuesses",
            new[] { Guess(2, "artist") },
            "test-juggler-guess-2");
        Assert.Equal("Rejected", again.Kind);
        Assert.Equal("juggler.already_guessed", again.RejectionCode);

        // 关账 → 第二夜：方古先选猎物（玩家自己的操作请求），随后杂耍艺人的格由说书人给出猜对数。
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>("CloseDay", "test-juggler-close-day")).Kind);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                2,
                "Original",
                "test-juggler-night-2")).Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(Demon).PendingRequest is not null,
                Wait),
            "恶魔没有拿到选择猎物的请求");
        var hunt = host.Session.GetPlayerView(Demon).PendingRequest!;
        var target = hunt.Prompt.Options.FirstOrDefault(option => option.Value == "seat:4")?.Value
            ?? hunt.Prompt.Options[0].Value;
        Assert.Equal(
            "Accepted",
            (await demon.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                hunt.Id.Value,
                target,
                "test-juggler-demon-hunt",
                0)).Kind);

        var infoDecision = await TestServerHost.WaitForViewAsync(
            storyteller,
            view => view.AwaitingDecisionId is not null && view.AwaitingDecisionSeat == Juggler.Value,
            Wait);
        Assert.NotNull(infoDecision);
        Assert.Contains("猜对 1 条", infoDecision!.AwaitingDecisionContext!, StringComparison.Ordinal);
        Assert.Equal(
            "Accepted",
            (await storyteller.InvokeAsync<CommandResultDto>(
                "ResolveDecisionPoint",
                infoDecision.AwaitingDecisionId,
                "1",
                null,
                "test-juggler-info")).Kind);

        // 猜对数只到本人：旁观席位与恶魔席位一条信息都没有。
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(Juggler).InformationResults.Count == 1,
                Wait),
            "猜对数没有下发");
        Assert.Equal("1", host.Session.GetPlayerView(Juggler).InformationResults[0].Content);
        Assert.Empty(host.Session.GetPlayerView(Bystander).InformationResults);
        Assert.Empty(host.Session.GetPlayerView(new SeatId(3)).InformationResults);
        Assert.Empty(host.Session.GetPlayerView(Demon).InformationResults);
    }

    private static JugglerGuessDto Guess(int seat, string character) =>
        new() { Seat = seat, Character = character };

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] rows) =>
        [.. rows.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];
}
