using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 跨桌隔离（D-0024）：两桌各一条**真 SignalR 连接**同时在线，甲桌的事不许出现在乙桌。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="MultiTableIsolationTests"/> 的分工：那边用内存假存储证明"注册表的隔离由结构保证"
/// （快、但不经过宿主）；这里用**真宿主 + 真 Hub + 真 SQLite** 证明"接线之后运行时确实隔离"。
/// 两者都要，缺一不可——只测内存实现会漏掉"Hub 忘了按桌解析"这类接线错误。
/// </para>
/// <para>
/// 判据按验收规程 §4 第三行：**不该看见的人确实没看见**。
/// </para>
/// </remarks>
public sealed class MultiTableHostIsolationTests
{
    private static readonly GameId TableA = new("table-a");
    private static readonly GameId TableB = new("table-b");

    /// <summary>两桌各自开一条说书人连接：各拿各的视图，序号互不影响。</summary>
    [Fact]
    public async Task TwoTables_EachStorytellerSeesOnlyItsOwnTable()
    {
        await using var host = new TestServerHost(seatCount: 5);
        // Host 自带的是默认桌（GameServer:GameId = "default"）；这里显式建 table-a，让两桌都按标识寻址。
        await host.RegisterTableAsync(TableA, seatCount: 5);
        await host.RegisterTableAsync(TableB, seatCount: 5);

        // 制造一处**只属于甲桌**的事实：给甲桌 1 号席位起个名字。
        var tableAGame = await host.GameRegistry.GetOrCreateAsync(TableA, CancellationToken.None);
        tableAGame.SeatNames.Set(new SeatId(1), new AccountId(1), "甲桌的一号");
        var tableAView = await host.ConnectStorytellerToTableAsync(TableA);
        var tableBView = await host.ConnectStorytellerToTableAsync(TableB);

        var viewA = await tableAView.Raw.InvokeAsync<StorytellerViewDto>("GetStorytellerView", tableAView.Credential);
        var viewB = await tableBView.Raw.InvokeAsync<StorytellerViewDto>("GetStorytellerView", tableBView.Credential);

        // 正向：甲桌看得到自己的席位名。
        Assert.Contains(viewA.SeatNames, entry => entry.DisplayName == "甲桌的一号");

        // 反方向（本票核心）：乙桌的视图里**没有**甲桌的席位名——两桌各读各的读模型。
        Assert.DoesNotContain(viewB.SeatNames, entry => entry.DisplayName == "甲桌的一号");

        // 两条连接各自落在不同的会话上（事件流因此也是两条），而不是共用一局。
        var sessionA = (await host.GameRegistry.GetOrCreateAsync(TableA, CancellationToken.None)).Session;
        var sessionB = (await host.GameRegistry.GetOrCreateAsync(TableB, CancellationToken.None)).Session;
        Assert.NotSame(sessionA, sessionB);
        Assert.Equal(TableA, sessionA.GameId);
        Assert.Equal(TableB, sessionB.GameId);
    }

    /// <summary>甲桌的**房主账号**不能加入乙桌（归属按桌隔离，不是全局通行证；D-0027）。</summary>
    [Fact]
    public async Task OwnerOfOneTable_IsRejectedByAnotherTable()
    {
        await using var host = new TestServerHost(seatCount: 5);
        var ownerA = host.OwnerOf(TestServerHost.GameId);
        await host.RegisterTableAsync(TableB, seatCount: 5);

        // 拿甲桌房主的账号会话去连乙桌。
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.ServerBaseAddress, $"/hub/game?gameId={TableB.Value}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => host.ServerHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        try
        {
            await connection.StartAsync();

            // 连接本身能建立（它是乙桌的连接），但甲桌房主在乙桌不是房主。
            await Assert.ThrowsAsync<HubException>(
                () => connection.InvokeAsync<StorytellerJoinDto>("JoinStorytellerWithAccount", ownerA.AccountSession));
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    /// <summary>连接不声明桌标识：**显式拒绝**（D-0027 删掉了"缺省回落默认桌"那条路）。</summary>
    /// <remarks>
    /// 那条回落是"一张没有房主的桌"的来源，也是老客户端最后的依赖；删掉它之后，
    /// 每条连接都必须说清自己在哪一桌——说不清就没有任何可执行的动作。
    /// </remarks>
    [Fact]
    public async Task ConnectionWithoutTableId_IsRejectedExplicitly()
    {
        await using var host = new TestServerHost(seatCount: 5);
        var owner = host.OwnerOf(TestServerHost.GameId);

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.ServerBaseAddress, "/hub/game"), options =>
            {
                options.HttpMessageHandlerFactory = _ => host.ServerHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        try
        {
            await connection.StartAsync();
            var error = await Assert.ThrowsAsync<HubException>(
                () => connection.InvokeAsync<StorytellerJoinDto>(
                    "JoinStorytellerWithAccount",
                    owner.AccountSession));

            Assert.Contains("gameId", error.Message);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    /// <summary>未知的桌被显式拒绝（不静默失败、也不顺手建一张空桌）。</summary>
    [Fact]
    public async Task UnknownTable_IsRejectedExplicitly()
    {
        await using var host = new TestServerHost(seatCount: 5);
        var owner = host.OwnerOf(TestServerHost.GameId);

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.ServerBaseAddress, "/hub/game?gameId=no-such-table"), options =>
            {
                options.HttpMessageHandlerFactory = _ => host.ServerHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        try
        {
            await connection.StartAsync();
            await Assert.ThrowsAsync<HubException>(
                () => connection.InvokeAsync<StorytellerJoinDto>("JoinStorytellerWithAccount", owner.AccountSession));
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}
