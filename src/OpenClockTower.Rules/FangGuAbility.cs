using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>方古（<c>fang-gu</c>）的标识与文案：恶魔，除首夜外每夜击杀，首次成功杀死外来者改为侵染。</summary>
/// <remarks>
/// 来源：百科《方古》· 2026-10-01 抓取 · 角色能力 / 运作方式 / 提示标记「限一次」。
/// </remarks>
internal static class FangGuAbility
{
    /// <summary>角色标识。</summary>
    internal static readonly CharacterId Character = new("fang-gu");

    /// <summary>夜间击杀的能力标识（效果与状态变化的归因）。</summary>
    internal static readonly AbilityId KillAbility = new("fang-gu");

    /// <summary>侵染说明（进被侵染席位的角色 / 阵营变化事实与审计）。</summary>
    internal static string InfectionReason(SeatId source, SeatId target) =>
        $"方古侵染：{source.Value} 号方古首次成功攻击外来者（{target.Value} 号），"
        + "外来者变成新的邪恶方古、原方古死亡（限一次）";

    /// <summary>原方古死亡的说明（同一次侵染的另一半事实）。</summary>
    internal static string SourceDeathReason(SeatId source) =>
        $"方古侵染：原方古（{source.Value} 号）死亡，新方古接管恶魔角色（限一次）";
}
