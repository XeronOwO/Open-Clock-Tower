using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>贤者能力的常量与文案（死亡触发：被恶魔杀死后当晚得知两名玩家）。</summary>
/// <remarks>
/// 来源：百科《贤者》· 2026-10-01 抓取 · 角色能力 / 角色简介 / 运作方式 / 范例；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0038。
/// </remarks>
internal static class SageAbility
{
    /// <summary>角色标识。</summary>
    internal static readonly CharacterId Character = new("sage");

    /// <summary>能力标识（信息结果 / 裁定点归因）。</summary>
    internal static readonly AbilityId InfoAbility = new("sage");

    /// <summary>死亡时能力是否生效的说明（裁定点提示与信息注记共用）。</summary>
    internal static string EffectivenessNote(bool? effective) => effective switch
    {
        true => "死亡时贤者清醒健康：能力生效，展示的两名玩家中应包含击杀者",
        false => "死亡时贤者醉酒 / 中毒：能力未生效，展示内容可以是错的（百科《贤者》范例 2）",
        _ => "死亡时贤者的醉酒 / 中毒维度未观测：判不了是否生效，按「可能为假」处理（不猜，D-0015）",
    };

    /// <summary>把说书人选择的两名玩家翻译成信息内容（与筑梦师的组句同族，D-0002 说书人给出内容）。</summary>
    internal static string ComposeContent(SeatId first, SeatId second) =>
        $"你被唤醒并得知两名玩家：{first.Value} 号 与 {second.Value} 号（其中一名是杀死你的恶魔）。";
}
