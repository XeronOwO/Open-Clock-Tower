using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 旅行者加入 / 离开的真宿主链路（票据 `traveller-and-exile` D1）：席位落点、邀请码、
/// 邪恶旅行者的私密揭示、离场口径与护栏拒绝；跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </summary>
/// <remarks>
/// 规则来源：百科《旅行者》· 2026-10-04 抓取（加入第 2–3、6 步 / 离开流程）；
/// 平台口径见 `docs/standard/rulings.md` R-0044 第 6 条、R-0045 第 4 条与 R-0046。
/// </remarks>
public sealed class TravellerHostTests
{
    /// <summary>16 席开局用：15 个互不相同的非旅行者角色（覆盖四类型，全部有已实现的夜晚契约）。</summary>
    private static SeatCharacterAssignmentDto[] FifteenNonTravellers() =>
    [
        new() { Seat = 1, Character = "clockmaker" },
        new() { Seat = 2, Character = "dreamer" },
        new() { Seat = 3, Character = "snake-charmer" },
        new() { Seat = 4, Character = "mathematician" },
        new() { Seat = 5, Character = "flowergirl" },
        new() { Seat = 6, Character = "town-crier" },
        new() { Seat = 7, Character = "oracle" },
        new() { Seat = 8, Character = "savant" },
        new() { Seat = 9, Character = "seamstress" },
        new() { Seat = 10, Character = "philosopher" },
        new() { Seat = 11, Character = "artist" },
        new() { Seat = 12, Character = "juggler" },
        new() { Seat = 13, Character = "sage" },
        new() { Seat = 14, Character = "mutant" },
        new() { Seat = 15, Character = "sweetheart" },
    ];

    /// <summary>5 席夹具：4 名非旅行者 + 1 名恶魔（邪恶旅行者的揭示目标）。</summary>
    private static SeatCharacterAssignmentDto[] FourNonTravellersAndADemon() =>
    [
        new() { Seat = 1, Character = "clockmaker" },
        new() { Seat = 2, Character = "artist" },
        new() { Seat = 3, Character = "klutz" },
        new() { Seat = 4, Character = "mutant" },
        new() { Seat = 5, Character = "no-dashii" },
    ];

    /// <summary>
    /// 15+ 开局：第 16 席提前占好但还没有角色，说书人把旅行者落在该席位；
    /// 加入之后**整局能开夜**（这是 D1 存在的直接理由）。
    /// </summary>
    [Fact]
    public async Task JoinTraveller_FillsDeclaredSeat_ThenSixteenSeatGameStartsNight()
    {
        await using var host = new TestServerHost(seatCount: 16, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            FifteenNonTravellers(),
            "test-traveller-assign-15");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            16,
            "deviant",
            "Good",
            null,
            "test-traveller-join-16");
        Assert.Equal("Accepted", joined.Kind);

        // 落点是指定席位：不动席位名单，因此不追加任何席位。
        Assert.Null(joined.IssuedSeat);

        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        var traveller = Assert.Single(view.Seats, entry => entry.Seat == 16);
        Assert.Contains(traveller.Facts, fact => fact.Dimension == "Character" && fact.Value == "deviant");
        Assert.Contains(traveller.Facts, fact => fact.Dimension == "Alignment" && fact.Value == "Good");
        Assert.Contains(traveller.Facts, fact => fact.Dimension == "Life" && fact.Value == "Alive");

