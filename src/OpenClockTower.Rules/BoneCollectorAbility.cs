using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 集骨者（<c>bone-collector</c>）的标识、选项与规则口径：每局限一次，在夜晚选择一名**死亡**玩家，
/// 让他重新获得角色能力直到下个黄昏。
/// </summary>
/// <remarks>
/// 来源：百科《集骨者》· 2026-10-04 抓取 · 角色能力 / 角色简介 / 运作方式 / 提示标记 / 规则细节；
/// 平台口径（目标集合、摇头、未生效、窗口存续与终止、死者槽位激活、上限叠加）见
/// <c>docs/standard/rulings.md</c> R-0054。
/// </remarks>
internal static class BoneCollectorAbility
{
    /// <summary>角色标识。</summary>
    internal static readonly CharacterId Character = new("bone-collector");

    /// <summary>「重获能力」的能力标识：每局限一次，用后即失去自身能力。</summary>
    internal static readonly AbilityId RegainAbility = new("bone-collector.regain");

    /// <summary>摇头不使用能力（百科《集骨者》· 运作方式：要么摇头，要么指向一名已死亡的玩家）。</summary>
    internal const string Decline = "decline";

    /// <summary>这条持续型效果是不是集骨者的「重获能力」窗口（收口触发器按它挑选要终止的效果）。</summary>
    internal static bool IsRegainEffect(PersistentEffect effect) =>
        effect.Ability == RegainAbility
        && effect.Window == EffectWindowKind.RegainedAbility;
}
