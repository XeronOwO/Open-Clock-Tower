namespace OpenClockTower.Kernel;

/// <summary>
/// 麻脸巫婆之夜的死亡裁量窗口关闭（越过最后一个能造成死亡的恶魔行动之后）。
/// </summary>
/// <remarks>
/// 关闭时仍未裁定的待定死亡按恶魔攻击的自然结果生效，并各产一条
/// <see cref="DeferredDeathResolvedEvent"/> 写明「未裁定 → 按默认结果生效」（R-0030 第 3 条）。
/// </remarks>
public sealed record PitHagNightClosedEvent : GameEvent
{
    /// <summary>收口说明（含未裁定条数，进审计与说书人视图）。</summary>
    public required string Note { get; init; }
}
