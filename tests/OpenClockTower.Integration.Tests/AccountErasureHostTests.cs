using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 账号注销在**真宿主 + 真库**上的判据（M5 / G-A1-6）：删到什么程度、撤销打到哪、公开面还剩什么。
/// </summary>
/// <remarks>
/// <para>
/// 注销是**不可逆**的自助动作，所以这一族用例的重点不是"成功返回 true"，
/// 而是三件事各有运行时证据：**该删的删干净**（账号行 / 席位认领 / 归属）、
/// **该撤的当场撤掉**（下一次进门与已经进门的连接）、**不该动的没动**（别人的账号、别人的对局记录）。
/// </para>
/// <para>
/// 判据尽量取库里的读数（真删了没有）与真 Hub 的答复（撤销生效没有），
/// 而不是"回执说成功了"——回执只证明代码走到了那一行。
/// </para>
/// </remarks>
public sealed class AccountErasureHostTests
{
    /// <summary>凭据闸被关上的证据：文案是 <c>ConnectionGateMessage</c>（与 G-A2-1 同一把尺）。</summary>
    private const string ConnectionGateMessage = "连接凭据无效";

    /// <summary>注销口令（夹具账号统一用它）。</summary>
    private const string Password = "password-123";

    /// <summary>
    /// 注销把这个人**从库里抹掉**：账号行没了、席位认领释放了、他开的桌还在但没有主人，
    /// 而公开面上不再出现这个名字（名字是会话层读时解析的，事件流里本来就没有）。
    /// </summary>
    [Fact]
    public async Task DeleteAccount_ErasesTheAccount_AndLeavesTheTableWithoutAnOwner()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var leaver = await TestServerHost.RegisterAccountAsync(account, "leaver", "要走的人", Password);
        Assert.True(leaver.Ok);

        // 他开了一桌、又在默认桌坐了一个席位：注销要同时清掉"归属"与"席位认领"这两样。
        var owned = new GameId("owned-by-leaver");
        await host.RegisterTableAsync(
            owned,
            3,
            new FixtureAccount(new AccountId(leaver.Id), leaver.Username, leaver.DisplayName, Password, leaver.AccountSession!));
        await using var seat = await host.ConnectSeatAsync(new SeatId(1), accountSession: leaver.AccountSession);

        Assert.Equal(1, CountSeatBindings(host.DatabasePath, leaver.Id));
        Assert.Contains(
            "要走的人",
            host.Session.GetStorytellerView().SeatNames.Select(name => name.DisplayName));

        var deleted = await account.InvokeAsync<AccountDto>("DeleteAccount", leaver.AccountSession, Password);
        Assert.True(deleted.Ok, deleted.Message);

        // ① 账号行没了；② 席位认领释放了；③ 他开的桌留着，但归属清空（不毁掉别人的对局）。
        Assert.Equal(0, CountUsers(host.DatabasePath, leaver.Id));
        Assert.Equal(0, CountSeatBindings(host.DatabasePath, leaver.Id));
        Assert.Equal(1, CountGames(host.DatabasePath, owned));
        Assert.Null(ReadOwnerAccountId(host.DatabasePath, owned));

        // ④ "下一次进门"被拒：旧账号会话当场失效。
        var resumed = await account.InvokeAsync<AccountDto>("Resume", leaver.AccountSession);
        Assert.False(resumed.Ok);

        // ⑤ **已经进门**的那条连接也被踢（M2 / G-A2-1 的撤销面，注销是账号级撤销）。
        Assert.True(await IsRejectedByConnectionGateAsync(seat, "erasure-after"));

