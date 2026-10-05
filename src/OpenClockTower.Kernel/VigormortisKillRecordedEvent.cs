namespace OpenClockTower.Kernel;

/// <summary>
/// 亡骨魔成功杀死一名爪牙：记下「保留能力」事实与说书人选择的中毒侧。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《亡骨魔》· 2026-10-01 抓取 · 规则细节 19（放置条件「被亡骨魔成功杀死的玩家角色如果是
/// 爪牙角色」）与 22（中毒标记放置在「与此次放置了『保留能力』标记的爪牙玩家邻近的镇民玩家之一」）。
/// 折叠成 <see cref="GameState.VigormortisKills"/>：两条持续型效果（保留能力窗口 / 邻近镇民中毒）
/// 都由常驻来源按它派生，死亡也不再终止被保有能力名下的效果
/// （口径见 <c>docs/standard/rulings.md</c> R-0056）。
/// </para>
/// <para>
/// **只在击杀真的落地时记**：麻脸巫婆之夜的待定死亡被说书人阻止时，这条事实不产生
/// （载荷随待定死亡走，见 <see cref="DeferredRetention"/>）。
/// </para>
/// </remarks>
public sealed record VigormortisKillRecordedEvent : GameEvent
{
    /// <summary>发起击杀的亡骨魔席位。</summary>
    public required SeatId Demon { get; init; }

    /// <summary>被杀死并保留能力的爪牙席位。</summary>
    public required SeatId Minion { get; init; }

    /// <summary>说书人选择的中毒侧；null = 场上没有镇民可中毒。</summary>
    public SeatRingDirection? Side { get; init; }
}
