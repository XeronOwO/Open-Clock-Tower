using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 席位邀请码的**凭据形态**（审计 G-A2-2，High）：只存哈希、有有效期、可轮换、只在签发那一次出现。
/// </summary>
/// <remarks>
/// <para>
/// 判据来自审计条目的「怎么验证修好了」：**库里不再有可用明文票据的读数** + **旧票据被拒的用例**。
/// 前者读的是**原始库文件字节**（EF 读回来的对象已经把凭据藏起来了，拿它判"库里没有明文"是自己证明自己），
/// 后者走真链路入座（本宿主 + 真 SignalR + 真 SQLite）。
/// </para>
/// <para>
/// 背景：邀请码此前就是席位票据本身——`seat-2-<GUID>` 明文写进 <c>Games.SeatsJson</c>、与席位同寿、
/// 不可作废，比较还是 <c>string.Equals(…, Ordinal)</c>。拿到库或备份的人可以长期冒名入座，
/// 不受"8 小时会话过期"的约束。
/// </para>
/// </remarks>
public sealed class SeatInvitationHostTests
{
    /// <summary>另一张桌的标识（跨桌用例：邀请码只在自己那一桌上有效）。</summary>
    /// <remarks>短是硬约束：夹具的房主登录名是 `fixture-owner-<桌标识>`，登录名上限 24 字符。</remarks>
    private static readonly GameId OtherTable = new("inv-other");

    /// <summary>核心判据：邀请码的明文**不在库文件里**（不是"读不出来"，是真的没有）。</summary>
    [Fact]
    public async Task InviteCode_IsNotStoredInPlaintext()
    {
        await using var host = new TestServerHost(seatCount: 3, autoStartTestNight: false);

        var code = await host.IssueInviteCodeAsync(new SeatId(2));
        Assert.False(string.IsNullOrWhiteSpace(code));

        // 库 + WAL 一起读：这一行可能还没落进主库文件（WAL 模式下很正常），
        // 只读主库会让"明文在 WAL 里"漏过去。
        var stored = ReadDatabaseText(host.DatabasePath);
        Assert.DoesNotContain(code, stored, StringComparison.Ordinal);

        // 反向成立：库里没有明文，但玩家拿着它**真的进得来**（不是"为了安全把功能弄坏"）。
        var account = await host.SeatFixtureAccountAsync(new SeatId(2));
        await using var connection = await host.ConnectAnonymousAsync();
        var joined = await connection.InvokeAsync<SeatJoinDto>("JoinByInviteCode", code, account.AccountSession, 0L);
        Assert.Equal(2, joined.Bundle.View.Seat);
    }

    /// <summary>轮换：重新签发之后，**旧的那一枚当场失效**，新的照常可用。</summary>
    /// <remarks>
    /// 这是"码发错人了"的收场动作：没有它，泄露出去的码只能等它自己过期。
    /// </remarks>
    [Fact]
    public async Task Rotation_InvalidatesThePreviousCode()
    {
        await using var host = new TestServerHost(seatCount: 3, autoStartTestNight: false);

        var first = await host.IssueInviteCodeAsync(new SeatId(2));
        var second = await host.RotateInviteCodeAsync(new SeatId(2));
        Assert.NotEqual(first, second);

        var account = await host.SeatFixtureAccountAsync(new SeatId(2));
        await using var stale = await host.ConnectAnonymousAsync();
        var rejected = await Assert.ThrowsAsync<HubException>(
            () => stale.InvokeAsync<SeatJoinDto>("JoinByInviteCode", first, account.AccountSession, 0L));
        Assert.Contains("邀请码无效", rejected.Message, StringComparison.Ordinal);

        await using var fresh = await host.ConnectAnonymousAsync();
        var joined = await fresh.InvokeAsync<SeatJoinDto>("JoinByInviteCode", second, account.AccountSession, 0L);
        Assert.Equal(2, joined.Bundle.View.Seat);
    }

    /// <summary>有效期：过期的邀请码被拒，而且文案**说得出"过期"**（玩家知道该去再要一个）。</summary>
    /// <remarks>有效期配成 0 = 签出来当场过期；这是"这条闸真的在判时间"的最小可复现读数。</remarks>
    [Fact]
    public async Task ExpiredInviteCode_IsRejected()
    {
        await using var host = new TestServerHost(
            seatCount: 3,
            autoStartTestNight: false,
            seatInvitationLifetimeHours: 0);

        var code = await host.IssueInviteCodeAsync(new SeatId(2));
        var account = await host.SeatFixtureAccountAsync(new SeatId(2));
        await using var connection = await host.ConnectAnonymousAsync();

        var rejected = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync<SeatJoinDto>("JoinByInviteCode", code, account.AccountSession, 0L));
        Assert.Contains("已过期", rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>邀请码只在自己那一桌上有效：拿甲桌的码进乙桌，核验在乙桌自己的凭据表里查不到。</summary>
    [Fact]
    public async Task InviteCode_FromAnotherTable_IsRejected()
    {
        await using var host = new TestServerHost(seatCount: 3, autoStartTestNight: false);
        await host.RegisterTableAsync(OtherTable, seatCount: 3);

        var code = await host.IssueInviteCodeAsync(OtherTable, new SeatId(2));
        var account = await host.SeatFixtureAccountAsync(new SeatId(2));
        await using var connection = await host.ConnectAnonymousAsync();

        var rejected = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync<SeatJoinDto>("JoinByInviteCode", code, account.AccountSession, 0L));
        Assert.Contains("没有这枚邀请码", rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>读库文件的文本形态（主库 + WAL；后者可能不存在）。</summary>
    /// <remarks>
    /// 库正被宿主打开着，所以必须显式允许共享读写——按独占方式打开会直接 IOException
    /// （真机读数：这条曾经把"没有明文"判成"读不出来"，而两者的结论天差地别）。
    /// </remarks>
    private static string ReadDatabaseText(string databasePath)
    {
        var builder = new StringBuilder();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal" })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            builder.Append(reader.ReadToEnd());
        }

        Assert.NotEqual(0, builder.Length);
        return builder.ToString();
    }
}
