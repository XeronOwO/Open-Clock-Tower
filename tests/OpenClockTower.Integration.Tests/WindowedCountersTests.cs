using Microsoft.Extensions.Logging.Abstractions;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 有界窗口计数器的口径（M4 / D-0033）：窗口过期、键独立、成功清零、内存有界。
/// </summary>
/// <remarks>
/// 账号入口限速与动作准入共用这一个零件（<see cref="AccountAttemptLimiter"/> / <see cref="ActionThrottle"/>），
/// 所以这里判的是两条链路共同的地基：窗口语义错一处，两处限速一起错。
/// </remarks>
public sealed class WindowedCountersTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CountsWithinTheWindow_AndRestartsAfterIt()
    {
        var counters = Create(TimeSpan.FromSeconds(60), maxTrackedKeys: 100);

        counters.Increment("a", Start);
        counters.Increment("a", Start.AddSeconds(10));

        Assert.Equal(2, counters.Count("a", Start.AddSeconds(20)));
        Assert.Equal(0, counters.Count("b", Start.AddSeconds(20)));
        Assert.InRange(counters.RemainingSeconds("a", Start.AddSeconds(20)), 39, 41);

        // 窗口一过：计数归零，剩余时间归零。
        Assert.Equal(0, counters.Count("a", Start.AddSeconds(61)));
        Assert.Equal(0, counters.RemainingSeconds("a", Start.AddSeconds(61)));
    }

    [Fact]
    public void KeysAreIndependent_AndResetForgetsThem()
    {
        var counters = Create(TimeSpan.FromSeconds(60), maxTrackedKeys: 100);

        counters.Increment("a", Start);
        counters.Increment("b", Start);
        counters.Reset("a");

        Assert.Equal(0, counters.Count("a", Start));
        Assert.Equal(1, counters.Count("b", Start));
    }

    [Fact]
    public void AtTheCap_ExpiredKeysArePruned_SoNewKeysStillGetTheirOwnAccount()
    {
        var counters = Create(TimeSpan.FromSeconds(60), maxTrackedKeys: 3);

        counters.Increment("a", Start);
        counters.Increment("b", Start);
        counters.Increment("c", Start);

        // 到顶：先清过期键（这里都还没过期）⇒ 新键不单独计数，既有键照常。
        counters.Increment("d", Start);
        Assert.Equal(3, counters.TrackedKeys);
        Assert.Equal(0, counters.Count("d", Start));

        // 窗口过后再记：过期键被清掉，新键拿得到自己的账，键数不会一路涨。
        counters.Increment("e", Start.AddSeconds(120));
        Assert.Equal(1, counters.Count("e", Start.AddSeconds(120)));
        Assert.Equal(1, counters.TrackedKeys);
    }

    private static WindowedCounters Create(TimeSpan window, int maxTrackedKeys) =>
        new(window, maxTrackedKeys, NullLogger.Instance);
}
