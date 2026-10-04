using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 配板建议的真宿主链路（`GameHub.ProposeSetup`，口径见 R-0041 / R-0042 / R-0046）：
/// 建议只覆盖非旅行者席位、同种子可复现、**不落账**、玩家与匿名连接读不到；
/// 15+ 开局按非旅行者人数取行、旅行者不参与配板；提交仍走既有分配命令面，重建（重放）之后配板不变。
/// </summary>
/// <remarks>
/// 验收矩阵映射：行 4（随机可重放：显式种子 + 重建对比）、行 6（提交走既有命令面）、
/// 行 7（只说书人可见）、行 1 / 2（净分布与在场修正一致）、行 11（15+ 配板边界，R-0046）。
/// </remarks>
public sealed class SetupProposalHostTests
{
    /// <summary>说书人查建议：每席一个角色、同种子同一结果，查询本身不写事件流。</summary>
    [Fact]
    public async Task ProposeSetup_CoversEverySeat_IsReproducible_AndWritesNothing()
    {
        await using var host = new TestServerHost(autoStartTestNight: false, seatCount: 5);
        await using var storyteller = await host.ConnectStorytellerAsync();
        var before = await TestServerHost.LastSequenceAsync(host);

        var first = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", (string?)null, null);

        Assert.True(first.Ok, first.FailureMessage);
        Assert.Equal([1, 2, 3, 4, 5], first.Assignments.Select(item => item.Seat));
        Assert.Equal(5, first.Assignments.Select(item => item.Character).Distinct().Count());
        Assert.False(string.IsNullOrWhiteSpace(first.Seed));
        // 人数回显（R-0046）：5 席全部按非旅行者算，缺省语义与旧行为一致。
        Assert.Equal(5, first.NonTravellerCount);
        Assert.Equal(0, first.TravellerCount);

        // 净分布与抽到的恶魔修正一致（5 人基线 3/0/1/1；方古 +1 外来者、亡骨魔 −1 被钳到 0）。
        var outsiders = first.Distribution.Single(item => item.Type == "Outsider").Count;
        var expectedOutsiders = first.Assignments.Any(item => item.Character == "fang-gu") ? 1 : 0;
        Assert.Equal(expectedOutsiders, outsiders);
        Assert.Equal(1, first.Distribution.Single(item => item.Type == "Demon").Count);
        Assert.Equal(1, first.Distribution.Single(item => item.Type == "Minion").Count);
        Assert.Equal(5 - 1 - 1 - outsiders, first.Distribution.Single(item => item.Type == "Townsfolk").Count);

        // 同一显式种子 → 同一配板：服务端回传的种子本身就是可复现的随机输入（D-0008 / D-0011）。
        var replay = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", first.Seed, null);
        Assert.True(replay.Ok, replay.FailureMessage);
        Assert.Equal(
            first.Assignments.Select(item => item.Character),
            replay.Assignments.Select(item => item.Character));

        // 建议是瞬态的：查询不落账。
        Assert.Equal(before, await TestServerHost.LastSequenceAsync(host));
    }

    /// <summary>15+ 开局：16 席里 1 名旅行者 → 配板只覆盖 15 名非旅行者，旅行者不占任何名额（R-0046）。</summary>
    [Fact]
    public async Task ProposeSetup_WithTravellers_CoversNonTravellersOnly()
    {
        await using var host = new TestServerHost(autoStartTestNight: false, seatCount: 16);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var result = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", "traveller-seed", 15);

        Assert.True(result.Ok, result.FailureMessage);
        Assert.Equal(15, result.NonTravellerCount);
        Assert.Equal(1, result.TravellerCount);
        // 建议只覆盖非旅行者席位（按 D1：旅行者以「追加席位」进入 = 高号席，故非旅行者取低号席 1–15）。
        Assert.Equal(Enumerable.Range(1, 15), result.Assignments.Select(item => item.Seat));
        Assert.Equal(15, result.Assignments.Select(item => item.Character).Distinct().Count());
        // 净分布只统计四类型：合计恰为非旅行者人数，绝不出现旅行者类型。
        Assert.Equal(15, result.Distribution.Sum(item => item.Count));
        Assert.DoesNotContain(result.Distribution, item => item.Type == "Traveller");
        Assert.Contains(result.Notes, note => note.Contains("旅行者 1 名", StringComparison.Ordinal));

        // 同种子可复现（显式随机输入的口径不变）。
        var replay = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", "traveller-seed", 15);
        Assert.Equal(
            result.Assignments.Select(item => item.Character),
            replay.Assignments.Select(item => item.Character));
    }

