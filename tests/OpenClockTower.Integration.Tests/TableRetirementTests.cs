using Microsoft.Extensions.Options;
using OpenClockTower.Application;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 空闲桌的保留判定（M5 / G-A5-5 容量半边 · G-A6-5）：一张决策表，逐档判。
/// </summary>
/// <remarks>
/// <para>
/// 判定是**纯计算**（看事实与阈值，不碰库、不碰连接），所以这一整族用几十毫秒跑完，
/// 而真正删库那一半交给真宿主用例（<see cref="TableRetirementHostTests"/>）与真机读数。
/// 拆开的理由与 <c>SchemaComparer</c> 同一套：能纯算的就别拿去压真库。
/// </para>
/// <para>
/// 反方向同样重要：**不该删的一张都不能删**——回收是不可逆动作，
/// "少删一张"的代价是占块磁盘，"多删一张"的代价是一桌人的对局没了。
/// </para>
/// </remarks>
public sealed class TableRetirementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>阈值：未开局 24 小时、已开局 90 天（产品默认值）。</summary>
    private static TableRetirementPolicy Policy(int emptyHours = 24, int playedDays = 90) =>
        new(Options.Create(new TableRetentionOptions
        {
            EmptyTableHours = emptyHours,
            PlayedTableDays = playedDays,
        }));

    private static TableActivity Activity(
        DateTimeOffset? createdAt = null,
        int events = 0,
        DateTimeOffset? lastEvent = null,
        DateTimeOffset? lastBinding = null,
        string name = "某一桌") =>
        new(new GameId("t1"), name, createdAt, events, lastEvent, lastBinding, 0);

    /// <summary>从未开局的空桌：过了保留期就该回收（它只是个占着额度的壳）。</summary>
    [Fact]
    public void EmptyTable_PastRetention_IsRetired()
    {
        var outcome = Policy().Decide(Activity(createdAt: Now.AddHours(-25)), Now, occupied: false);

        Assert.Equal(TableRetirementVerdict.Retire, outcome.Verdict);
        Assert.Equal(TimeSpan.FromHours(25), outcome.IdleFor);
        Assert.Contains("未开局", outcome.Reason, StringComparison.Ordinal);
    }

    /// <summary>反方向：刚到保留期不算过期，一分钟都不提前。</summary>
    [Fact]
    public void EmptyTable_WithinRetention_IsKept()
    {
        var outcome = Policy().Decide(Activity(createdAt: Now.AddHours(-23)), Now, occupied: false);

        Assert.Equal(TableRetirementVerdict.WithinRetention, outcome.Verdict);
        Assert.Contains("23 小时", outcome.Reason, StringComparison.Ordinal);
    }

    /// <summary>开过局的桌走**另一档**（天级）：别人的对局记录不能按小时算。</summary>
    [Fact]
    public void PlayedTable_UsesTheDayThreshold()
    {
        var recent = Policy().Decide(
            Activity(createdAt: Now.AddDays(-200), events: 40, lastEvent: Now.AddDays(-30)),
            Now,
            occupied: false);
        Assert.Equal(TableRetirementVerdict.WithinRetention, recent.Verdict);
        Assert.Contains("已开局", recent.Reason, StringComparison.Ordinal);

        var stale = Policy().Decide(
            Activity(createdAt: Now.AddDays(-200), events: 40, lastEvent: Now.AddDays(-91)),
            Now,
            occupied: false);
        Assert.Equal(TableRetirementVerdict.Retire, stale.Verdict);
    }

    /// <summary>
    /// 活跃度取**三者最大**：建桌很久但刚有人认领席位 = 还在用，一张都不该删。
    /// </summary>
    /// <remarks>
    /// 这条是"三份时刻取最大"这个写法的判据：任何一处写反（取最小 / 只看建桌时刻），
    /// 一张两年前开、今天还在玩的桌就会被判成到期。
    /// </remarks>
    [Fact]
    public void LastActivity_IsTheLatestOfTheThreeFacts()
    {
        var outcome = Policy().Decide(
            Activity(createdAt: Now.AddDays(-700), events: 12, lastEvent: Now.AddDays(-400), lastBinding: Now.AddMinutes(-3)),
            Now,
            occupied: false);

        Assert.Equal(TableRetirementVerdict.WithinRetention, outcome.Verdict);
        Assert.Equal(TimeSpan.FromMinutes(3), outcome.IdleFor);
    }

    /// <summary>有在线连接时**先看这一条**：哪怕空闲时长远超保留期也不动。</summary>
    [Fact]
    public void OccupiedTable_IsNeverRetired()
    {
        var outcome = Policy().Decide(
            Activity(createdAt: Now.AddDays(-900), events: 3, lastEvent: Now.AddDays(-800)),
            Now,
            occupied: true);

        Assert.Equal(TableRetirementVerdict.InUse, outcome.Verdict);
        Assert.Null(outcome.IdleFor);
        Assert.Contains("在线连接", outcome.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// 三个时刻全空 = **无法判定**（老库里的空桌），不删。
    /// </summary>
    /// <remarks>
    /// 这条是"没有依据就不删"的判据。v2 迁移之前建的桌没有建桌时刻，从未开局的桌也没有事件——
    /// 把"不知道"当成"很久没动"会删掉运维刚手工导进来、还没来得及用的桌。
    /// </remarks>
    [Fact]
    public void TableWithoutAnyTimestamp_IsUndecidable_NotRetired()
    {
        var outcome = Policy().Decide(Activity(), Now, occupied: false);

        Assert.Equal(TableRetirementVerdict.Undecidable, outcome.Verdict);
        Assert.Null(outcome.IdleFor);
        Assert.Contains("没有依据", outcome.Reason, StringComparison.Ordinal);
    }

    /// <summary>时钟回拨（NTP 校时 / 有人改了库里的时刻）按"刚刚活跃"处理，不按负数算。</summary>
    [Fact]
    public void FutureTimestamp_IsTreatedAsJustActive()
    {
        var outcome = Policy().Decide(Activity(createdAt: Now.AddHours(5)), Now, occupied: false);

        Assert.Equal(TableRetirementVerdict.WithinRetention, outcome.Verdict);
        Assert.Equal(TimeSpan.Zero, outcome.IdleFor);
    }

    /// <summary>阈值为 0 = 立即到期（演练与真机取证靠它验证回收链路真的会删）。</summary>
    [Fact]
    public void ZeroThreshold_RetiresEverythingWithABasis()
    {
        var policy = Policy(emptyHours: 0, playedDays: 0);

        Assert.Equal(
            TableRetirementVerdict.Retire,
            policy.Decide(Activity(createdAt: Now), Now, occupied: false).Verdict);
        Assert.Equal(
            TableRetirementVerdict.Retire,
            policy.Decide(Activity(createdAt: Now, events: 1, lastEvent: Now), Now, occupied: false).Verdict);
        Assert.Equal(
            TableRetirementVerdict.Undecidable,
            policy.Decide(Activity(), Now, occupied: false).Verdict);
    }
}
