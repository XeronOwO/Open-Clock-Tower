namespace OpenClockTower.Kernel;

/// <summary>
/// 游戏阶段：步骤机在哪个大阶段上推进。
/// </summary>
/// <remarks>
/// 依据 <c>docs/architecture/current.md</c> §2.7：首夜 / 其他夜晚 / 白天 / 结算中。
/// </remarks>
public enum GamePhase
{
    /// <summary>首夜。</summary>
    FirstNight,

    /// <summary>首夜之后的夜晚。</summary>
    OtherNight,

    /// <summary>白天。</summary>
    Day,

    /// <summary>结算中。</summary>
    Resolving,
}
