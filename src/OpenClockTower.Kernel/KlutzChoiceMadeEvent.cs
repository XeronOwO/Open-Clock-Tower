namespace OpenClockTower.Kernel;

/// <summary>
/// 呆瓜的死亡选择：得知自己死亡后公开选择一名存活玩家（R-0027）。
/// </summary>
/// <remarks>
/// 依据：百科《呆瓜》· 2026-10-01 抓取 · 角色能力——「当你得知你死亡时，你要公开选择一名存活的玩家：
/// 如果他是邪恶的，你的阵营落败」。本事件只记录"选了什么"这一事实；
/// 胜负后果由 <see cref="OutcomeEvaluator"/> 在**同一次提交的求值**里判定（R-0024）。
/// </remarks>
public sealed record KlutzChoiceMadeEvent : GameEvent
{
    /// <summary>做出选择的呆瓜席位。</summary>
    public required SeatId Klutz { get; init; }

    /// <summary>被选中的席位。</summary>
    public required SeatId Target { get; init; }
}
