using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 洗脑师能力的共同口径：能力标识、要求标识与到期日换算、可选角色集合。
/// </summary>
/// <remarks>
/// <para>
/// 提示（夜晚选择）、结算（写入要求与告知目标）、处罚依据（要求是否仍生效）三处必须用**同一份**口径，
/// 否则会出现"提示说能选、结算找不到角色"这类分叉（同 <see cref="WitchAbility"/> 的教训）。
/// </para>
/// <para>
/// 来源：百科《洗脑师》· 2026-10-01 抓取 · 角色能力——「每个夜晚，你要选择一名玩家和一个善良角色。
/// 他明天白天和夜晚需要"疯狂"地证明自己是这个角色，不然他可能被处决。」；· 运作方式——目标集合与
/// 告知方式；· 提示标记——「若此时洗脑师醉酒中毒，不放置该标记」与移除时机。
/// </para>
/// </remarks>
internal static class CerenovusAbility
{
    /// <summary>夜间行动的能力标识（进两本账）。</summary>
    internal static readonly AbilityId ActionAbility = new("cerenovus");

    /// <summary>疯狂要求的能力标识：与夜间行动分开记（同女巫的击杀 / 诅咒）。</summary>
    internal static readonly AbilityId MadnessAbility = new("cerenovus.madness");

    /// <summary>洗脑师的角色标识。</summary>
    internal static readonly CharacterId Cerenovus = new("cerenovus");

    /// <summary>
    /// 要求标识：槽位稳定键（<c>sv:night-2:cerenovus</c>，重进的遍次带 <c>#N</c>）加后缀。
    /// 每次进入唯一、重放稳定，撤下事件与说书人视图按它认人（R-0021 / R-0052 第 2 条）。
    /// </summary>
    internal static MadnessRequirementId RequirementId(string slotKey) =>
        new($"{slotKey}:madness");

    /// <summary>死亡事实的原因（机器可读前缀 + 人可读说明）；具体要证明的角色由处罚依据补齐。</summary>
    internal const string PunishmentDeathReason = "cerenovus.madness：目标未按洗脑师的要求疯狂（R-0020）";

    /// <summary>
    /// 到期日：施加夜的**次日白天与其后夜晚**有效，在下一个黎明撤下（R-0021）。
    /// 施加夜为第 N 夜时，白天账里已开始 N−1 天（夜晚 N 之后是第 N 天），
    /// 因此到期日 = 已开始天数 + 2。
    /// </summary>
    internal static int ExpiresAtDay(int daysStarted) => daysStarted + 2;

    /// <summary>可选角色：镇民与外来者（百科《洗脑师》：「一个镇民或外来者角色」）。</summary>
    internal static IReadOnlyList<CharacterId> SelectableCharacters() =>
    [
        .. SectsAndVioletsRoster.OfType(CharacterType.Townsfolk),
        .. SectsAndVioletsRoster.OfType(CharacterType.Outsider),
    ];

    /// <summary>角色的展示名（要求内容与目标告知都是人可读文本）。</summary>
    internal static string DisplayNameOf(CharacterId character) =>
        SectsAndVioletsRoster.DisplayNameOf(character) ?? character.Value;
}