        var started = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-traveller-night-1");
        Assert.True(
            started.Kind == "Accepted",
            $"16 席局在旅行者加入后仍被拒绝开夜：{started.RejectionCode} {started.RejectionMessage} {started.Failure}");
    }

    /// <summary>
    /// 追加席位：服务端分配席位号（**不签发凭据**，D-0038）；说书人随后为它签发邀请码，
    /// 玩家拿那一枚真的能进来；重复投递（同幂等键）仍回同一席位，且**不会把已经发出去的码弄坏**。
    /// </summary>
    [Fact]
    public async Task JoinTraveller_AppendsSeat_ThenIssuedInviteCode_CanJoin()
    {
        await using var host = new TestServerHost(seatCount: 3, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "barista",
            "Good",
            null,
            "test-traveller-append");
        Assert.Equal("Accepted", joined.Kind);
        Assert.Equal(4, joined.IssuedSeat);

        // 凭据不在命令回执里（D-0038）：说书人另外签一枚，玩家凭它入座。
        var code = await host.IssueInviteCodeAsync(new SeatId(4));
        Assert.False(string.IsNullOrWhiteSpace(code));

        var account = await host.SeatFixtureAccountAsync(new SeatId(4));
        await using var traveller = await host.ConnectAnonymousAsync();
        var seated = await traveller.InvokeAsync<SeatJoinDto>("JoinByInviteCode", code, account.AccountSession, 0L);
        Assert.Equal(4, seated.Bundle.View.Seat);

        // 重投一次：席位回得一样，而**刚才那一枚码仍然有效**——
        // 幂等重试不该变成一次隐式轮换（那会把说书人已经转交出去的码弄坏）。
        var replay = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "barista",
            "Good",
            null,
            "test-traveller-append");
        Assert.Equal("Duplicate", replay.Kind);
        Assert.Equal(joined.IssuedSeat, replay.IssuedSeat);

        await using var again = await host.ConnectAnonymousAsync();
        var stillWorks = await again.InvokeAsync<SeatJoinDto>("JoinByInviteCode", code, account.AccountSession, 0L);
        Assert.Equal(4, stillWorks.Bundle.View.Seat);
    }

    /// <summary>
    /// 邪恶旅行者：说书人指定告知的存活恶魔（本例一名）走**私密信息面**——
    /// 只有本人收得到；其他玩家只看到公开的「谁 + 角色」，看不到阵营。
    /// </summary>
    [Fact]
    public async Task JoinTraveller_Evil_RevealsDemonOnlyToHimself()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            FourNonTravellersAndADemon(),
            "test-traveller-evil-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "harlot",
            "Evil",
            new[] { 5 },
            "test-traveller-evil-join");
        Assert.Equal("Accepted", joined.Kind);
        var travellerSeat = new SeatId(joined.IssuedSeat!.Value);

        await using var traveller = await host.ConnectSeatAsync(travellerSeat);
        await using var townsfolk = await host.ConnectSeatAsync(new SeatId(1));

        // 本人：收到私密揭示（5 号是恶魔），内容由说书人指定。
        var information = Assert.Single(
            host.Bundles[travellerSeat].Events,
            item => item.Kind == "InformationResultIssued");
        Assert.Contains("5 号", information.Information!.Content, StringComparison.Ordinal);
        Assert.Contains("恶魔", information.Information.Content, StringComparison.Ordinal);

        // 他人：收得到公开事实（席位 + 角色），收不到那条私密信息、也看不到阵营。
        var publicEvent = Assert.Single(
            host.Bundles[new SeatId(1)].Events,
            item => item.Kind == "TravellerJoined");
        Assert.Equal(travellerSeat.Value, publicEvent.Seat);
        Assert.Equal("harlot", publicEvent.Character);
        Assert.DoesNotContain(
            host.Bundles[new SeatId(1)].Events,
            item => item.Kind == "InformationResultIssued");
    }

    /// <summary>
    /// 离场：席位从在局座次移除、票据保留（重连语义不动）、重建后账仍等价（离场账参与等价判定）。
    /// </summary>
    [Fact]
    public async Task RemoveTraveller_KeepsTicket_ExcludesSeatFromTheGame_AndSurvivesRebuild()
    {
        await using var host = new TestServerHost(seatCount: 4, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "clockmaker" },
                new() { Seat = 2, Character = "artist" },
                new() { Seat = 3, Character = "klutz" },
                new() { Seat = 4, Character = "mutant" },
            },
            "test-traveller-depart-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "butcher",
            "Good",
            null,
            "test-traveller-depart-join");
        Assert.Equal("Accepted", joined.Kind);
        var travellerSeat = new SeatId(joined.IssuedSeat!.Value);

        var removed = await storyteller.InvokeAsync<CommandResultDto>(
            "RemoveTraveller",
            travellerSeat.Value,
            "玩家需要提前离开",
            "test-traveller-depart");
        Assert.Equal("Accepted", removed.Kind);

        // 席位账里不再有这一席（角色与生命标记一起移除）。
        var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.DoesNotContain(view.Seats, entry => entry.Seat == travellerSeat.Value);

        // 席位与邀请码保留：原席位仍在名单里，重连凭账号绑定回得去（重连 / 复盘语义不动）。
        var setup = await host.GetSetupAsync();
        Assert.Contains(travellerSeat, setup.Seats);
        await using var reconnect = await host.ConnectSeatAsync(travellerSeat);
        Assert.Equal(travellerSeat.Value, host.Bundles[travellerSeat].View.Seat);

        var rebuilt = await storyteller.InvokeAsync<CommandResultDto>(
            "RebuildRoom",
            "离场后重建核对",
            "test-traveller-depart-rebuild");
        Assert.Equal("Accepted", rebuilt.Kind);
        Assert.True(rebuilt.LedgerEquivalent == true, "重建后的状态账与内存账不等价（离场账没有参与比对？）");
    }

    /// <summary>加入侧的护栏：玩家不能发、非旅行者角色、已占用席位、重复角色全部显式拒绝。</summary>
    [Fact]
    public async Task JoinTraveller_Guardrails_RejectExplicitly()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        await using var player = await host.ConnectSeatAsync(new SeatId(1));

        var byPlayer = await player.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "deviant",
            "Good",
            null,
            "test-traveller-guard-player");
        Assert.Equal("Rejected", byPlayer.Kind);
        Assert.Equal("identity.storyteller_only", byPlayer.RejectionCode);

        var notTraveller = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "clockmaker",
            "Good",
            null,
            "test-traveller-guard-character");
        Assert.Equal("Rejected", notTraveller.Kind);
        Assert.Equal("legality.traveller_character_invalid", notTraveller.RejectionCode);

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            FourNonTravellersAndADemon(),
            "test-traveller-guard-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var occupied = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            1,
            "deviant",
            "Good",
            null,
            "test-traveller-guard-occupied");
        Assert.Equal("Rejected", occupied.Kind);
        Assert.Equal("legality.seat_occupied", occupied.RejectionCode);

        var first = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "deviant",
            "Good",
            null,
            "test-traveller-guard-first");
        Assert.Equal("Accepted", first.Kind);

        var duplicated = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "deviant",
            "Good",
            null,
            "test-traveller-guard-duplicated");
        Assert.Equal("Rejected", duplicated.Kind);
        Assert.Equal("legality.character_duplicated", duplicated.RejectionCode);

        // 被拒绝的追加不会把席位写进目录（先校验后落库）。
        var setup = await host.GetSetupAsync();
        Assert.Equal(6, setup.Seats.Count);
    }

    /// <summary>邪恶揭示的护栏：必须给目标、目标必须是在局存活恶魔；善良不许带揭示列表。</summary>
    [Fact]
    public async Task JoinTraveller_EvilReveal_Guardrails_RejectExplicitly()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            FourNonTravellersAndADemon(),
            "test-traveller-reveal-guard-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var missing = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "harlot",
            "Evil",
            null,
            "test-traveller-reveal-missing");
        Assert.Equal("Rejected", missing.Kind);
        Assert.Equal("legality.traveller_reveal_required", missing.RejectionCode);

        var notDemon = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "harlot",
            "Evil",
            new[] { 1 },
            "test-traveller-reveal-not-demon");
        Assert.Equal("Rejected", notDemon.Kind);
        Assert.Equal("legality.traveller_reveal_invalid", notDemon.RejectionCode);

        var goodWithReveal = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "harlot",
            "Good",
            new[] { 5 },
            "test-traveller-reveal-good");
        Assert.Equal("Rejected", goodWithReveal.Kind);
        Assert.Equal("legality.traveller_reveal_not_allowed", goodWithReveal.RejectionCode);
    }

    /// <summary>离场侧的护栏：未加入的席位、非旅行者、重复离场、玩家身份全部显式拒绝。</summary>
    [Fact]
    public async Task RemoveTraveller_Guardrails_RejectExplicitly()
    {
        await using var host = new TestServerHost(seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        await using var player = await host.ConnectSeatAsync(new SeatId(1));

        var byPlayer = await player.InvokeAsync<CommandResultDto>(
            "RemoveTraveller",
            1,
            null,
            "test-remove-guard-player");
        Assert.Equal("Rejected", byPlayer.Kind);
        Assert.Equal("identity.storyteller_only", byPlayer.RejectionCode);

        var notJoined = await storyteller.InvokeAsync<CommandResultDto>(
            "RemoveTraveller",
            5,
            null,
            "test-remove-guard-not-joined");
        Assert.Equal("Rejected", notJoined.Kind);
        Assert.Equal("legality.traveller_not_joined", notJoined.RejectionCode);

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            FourNonTravellersAndADemon(),
            "test-remove-guard-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var notTraveller = await storyteller.InvokeAsync<CommandResultDto>(
            "RemoveTraveller",
            1,
            null,
            "test-remove-guard-not-traveller");
        Assert.Equal("Rejected", notTraveller.Kind);
        Assert.Equal("legality.not_a_traveller", notTraveller.RejectionCode);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "butcher",
            "Good",
            null,
            "test-remove-guard-join");
        Assert.Equal("Accepted", joined.Kind);
        var travellerSeat = joined.IssuedSeat!.Value;

        var removed = await storyteller.InvokeAsync<CommandResultDto>(
            "RemoveTraveller",
            travellerSeat,
            null,
            "test-remove-guard-remove");
        Assert.Equal("Accepted", removed.Kind);

        var again = await storyteller.InvokeAsync<CommandResultDto>(
            "RemoveTraveller",
            travellerSeat,
            null,
            "test-remove-guard-again");
        Assert.Equal("Rejected", again.Kind);
        Assert.Equal("legality.seat_departed", again.RejectionCode);
    }

    /// <summary>阶段进行中也允许加入 / 离场：步骤机状态必须原样保留（不能把进行中的阶段抹掉）。</summary>
    [Fact]
    public async Task JoinAndRemoveTraveller_DuringAnOpenPhase_KeepTheStepMachine()
    {
        await using var host = new TestServerHost(seatCount: 3, autoStartTestNight: true);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var before = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.NotNull(before.Phase);

        var joined = await storyteller.InvokeAsync<CommandResultDto>(
            "JoinTraveller",
            null,
            "barista",
            "Good",
            null,
            "test-traveller-mid-phase-join");
        Assert.Equal("Accepted", joined.Kind);

        var afterJoin = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Equal(before.Phase, afterJoin.Phase);
        Assert.Equal(before.SlotIndex, afterJoin.SlotIndex);

        var removed = await storyteller.InvokeAsync<CommandResultDto>(
            "RemoveTraveller",
            joined.IssuedSeat!.Value,
            "阶段中离场",
            "test-traveller-mid-phase-remove");
        Assert.Equal("Accepted", removed.Kind);

        var afterRemove = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        Assert.Equal(before.Phase, afterRemove.Phase);
        Assert.Equal(before.SlotIndex, afterRemove.SlotIndex);
    }

    /// <summary>角色事实端口把旅行者认出来（R-0045 第 4 条的规则层实现）。</summary>
    [Fact]
    public void WinConditionFacts_KnowsTravellers()
    {
        Assert.True(WinConditionFacts.Instance.IsTraveller(new CharacterId("deviant")));
        Assert.False(WinConditionFacts.Instance.IsTraveller(new CharacterId("clockmaker")));
        Assert.False(WinConditionFacts.Instance.IsTraveller(new CharacterId("not-a-character")));
    }
}
