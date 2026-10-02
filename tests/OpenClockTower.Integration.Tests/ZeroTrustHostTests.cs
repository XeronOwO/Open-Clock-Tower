using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 零信任负向套件（票据 <c>todo/zero-trust-security-model.md</c> 的验收矩阵）：
/// 这些用例问的不是"正常能进"，而是**越权进不来**；每一条都走真宿主 + 真 SignalR 客户端。
/// </summary>
/// <remarks>
/// <para>
/// 边界（诚实记录）：平台当前只有夜晚阶段，没有"白天"；矩阵行 5 的"白天提交夜间行动"
/// 以「阶段未开始」与「不是发给你的请求」两条真实反例覆盖阶段闸，白天阶段落地后按行补测。
/// 矩阵行 6 的"僧侣保护自己"同理：僧侣角色未实现，用**不在服务端合法集合里的选项**做等价反例。
/// </para>
/// </remarks>
public sealed class ZeroTrustHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>矩阵行 2：不持票据直接连 Hub 发命令 → 拒绝 + 审计。</summary>
    [Fact]
    public async Task Row2_ConnectionWithoutJoin_CannotInvokeCommands()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var anonymous = await host.ConnectAnonymousAsync();

        await Assert.ThrowsAsync<HubException>(
            () => anonymous.InvokeAsync<StorytellerViewDto>("GetStorytellerView", "伪造凭据"));

        Assert.Contains(host.Logs, line => line.Contains("凭据闸", StringComparison.Ordinal));
    }

    /// <summary>矩阵行 4：玩家凭据调说书人命令 → 身份闸拒绝（Application 层，带拒绝码与审计）。</summary>
    [Fact]
    public async Task Row4_PlayerCredential_CannotIssueStorytellerCommands()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));

        var result = await seat1.InvokeAsync<CommandResultDto>(
            "ForceAdvance",
            "玩家自称说书人",
            "zt-force-1");

        Assert.Equal("Rejected", result.Kind);
        Assert.Equal("identity.storyteller_only", result.RejectionCode);
        Assert.Contains(host.Logs, line => line.Contains("identity.storyteller_only", StringComparison.Ordinal));
    }

    /// <summary>
    /// 矩阵行 3：凭据绑定"下发它的那条连接"——新连接持旧凭据被拒；同席重连后旧连接的凭据立即作废。
    /// </summary>
    [Fact]
    public async Task Row3_CredentialsAreBoundToTheIssuingConnection()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var first = await host.ConnectSeatAsync(new SeatId(2));
        var staleCredential = first.Credential;

        // 同席"重连"：新连接换新凭据。
        await using var reconnected = await host.ConnectSeatAsync(new SeatId(2));
        Assert.NotEqual(staleCredential, reconnected.Credential);

        // (a) 被顶替的旧连接立即不能再发命令（即使它的 TCP 还没断）。
        await Assert.ThrowsAsync<HubException>(
            () => first.InvokeAsync<CommandResultDto>("ForceAdvance", "旧连接继续发命令", "zt-stale-1"));

        // (b) 一条没加入过的新连接出示旧凭据 → 拒绝（连接上没有有效凭据）。
        var anonymous = await host.ConnectAnonymousAsync();
        await Assert.ThrowsAsync<HubException>(
            () => anonymous.InvokeAsync<StorytellerViewDto>("GetStorytellerView", staleCredential));

        // (c) 另一条已加入的连接出示别人的凭据 → 拒绝（凭据与这条连接不匹配）。
        await using var seat3 = await host.ConnectSeatAsync(new SeatId(3));
        await Assert.ThrowsAsync<HubException>(
            () => seat3.InvokeRawAsync<CommandResultDto>("ForceAdvance", staleCredential, "冒用", "zt-stale-2"));

        Assert.Contains(host.Logs, line => line.Contains("凭据闸", StringComparison.Ordinal));
    }

    /// <summary>矩阵行 1 / 5：别的席位的请求不是你的——提交被阶段闸拒绝（不泄露他人请求内容）。</summary>
    [Fact]
    public async Task Row1_5_OtherSeatsPendingRequest_IsRejectedByPhaseGate()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        await using var seat2 = await host.ConnectSeatAsync(new SeatId(2));
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait),
            "夹具夜晚应把请求发给 1 号");

        var request = requests.First();
        var result = await seat2.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            request.RequestId,
            request.Options[0].Value,
            "zt-cross-1",
            0L);

        Assert.Equal("Rejected", result.Kind);
        Assert.Equal("phase.no_request_for_you", result.RejectionCode);
        Assert.Contains(host.Logs, line => line.Contains("phase.no_request_for_you", StringComparison.Ordinal));
    }

    /// <summary>矩阵行 5（阶段闸的另一面）：阶段没开始就提交 → 拒绝。</summary>
    [Fact]
    public async Task Row5_SubmitBeforeAnyPhase_IsRejected()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3, autoStartTestNight: false);
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));

        var result = await seat1.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            "unknown-request",
            "seat:2",
            "zt-nophase-1",
            0L);

        Assert.Equal("Rejected", result.Kind);
        Assert.Equal("phase.no_request_for_you", result.RejectionCode);
    }

    /// <summary>
    /// 矩阵行 3（并发面）：同一席位被多条连接同时抢占时，**只有最后签发的凭据有效**。
    /// 注册表的"读旧绑定 → 作废 → 写新绑定"必须在同一临界区里完成，否则会留下多条都有效的凭据。
    /// </summary>
    [Fact]
    public async Task Row3_ConcurrentJoins_LeaveExactlyOneValidCredential()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var clients = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => host.ConnectSeatAsync(new SeatId(1))));

        var accepted = 0;
        var credentialRejected = 0;
        foreach (var client in clients)
        {
            try
            {
                // 凭据有效 → 进入四道闸（夹具夜里 1 号有挂起请求，会落到阶段闸或合法性闸的拒绝）；
                // 凭据失效 → Hub 层直接拒绝。两者形状不同，正好用来区分。
                var result = await client.InvokeAsync<CommandResultDto>(
                    "SubmitResponse",
                    "not-a-request",
                    "seat:1",
                    $"zt-race-{Guid.NewGuid():N}",
                    0L);
                if (result.Kind == "Rejected")
                {
                    accepted++;
                }
            }
            catch (HubException)
            {
                credentialRejected++;
            }
        }

        Assert.Equal(1, accepted);
        Assert.Equal(clients.Length - 1, credentialRejected);
    }

    /// <summary>矩阵行 6：选项不在服务端算出的合法集合里 → 合法性闸拒绝。</summary>
    [Fact]
    public async Task Row6_IllegalOption_IsRejectedByLegalityGate()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        var requests = new ConcurrentQueue<OperationRequestDto>();
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1), requests.Enqueue);
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => !requests.IsEmpty, Wait),
            "夹具夜晚应把请求发给 1 号");

        var request = requests.First();
        var result = await seat1.InvokeAsync<CommandResultDto>(
            "SubmitResponse",
            request.RequestId,
            "seat:99",
            "zt-illegal-1",
            0L);

        Assert.Equal("Rejected", result.Kind);
        Assert.Equal("legality.option_not_legal", result.RejectionCode);
        Assert.Contains(host.Logs, line => line.Contains("legality.option_not_legal", StringComparison.Ordinal));
    }

    /// <summary>矩阵行 11：拒绝都留下了可定位的审计，且**日志里没有凭据明文**（只有短指纹）。</summary>
    [Fact]
    public async Task Row11_RejectionsAreAudited_AndCredentialsNeverLoggedInPlaintext()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3);
        await using var seat1 = await host.ConnectSeatAsync(new SeatId(1));
        var credential = seat1.Credential;

        // 身份路径：玩家凭据读说书人视图（查询被拒）。
        await Assert.ThrowsAsync<HubException>(
            () => seat1.InvokeAsync<CommandResultDto>("GetStorytellerView"));

        // 凭据路径：伪造凭据调命令（凭据闸拒绝）。
        await Assert.ThrowsAsync<HubException>(
            () => seat1.InvokeRawAsync<CommandResultDto>("ForceAdvance", "伪造凭据", "越权", "zt-audit-2"));

        var logText = string.Join('\n', host.Logs);
        Assert.Contains("查询被拒（身份）", logText, StringComparison.Ordinal);
        Assert.Contains("命令被拒绝（凭据闸）", logText, StringComparison.Ordinal);
        Assert.DoesNotContain(credential, logText, StringComparison.Ordinal);
    }
}