        // ⑥ 公开面：那一席空出来了，他开的桌不再属于任何人，玩家名从视图里消失。
        var tables = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", null);
        Assert.DoesNotContain(1, Assert.Single(tables, item => item.GameId == TestServerHost.GameId.Value).OccupiedSeatNumbers);
        Assert.False(Assert.Single(tables, item => item.GameId == owned.Value).CreatedByMe);
        Assert.DoesNotContain(
            "要走的人",
            host.Session.GetStorytellerView().SeatNames.Select(name => name.DisplayName));
    }

    /// <summary>
    /// 口令不对就**什么都不删**，而且反复试会被限速闸拦下——注销的确认口令也要跑一次慢哈希。
    /// </summary>
    /// <remarks>
    /// 会话比口令好拿得多（一台没锁屏的机器就够了），如果注销只认会话，那么"猜口令"就成了一条
    /// 不限速的爆破入口。这条判据同时锁住两件事：拒得干净、拒得够快。
    /// </remarks>
    [Fact]
    public async Task DeleteAccount_WithWrongPassword_IsRejected_AndThrottled()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var survivor = await TestServerHost.RegisterAccountAsync(account, "survivor", "还在的人", Password);
        Assert.True(survivor.Ok);

        // 额度默认 5：前 5 次按"口令不对"拒。
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var denied = await account.InvokeAsync<AccountDto>("DeleteAccount", survivor.AccountSession, "猜的口令");
            Assert.False(denied.Ok);
            Assert.Equal("invalid_credentials", denied.Code);
        }

        // 第 6 次连**正确口令**也进不去：闸在慢哈希之前（这条就是"拒绝发生在哈希之前"的读数）。
        var throttled = await account.InvokeAsync<AccountDto>("DeleteAccount", survivor.AccountSession, Password);
        Assert.False(throttled.Ok);
        Assert.Equal("too_many_attempts", throttled.Code);

        // 账号一个字节都没少，会话照样能用（拒绝不该顺手把人踢下线）。
        Assert.Equal(1, CountUsers(host.DatabasePath, survivor.Id));
        Assert.True((await account.InvokeAsync<AccountDto>("Resume", survivor.AccountSession)).Ok);
    }

    /// <summary>
    /// 反方向（隔离）：注销一个账号**不碰别人**——别的账号、别的席位、别桌的归属都原样。
    /// </summary>
    [Fact]
    public async Task DeleteAccount_DoesNotTouchOtherAccounts()
    {
        await using var host = new TestServerHost(seatCount: 3);
        var account = await host.ConnectAccountAsync();
        var leaver = await TestServerHost.RegisterAccountAsync(account, "leaver", "要走的人", Password);
        var stayer = await TestServerHost.RegisterAccountAsync(account, "stayer", "留下的人", Password);
        await using var stayerSeat = await host.ConnectSeatAsync(new SeatId(2), accountSession: stayer.AccountSession);

        var deleted = await account.InvokeAsync<AccountDto>("DeleteAccount", leaver.AccountSession, Password);
        Assert.True(deleted.Ok, deleted.Message);

        Assert.Equal(1, CountUsers(host.DatabasePath, stayer.Id));
        Assert.Equal(1, CountSeatBindings(host.DatabasePath, stayer.Id));
        Assert.False(await IsRejectedByConnectionGateAsync(stayerSeat, "stayer-after"));
        Assert.Contains(
            "留下的人",
            host.Session.GetStorytellerView().SeatNames.Select(name => name.DisplayName));
    }

    /// <summary>这条连接的凭据是不是已被凭据闸拒了（领域闸的拒不算，见 G-A2-1 的同名判据）。</summary>
    private static async Task<bool> IsRejectedByConnectionGateAsync(GameClient client, string idempotencyKey)
    {
        try
        {
            await client.InvokeAsync<CommandResultDto>("Nominate", 2, idempotencyKey);
            return false;
        }
        catch (HubException exception)
        {
            return exception.Message.Contains(ConnectionGateMessage, StringComparison.Ordinal);
        }
    }

    private static long CountUsers(string databasePath, int accountId) =>
        Scalar(databasePath, "SELECT COUNT(*) FROM Users WHERE Id = $value;", accountId);

    private static long CountSeatBindings(string databasePath, int accountId) =>
        Scalar(databasePath, "SELECT COUNT(*) FROM SeatBindings WHERE AccountId = $value;", accountId);

    private static long CountGames(string databasePath, GameId gameId) =>
        Scalar(databasePath, "SELECT COUNT(*) FROM Games WHERE GameId = $value;", gameId.Value);

    private static int? ReadOwnerAccountId(string databasePath, GameId gameId)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CreatedByAccountId FROM Games WHERE GameId = $value;";
        command.Parameters.AddWithValue("$value", gameId.Value);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    /// <summary>读一个标量（夹具直接查库：注销"删干净了没有"只能从库里判）。</summary>
    private static long Scalar(string databasePath, string sql, object value)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$value", value);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
