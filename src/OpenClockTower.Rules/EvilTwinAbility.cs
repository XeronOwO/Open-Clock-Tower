using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 镜像双子的能力常量与标识构造：首夜说书人配对 + 双向互认。
/// </summary>
/// <remarks>
/// 来源：百科《镜像双子》· 2026-10-01 抓取 · 运作方式 / 提示标记——「由说书人来选择镜像双子与一名
/// 对立阵营玩家配对」「在首个夜晚，同时唤醒两名双子……互相得知对方的角色」；
/// 配对事实的建模与胜负口径见 <c>docs/standard/rulings.md</c> R-0025。
/// </remarks>
internal static class EvilTwinAbility
{
    /// <summary>镜像双子角色标识。</summary>
    internal static readonly CharacterId Character = new("evil-twin");

    /// <summary>配对标记的能力标识（一条 `Dimension = null` 的持续型效果，R-0025 第 1 条）。</summary>
    internal static readonly AbilityId PairAbility = new("evil-twin.pair");

    /// <summary>配对效果的稳定标识：`{PlanLabel}:{SlotId}:pair`（重放与终止按它认人）。</summary>
    internal static EffectId PairEffectId(string planLabel, StepSlotId slotId) =>
        new($"{planLabel}:{slotId}:pair");
}
