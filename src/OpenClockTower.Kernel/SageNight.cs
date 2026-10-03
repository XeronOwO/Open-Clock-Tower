namespace OpenClockTower.Kernel;

/// <summary>
/// 贤者「被恶魔杀死」的待展示事实：死亡时记下击杀者与生效判定，当夜贤者触发格开说书人裁定。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《贤者》· 2026-10-01 抓取 · 角色能力——「如果恶魔杀死了你，在**当晚**你会被唤醒并
/// 得知两名玩家，其中一名是杀死你的那个恶魔」；《死亡触发能力》· 能力简介——死亡时立即触发、
/// 涉及交互的效果等到夜晚。
/// </para>
/// <para>
/// 平台口径（见 <c>docs/standard/rulings.md</c> R-0038）：死亡批立即记事实（含击杀者与
/// 死亡时点的生效判定），当夜 sage 触发格由触发管线开裁定点；夜晚计划走完仍未消费时
/// 由推进路径**显式清空**并记「过时不候」。事实只属于当夜——阶段边界上仍挂着即视为
/// 事件流收口缺失（显式失败，不顺延）。
/// </para>
/// </remarks>
public sealed record SageNight
{
    /// <summary>以贤者身份死亡的席位（信息收件人）。</summary>
    public required SeatId Sage { get; init; }

    /// <summary>杀死他的恶魔席位（死亡时刻的角色归因）。</summary>
    public required SeatId Demon { get; init; }

    /// <summary>击杀者在死亡时刻的角色（用于提示与审计）。</summary>
    public required CharacterId DemonCharacter { get; init; }

    /// <summary>
    /// 死亡时贤者的能力是否生效：true / false；null = 醉酒 / 中毒维度未观测，判不了（不猜）。
    /// </summary>
    public required bool? Effective { get; init; }

    /// <summary>记账说明（谁被谁杀死、死亡时点的判定），进审计与说书人视图。</summary>
    public required string Note { get; init; }
}
