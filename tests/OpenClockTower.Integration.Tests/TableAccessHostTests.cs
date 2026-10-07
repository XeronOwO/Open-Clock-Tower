using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 桌的访问模式（D-0037）：公开桌 / 邀请制桌两种形态、说书人切换、**切换即推**给该桌全部连接。
/// </summary>
/// <remarks>
/// <para>
/// 本批把当年的"锁桌"正名为"邀请制"：那一列的**效果**从来没变过（自助入座被拒、持邀请码者照进），
/// 变的是口径——审计 G-A4-2 把"锁桌没拦住票据入座"记成缺陷，按新模型那正是设计。
/// 闸与入口的两半分别由 <see cref="SelfServiceJoinHostTests"/> 判（拒 / 进），
/// 这里判**第三条**：切换要当场到达该桌的每一类连接。
/// </para>
/// <para>
/// 推送覆盖面刻意分成两类分别断言：说书人连接（他刚点的按钮，界面要跟着变）与
/// **在场玩家**（他没有做任何操作，界面也得跟着变）——只推一端是很容易漏的那一半。
/// </para>
/// </remarks>
public sealed class TableAccessHostTests
{
    private static readonly GameId TableA = new("table-a");
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>切成邀请制：说书人与在场玩家**各收到一条**推送（不刷新、不重连就变）。</summary>
    /// <remarks>
    /// 收集用 <see cref="ConcurrentQueue{T}"/> 而**不是** <c>List</c>：推送在 SignalR 的接收线程上写、
    /// 断言在测试线程上读，<c>List</c> 在这两个线程之间既不安全也会丢项（本轮实测：偶发只收到一半，
    /// 看起来像"推送没到"，其实是收集器吃掉了它）。
    /// </remarks>
    [Fact]
    public async Task SwitchingToInviteOnly_PushesToStorytellerAndEverySeatedPlayer()
    {
        await using var host = new TestServerHost(seatCount: 3);
        await host.RegisterTableAsync(TableA, seatCount: 3);

        var storytellerPushes = new ConcurrentQueue<TableAccessDto>();
        var seat1Pushes = new ConcurrentQueue<TableAccessDto>();
        var seat2Pushes = new ConcurrentQueue<TableAccessDto>();

        var storyteller = await host.ConnectStorytellerToTableAsync(TableA);
        storyteller.Raw.On<TableAccessDto>("ReceiveTableAccessChanged", dto => storytellerPushes.Enqueue(dto));

        // 两席在场玩家：推送面按"本桌已绑定席位"算，所以两端都要收到。
        await using var seat1 = await host.ConnectSeatToTableAsync(TableA, new SeatId(1), onAccess: dto => seat1Pushes.Enqueue(dto));
        await using var seat2 = await host.ConnectSeatToTableAsync(TableA, new SeatId(2), onAccess: dto => seat2Pushes.Enqueue(dto));

        Assert.True(await storyteller.Raw.InvokeAsync<bool>("SetTableInviteOnly", storyteller.Credential, true));

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => seat1Pushes.Any(dto => dto.InviteOnly) && seat2Pushes.Any(dto => dto.InviteOnly),
                Wait),
            $"在场玩家没有收到访问模式推送：说书人 {storytellerPushes.Count} 条、"
                + $"1 号 {seat1Pushes.Count} 条、2 号 {seat2Pushes.Count} 条");
        Assert.All(storytellerPushes, dto => Assert.True(dto.InviteOnly));
        Assert.All(seat1Pushes, dto => Assert.Equal(TableA.Value, dto.GameId));
        Assert.All(seat2Pushes, dto => Assert.Equal(TableA.Value, dto.GameId));

        // 再切回公开：两端**各再收到一条 false**（推送是"当前状态"，不是"单向开启"）。
        Assert.False(await storyteller.Raw.InvokeAsync<bool>("SetTableInviteOnly", storyteller.Credential, false));
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => seat1Pushes.Any(dto => !dto.InviteOnly) && seat2Pushes.Any(dto => !dto.InviteOnly),
                Wait),
            $"切回公开桌的推送没到：1 号 {seat1Pushes.Count(dto => !dto.InviteOnly)} 条、"
                + $"2 号 {seat2Pushes.Count(dto => !dto.InviteOnly)} 条");
    }

    /// <summary>反方向：**别的桌**的连接一条都收不到——访问模式是桌级事实，不跨桌广播。</summary>
    [Fact]
    public async Task SwitchingAccessMode_DoesNotReachOtherTables()
    {
        await using var host = new TestServerHost(seatCount: 3);
        await host.RegisterTableAsync(TableA, seatCount: 3);
        var tableB = new GameId("table-b");
        await host.RegisterTableAsync(tableB, seatCount: 3);

        var otherPushes = new ConcurrentQueue<TableAccessDto>();
        await using var otherSeat = await host.ConnectSeatToTableAsync(tableB, new SeatId(1), onAccess: dto => otherPushes.Enqueue(dto));

        var storyteller = await host.ConnectStorytellerToTableAsync(TableA);

        // 本桌也坐一个人：它是"推到了"的阳性对照——没有它，"别的桌没收到"可能只是这条推送整条坏了。
        var selfPushes = new ConcurrentQueue<TableAccessDto>();
        await using var selfSeat = await host.ConnectSeatToTableAsync(TableA, new SeatId(1), onAccess: dto => selfPushes.Enqueue(dto));

        Assert.True(await storyteller.Raw.InvokeAsync<bool>("SetTableInviteOnly", storyteller.Credential, true));
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => !selfPushes.IsEmpty, Wait),
            "本桌的推送没到，观察窗口不成立");

        Assert.Empty(otherPushes);
    }

    /// <summary>只在**真的改了**的时候推：重复设同一个值不发空包。</summary>
    [Fact]
    public async Task SettingTheSameValueTwice_PushesOnlyOnce()
    {
        await using var host = new TestServerHost(seatCount: 3);
        await host.RegisterTableAsync(TableA, seatCount: 3);

        var pushes = new ConcurrentQueue<TableAccessDto>();
        await using var seat = await host.ConnectSeatToTableAsync(TableA, new SeatId(1), onAccess: dto => pushes.Enqueue(dto));

        var storyteller = await host.ConnectStorytellerToTableAsync(TableA);
        Assert.True(await storyteller.Raw.InvokeAsync<bool>("SetTableInviteOnly", storyteller.Credential, true));
        Assert.True(await TestServerHost.WaitUntilAsync(() => pushes.Count == 1, Wait), "第一条推送没到");

        Assert.True(await storyteller.Raw.InvokeAsync<bool>("SetTableInviteOnly", storyteller.Credential, true));

        // 没有第二次变化：再等一小会儿，计数必须还是一。
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.Single(pushes);
    }

    /// <summary>玩家连接改不了访问模式（说书人闸；反方向由授权面表驱动扫描再扫一遍）。</summary>
    [Fact]
    public async Task PlayerConnection_CannotChangeAccessMode()
    {
        await using var host = new TestServerHost(seatCount: 3);
        await host.RegisterTableAsync(TableA, seatCount: 3);
        await using var seat = await host.ConnectSeatToTableAsync(TableA, new SeatId(1), onAccess: null);

        var rejected = await Assert.ThrowsAsync<HubException>(
            () => seat.InvokeAsync<bool>("SetTableInviteOnly", true));
        Assert.Contains("不是有效的说书人连接", rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>在大厅里如实呈现：邀请制桌**仍然列出**（不隐藏），带邀请制标记。</summary>
    [Fact]
    public async Task Lobby_StillListsInviteOnlyTables_WithTheFlag()
    {
        await using var host = new TestServerHost(seatCount: 3);
        await host.RegisterTableAsync(TableA, seatCount: 3);

        var storyteller = await host.ConnectStorytellerToTableAsync(TableA);
        Assert.True(await storyteller.Raw.InvokeAsync<bool>("SetTableInviteOnly", storyteller.Credential, true));

        await using var account = await host.ConnectAccountAsync();
        var tables = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", null);

        var table = Assert.Single(tables, item => item.GameId == TableA.Value);
        Assert.True(table.InviteOnly);
        Assert.False(table.Started);
    }
}
