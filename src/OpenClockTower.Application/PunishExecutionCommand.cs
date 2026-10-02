using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人处罚处决：洗脑师 / 畸形秀演员的"疯狂"后果，绕过提名流程主动处决一名席位。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0020：白天形态占用当天处决上限并立即收口白天；
/// 夜晚形态不占任何白天的上限、不推进阶段。依据是否成立由内核的处罚依据契约判定
/// （<see cref="IAdjudicatedExecutionSource"/>），命令面只做身份 / 阶段 / 席位三道形状检查。
/// </remarks>
public sealed record PunishExecutionCommand : GameCommand
{
    /// <summary>被处罚处决的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>处罚来源：洗脑师的疯狂要求，或畸形秀演员自己的能力。</summary>
    public required MadnessPunishmentSource Source { get; init; }

    /// <summary>说书人的执行说明（可选，进审计与处决事件）。</summary>
    public string? Note { get; init; }
}