    /// <summary>非旅行者 &gt;15（旅行者数不足）显式失败：不静默取近似（R-0046 第 3 条）。</summary>
    [Fact]
    public async Task ProposeSetup_OverFifteenNonTravellers_FailsExplicitly()
    {
        await using var host = new TestServerHost(autoStartTestNight: false, seatCount: 16);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var result = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", "no-traveller-seed", 16);

        Assert.False(result.Ok);
        Assert.Equal("setup.player_count_unsupported", result.FailureCode);
        Assert.Contains("旅行者", result.FailureMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(result.Assignments);
        // 人数回显让说书人面能解释「差几名旅行者」。
        Assert.Equal(16, result.NonTravellerCount);
        Assert.Equal(0, result.TravellerCount);
    }

    /// <summary>非旅行者人数超出本局席位数：显式失败，不猜（调用面契约）。</summary>
    [Fact]
    public async Task ProposeSetup_NonTravellerCountBeyondSeats_FailsExplicitly()
    {
        await using var host = new TestServerHost(autoStartTestNight: false, seatCount: 5);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var result = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", "beyond-seed", 6);

        Assert.False(result.Ok);
        Assert.Equal("setup.non_traveller_count_invalid", result.FailureCode);
        Assert.Empty(result.Assignments);
    }

    /// <summary>玩家连接与未加入的连接都读不到配板建议（D-0012：越权在 Hub 层拒绝）。</summary>
    [Fact]
    public async Task ProposeSetup_RejectsPlayersAndAnonymousConnections()
    {
        await using var host = new TestServerHost(autoStartTestNight: false, seatCount: 5);
        await using var player = await host.ConnectSeatAsync(new SeatId(1));

        var playerError = await Assert.ThrowsAsync<HubException>(
            () => player.InvokeAsync<SetupProposalDto>("ProposeSetup", (string?)null, null));
        Assert.Contains("说书人", playerError.Message, StringComparison.Ordinal);

        var anonymous = await host.ConnectAnonymousAsync();
        await Assert.ThrowsAsync<HubException>(
            () => anonymous.InvokeAsync<SetupProposalDto>("ProposeSetup", null, null, null));
        // 客户端只拿到 SignalR 的包装消息，服务端的拒绝理由看审计日志（每次都留痕）。
        Assert.Contains(host.Logs, line => line.Contains("凭据闸", StringComparison.Ordinal));
    }

    /// <summary>建议可以直接提交（既有分配命令面）；重建 = 重放之后配板一字不变——重放不重摇。</summary>
    [Fact]
    public async Task SubmittedProposal_SurvivesARebuildWithoutRedrawing()
    {
        await using var host = new TestServerHost(autoStartTestNight: false, seatCount: 5);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var proposal = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", "rebuild-seed", null);
        Assert.True(proposal.Ok, proposal.FailureMessage);

        var submitted = proposal.Assignments
            .Select(item => new SeatCharacterAssignmentDto { Seat = item.Seat, Character = item.Character })
            .ToArray();
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            submitted,
            "test-setup-proposal-assign");
        Assert.Equal("Accepted", assigned.Kind);

        var rebuilt = await storyteller.InvokeAsync<CommandResultDto>(
            "RebuildRoom",
            "配板重放核对",
            "test-setup-proposal-rebuild");
        Assert.Equal("Accepted", rebuilt.Kind);

        var view = host.Session.GetStorytellerView();
        foreach (var item in submitted)
        {
            var seat = Assert.Single(view.Seats, entry => entry.Seat == new SeatId(item.Seat));
            Assert.Equal(item.Character, seat.CharacterValue?.Value);
        }
    }

    /// <summary>首个阶段开始之后不再给建议：配板只在开局设置窗口里（与分配闸同一把尺子）。</summary>
    [Fact]
    public async Task ProposeSetup_AfterTheFirstPhase_IsRejectedExplicitly()
    {
        await using var host = new TestServerHost(seatCount: 3);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var result = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", "late-seed", null);

        Assert.False(result.Ok);
        Assert.Equal("setup.phase_started", result.FailureCode);
        Assert.Empty(result.Assignments);
    }
}
