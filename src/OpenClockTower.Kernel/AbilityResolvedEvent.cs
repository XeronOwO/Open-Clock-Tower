namespace OpenClockTower.Kernel;

/// <summary>
/// 一次角色能力结算的结论：**用过没有、生效没有**——两件事分开记录（架构 §2.2）。
/// </summary>
/// <remarks>
/// 依据：百科《重要细节》三-3——醉酒或中毒的玩家会失去能力，且「每局游戏限一次」的能力
/// 在醉酒 / 中毒期间被使用即被浪费。因此「是否使用过」与「是否生效过」必须是两个独立的布尔量；
/// 这条事件同时喂给状态账里的两本账（<see cref="GameState.AbilityUses"/> 与
/// <see cref="GameState.Malfunctions"/>），不另起状态仓（D-0015）。
/// </remarks>
public sealed record AbilityResolvedEvent : GameEvent
{
    /// <summary>被结算的槽位；可回溯到是哪一步。</summary>
    public required StepSlotId SlotId { get; init; }

    /// <summary>实施这条能力的席位。</summary>
    public required SeatId Actor { get; init; }

    /// <summary>被使用的能力。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>这次使用是否正常生效。</summary>
    public required bool Effective { get; init; }

    /// <summary>未正常生效时的原因分类（R-0004）；生效时为 null。</summary>
    public MalfunctionKind? Malfunction { get; init; }

    /// <summary>说明：分类无法表达的组合（如「同时中毒且醉酒」）写在这里，不许丢。</summary>
    public string? Note { get; init; }
}
