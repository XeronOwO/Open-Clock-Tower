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
/// 已实现的白天契约登记在 <see cref="CoveredCharacters"/>：女巫的诅咒在夜晚施加、在下个白天触发
/// （提名即死），触发与存续两族契约见 <see cref="RoleContracts"/>。角色分批实现时在这里登记，
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

    /// <summary>
    /// 白天契约**已经实现**的角色（分批登记）：
    /// 女巫（夜晚诅咒 → 下个白天提名即死）；
    /// 洗脑师（夜晚签发疯狂要求 → 处罚处决，R-0020 / R-0021）；
    /// 畸形秀演员（说书人主动处罚处决，R-0020）；
    /// 呆瓜（死亡公告后公开选择，R-0027）、镜像双子（首夜配对 + 善良方被处决即邪恶获胜，R-0025）、
    /// 涡流（黄昏无人被处决即邪恶获胜，R-0026）；
    /// 理发师（死亡触发立即记账、与恶魔的交互等到当夜理发师格，R-0033）。
    /// </summary>
    private static readonly CharacterId[] CoveredCharacters =
    [
        // 女巫：夜晚选择目标施加「被诅咒」，被诅咒者下个白天发起提名即死（提名仍生效）；
        // 诅咒的触发与存续见 RoleContracts。
        new("witch"),

        // 洗脑师：夜晚选择玩家与善良角色（两维选择，R-0021），处罚处决走 AdjudicatedExecutionMachine。
        new("cerenovus"),

        // 畸形秀演员：没有夜晚行动、没有提示标记，处罚处决走同一条命令面（R-0020）。
        new("mutant"),

        // 呆瓜：死亡公告后开出一条"公开选择一名存活玩家"的触发型请求（R-0027）。
        new("klutz"),

        // 镜像双子：首夜配对 + 互认（NightActions），胜负条件读配对效果（R-0025）。
        new("evil-twin"),

        // 涡流：其他夜击杀（NightActions）+ 黄昏无人被处决即邪恶获胜（R-0026）。
        new("vortox"),

        // 理发师：白天处决 / 夜晚被杀都在死亡那一批立即记「今晚理发」，与恶魔的交互等到当夜
        // 理发师格（触发格 + BarberNightTrigger，R-0033）——白天本身不发生交互，但必须登记覆盖，
        // 否则开白天会被 legality.day_contract_missing 拒绝。
        new("barber"),

        // 心上人：任何死因（含白天处决）都在死亡批内立即开触发型裁定 + 施加持续醉酒（R-0039）；
        // 白天死亡同样要立刻处理，因此必须登记覆盖。
        new("sweetheart"),
    ];

    /// <summary>该角色是否与白天阶段相关（无论实现与否）。</summary>
    public static bool IsDayRelevant(CharacterId character) =>
        DayRelevantCharacters.Contains(character);

    /// <summary>该角色的白天契约是否已实现；未实现的角色在场时开白天会被显式拒绝。</summary>
    public static bool IsCovered(CharacterId character) => CoveredCharacters.Contains(character);
}
