using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 配板建议的真宿主链路（`GameHub.ProposeSetup`，口径见 R-0041 / R-0042）：
/// 建议覆盖每一席、同种子可复现、**不落账**、玩家与匿名连接读不到；
/// 提交仍走既有分配命令面，重建（重放）之后配板不变。
/// </summary>
/// <remarks>
/// 验收矩阵映射：行 4（随机可重放：显式种子 + 重建对比）、行 6（提交走既有命令面）、
/// 行 7（只说书人可见）、行 1 / 2（净分布与在场修正一致）。
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

        var first = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", (string?)null);

        Assert.True(first.Ok, first.FailureMessage);
        Assert.Equal([1, 2, 3, 4, 5], first.Assignments.Select(item => item.Seat));
        Assert.Equal(5, first.Assignments.Select(item => item.Character).Distinct().Count());
        Assert.False(string.IsNullOrWhiteSpace(first.Seed));

        // 净分布与抽到的恶魔修正一致（5 人基线 3/0/1/1；方古 +1 外来者、亡骨魔 −1 被钳到 0）。
        var outsiders = first.Distribution.Single(item => item.Type == "Outsider").Count;
        var expectedOutsiders = first.Assignments.Any(item => item.Character == "fang-gu") ? 1 : 0;
        Assert.Equal(expectedOutsiders, outsiders);
        Assert.Equal(1, first.Distribution.Single(item => item.Type == "Demon").Count);
        Assert.Equal(1, first.Distribution.Single(item => item.Type == "Minion").Count);
        Assert.Equal(5 - 1 - 1 - outsiders, first.Distribution.Single(item => item.Type == "Townsfolk").Count);

        // 同一显式种子 → 同一配板：服务端回传的种子本身就是可复现的随机输入（D-0008 / D-0011）。
        var replay = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", first.Seed);
        Assert.True(replay.Ok, replay.FailureMessage);
        Assert.Equal(
            first.Assignments.Select(item => item.Character),
            replay.Assignments.Select(item => item.Character));

        // 建议是瞬态的：查询不落账。
        Assert.Equal(before, await TestServerHost.LastSequenceAsync(host));
    }

    /// <summary>玩家连接与未加入的连接都读不到配板建议（D-0012：越权在 Hub 层拒绝）。</summary>
    [Fact]
    public async Task ProposeSetup_RejectsPlayersAndAnonymousConnections()
    {
        await using var host = new TestServerHost(autoStartTestNight: false, seatCount: 5);
        await using var player = await host.ConnectSeatAsync(new SeatId(1));

        var playerError = await Assert.ThrowsAsync<HubException>(
            () => player.InvokeAsync<SetupProposalDto>("ProposeSetup", (string?)null));
        Assert.Contains("说书人", playerError.Message, StringComparison.Ordinal);

        var anonymous = await host.ConnectAnonymousAsync();
        await Assert.ThrowsAsync<HubException>(
            () => anonymous.InvokeAsync<SetupProposalDto>("ProposeSetup", null, null));
        // 客户端只拿到 SignalR 的包装消息，服务端的拒绝理由看审计日志（每次都留痕）。
        Assert.Contains(host.Logs, line => line.Contains("凭据闸", StringComparison.Ordinal));
    }

    /// <summary>建议可以直接提交（既有分配命令面）；重建 = 重放之后配板一字不变——重放不重摇。</summary>
    [Fact]
    public async Task SubmittedProposal_SurvivesARebuildWithoutRedrawing()
    {
        await using var host = new TestServerHost(autoStartTestNight: false, seatCount: 5);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var proposal = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", "rebuild-seed");
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

        var result = await storyteller.InvokeAsync<SetupProposalDto>("ProposeSetup", "late-seed");

        Assert.False(result.Ok);
        Assert.Equal("setup.phase_started", result.FailureCode);
        Assert.Empty(result.Assignments);
    }
}
