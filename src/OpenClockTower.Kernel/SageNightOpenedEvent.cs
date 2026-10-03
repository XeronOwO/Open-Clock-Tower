namespace OpenClockTower.Kernel;

/// <summary>
/// 贤者「被恶魔杀死」事实开启：死亡事件落账时立即记下，当夜由贤者触发格开说书人展示交互。
/// </summary>
/// <remarks>
/// 由规则层的死亡触发器在死亡的**同一批**产出；平台口径见 <c>docs/standard/rulings.md</c> R-0038。
/// </remarks>
public sealed record SageNightOpenedEvent : GameEvent
{
    /// <summary>以贤者身份死亡的席位。</summary>
    public required SeatId Sage { get; init; }

    /// <summary>杀死他的恶魔席位。</summary>
    public required SeatId Demon { get; init; }

    /// <summary>击杀者在死亡时刻的角色。</summary>
    public required CharacterId DemonCharacter { get; init; }

    /// <summary>死亡时能力是否生效；null = 醉酒 / 中毒维度未观测，判不了（不猜）。</summary>
    public required bool? Effective { get; init; }

    /// <summary>开启说明（死亡时点的判定依据），进审计与说书人视图。</summary>
    public required string Note { get; init; }
}
