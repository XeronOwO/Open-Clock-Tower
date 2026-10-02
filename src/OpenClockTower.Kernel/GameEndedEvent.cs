namespace OpenClockTower.Kernel;

/// <summary>
/// 游戏结束：某阵营达成胜利条件，本局立即结束。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《规则概要》四 · 2026-10-01 抓取（达成条件即宣告结束）；
/// 判定时机与优先级见 <c>docs/standard/rulings.md</c> R-0024——一次原子提交的业务事件折完后先判一次，
/// 未结束才跑事件触发 / 常驻对账，随后再判一次；结论只产出一条，此后一切命令被拒。
/// </para>
/// <para>
/// 事件只描述"发生了什么"：谁获胜、因为哪条条件、说明；不携带墙上时间（D-0008）。
/// </para>
/// </remarks>
public sealed record GameEndedEvent : GameEvent
{
    /// <summary>获胜阵营。</summary>
    public required Alignment Winner { get; init; }

    /// <summary>触发的条件分类。</summary>
    public required OutcomeCondition Condition { get; init; }

    /// <summary>人类可读说明（结束横幅与审计用）。</summary>
    public required string Detail { get; init; }
}
