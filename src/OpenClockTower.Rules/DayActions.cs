using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 白天相关角色的契约目录：登记"哪些角色与白天阶段相关"，以及它们的白天能力是否已实现。
/// </summary>
/// <remarks>
/// <para>
/// 与夜晚的 <see cref="NightActions"/> 同族：未实现契约的角色在开白天时会被**显式拒绝**
/// （<c>legality.day_contract_missing</c>），不允许"白天照跑、能力静默不发生"。
/// </para>
/// <para>
/// 名单来源：百科各角色页的「规则细节」与《规则概要》三 · 2026-10-01 抓取，逐条见
/// <c>docs/backlog/done/day-phase.md</c> 的机制清点表（卖花女孩 / 城镇公告员只读取
/// 白天事实、自身仍是夜晚能力，不在本闸名单里）。
/// </para>
/// <para>
/// 本票没有已实现的白天契约：<see cref="IsCovered"/> 恒为 false。角色分批实现时在这里登记，
/// 并补上对应的运行时证据。
/// </para>
/// </remarks>
public static class DayActions
{
    private static readonly CharacterId[] DayRelevantCharacters =
    [
        // 白天行动 / 白天信息：博学者、艺术家、杂耍艺人
        new("savant"),
        new("artist"),
        new("juggler"),

        // 处决相关的触发：畸形秀演员、洗脑师（惩罚处决）、女巫（提名即死）
        new("mutant"),
        new("cerenovus"),
        new("witch"),

        // 死亡触发（含被处决）：心上人、理发师、呆瓜
        new("sweetheart"),
        new("barber"),
        new("klutz"),

        // 处决 / 无人被处决直接决定胜负：镜像双子、涡流
        new("evil-twin"),
        new("vortox"),
    ];

    /// <summary>该角色是否与白天阶段相关（无论实现与否）。</summary>
    public static bool IsDayRelevant(CharacterId character) =>
        DayRelevantCharacters.Contains(character);

    /// <summary>该角色的白天契约是否已实现；本票为空，全部未实现。</summary>
    public static bool IsCovered(CharacterId character) => false;
}
