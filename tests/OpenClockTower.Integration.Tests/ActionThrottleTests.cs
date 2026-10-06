using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClockTower.Application;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 动作准入的口径（M4 / G-A5-6 · G-A5-8 的频率半）：入座与写文本各按自己的桶计数。
/// </summary>
/// <remarks>
/// <para>
/// 两类动作的代价不同，所以桶也不同：**入座**按（来源 + 桌）与（来源）两个桶判，
/// **写文本**按（来源 + 桌 + 身份）判。这里逐条钉死"哪一类、按什么分桶、超了给什么"，
/// 免得日后把两个桶合成一个——合了就会出现"另一桌刷满、这一桌也进不去"的连坐。
/// </para>
/// <para>
/// 计数口径是**准入即计数**（不看命令最终成不成）：一条被内核拒绝的入座同样已经把事件流读了一遍，
/// 所以它必须算数。这条与账号入口的"失败才计数"相反，是有意的（见 <see cref="ActionThrottle"/>）。
/// </para>
/// </remarks>
public sealed class ActionThrottleTests
{
    private const string Client = "198.51.100.1";
    private const string OtherClient = "198.51.100.2";
    private static readonly GameId Table = new("table-one");
    private static readonly GameId OtherTable = new("table-two");

    [Fact]
    public void JoinBudget_IsPerClientAndGame_AndDoesNotLeakAcrossTables()
    {
        var clock = new MutableClock();
        var throttle = Create(new ActionThrottleOptions { JoinCallsPerClientAndGame = 3, JoinCallsPerClient = 100 }, clock);
        var caller = new CallerContext(Client, "connection-1");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            throttle.AdmitJoin(Table, caller);
        }

        var blocked = Assert.Throws<HubException>(() => throttle.AdmitJoin(Table, caller));
        Assert.Contains("秒后再试", blocked.Message, StringComparison.Ordinal);

        // 反方向一：另一桌不受牵连（额度按桌分开算）。
        throttle.AdmitJoin(OtherTable, caller);

        // 反方向二：另一个来源不受牵连。
        throttle.AdmitJoin(Table, new CallerContext(OtherClient, "connection-2"));
    }

    [Fact]
    public void JoinBudget_AlsoHasAClientWideBucket_SoHoppingTablesDoesNotEscapeIt()
    {
        var clock = new MutableClock();
        var throttle = Create(new ActionThrottleOptions { JoinCallsPerClientAndGame = 100, JoinCallsPerClient = 2 }, clock);
        var caller = new CallerContext(Client, "connection-1");

        throttle.AdmitJoin(Table, caller);
        throttle.AdmitJoin(OtherTable, caller);

        // 第三个**不同的桌**也进不去：跨桌的那个桶到顶了（换桌不是绕过限速的办法）。
        Assert.Throws<HubException>(() => throttle.AdmitJoin(new GameId("table-three"), caller));
    }

    [Fact]
    public void WriteTextBudget_IsPerActor_SoOneSeatCannotUseUpTheStorytellersBudget()
    {
        var clock = new MutableClock();
        var throttle = Create(new ActionThrottleOptions { WriteTextCallsPerActor = 2 }, clock);
        var caller = new CallerContext(Client, "connection-1");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            throttle.AdmitCommand(Table, Actor.Storyteller(), TextCommand(), caller);
        }

        Assert.Throws<HubException>(() => throttle.AdmitCommand(Table, Actor.Storyteller(), TextCommand(), caller));

        // 另一个身份（1 号席）在同一桌、同一来源下照常可用。
        throttle.AdmitCommand(Table, Actor.Player(new SeatId(1)), TextCommand(), caller);
    }

    [Fact]
    public void CommandsWithoutClientText_AreNotCountedAtAll()
    {
        var clock = new MutableClock();
        var throttle = Create(new ActionThrottleOptions { WriteTextCallsPerActor = 1 }, clock);
        var caller = new CallerContext(Client, "connection-1");

        // 不带文本的命令随便发：它们各自有合法性闸，且不写自由文本。
        for (var attempt = 0; attempt < 10; attempt++)
        {
            throttle.AdmitCommand(Table, Actor.Storyteller(), new StartDayCommand(), caller);
        }

        Assert.Equal(0, throttle.TrackedKeys);
    }

    [Fact]
    public void Budget_IsReleasedWhenTheWindowExpires()
    {
        var clock = new MutableClock();
        var throttle = Create(new ActionThrottleOptions { JoinCallsPerClientAndGame = 1, JoinCallsPerClient = 1 }, clock);
        var caller = new CallerContext(Client, "connection-1");

        throttle.AdmitJoin(Table, caller);
        Assert.Throws<HubException>(() => throttle.AdmitJoin(Table, caller));

        clock.Advance(TimeSpan.FromSeconds(301));

        throttle.AdmitJoin(Table, caller);
    }

    private static GameCommand TextCommand() => new ForceAdvanceCommand { Reason = "推进一格" };

    private static ActionThrottle Create(ActionThrottleOptions options, MutableClock clock) =>
        new(Options.Create(options), clock, NullLogger<ActionThrottle>.Instance);

    /// <summary>可控时钟：窗口过期当场可判，不用真的等 5 分钟。</summary>
    private sealed class MutableClock : IClock
    {
        private DateTimeOffset _now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _now;

        internal void Advance(TimeSpan delta) => _now += delta;
    }
}
