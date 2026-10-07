using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 麻脸巫婆的角色标识、能力标识与共享口径。
/// </summary>
/// <remarks>
/// 来源：百科《麻脸巫婆》· 2026-10-01 抓取 · 角色信息 / 角色能力 / 角色简介 / 运作方式。
/// 相克条目（异端分子、村夫、落难少女、召唤师等）在首版纯 S&V 对局里不会触发：
/// 这些对手角色都来自其他剧本或实验性角色，`docs/standard/character-rules.md` 已登记。
/// </remarks>
internal static class PitHagAbility
{
    /// <summary>麻脸巫婆（爪牙）。</summary>
    internal static readonly CharacterId PitHag = new("pit-hag");

    /// <summary>「变成所选角色」这条能力的标识（进两本账与效果归因）。</summary>
    internal static readonly AbilityId TransformAbility = new("pit-hag.transform");

    /// <summary>
    /// 说书人在窗口期内追加死亡的归因标识（内核不硬编码角色，这个标识由规则层在开窗时交给它）。
    /// </summary>
    internal static readonly AbilityId CasualtyAbility = new("pit-hag.casualty");

    /// <summary>
    /// 可选择的角色表 = **角色列表**上的全部角色（镇民 / 外来者 / 爪牙 / 恶魔，不含旅行者）。
    /// </summary>
    /// <remarks>
    /// 物理桌面上她看着**角色列表**选（百科《麻脸巫婆》· 2026-10-04 抓取 · 运作方式：「让她指向一名玩家和
    /// 角色列表上的一个角色图标」），列表本身不透露哪些角色在场——所以提示里**不标注在场与否**，
    /// 那会泄漏魔典上的信息（D-0012）。「已在场则无事发生」由结算判定，不由选项过滤。
    /// 旅行者在**旅行者列表**上、不在角色列表上，因此既不满足「不在场」的定义、也不能被她创造
    /// （百科《哪些是“可以但不建议”》· 2026-10-04 抓取 · 基础规则部分「旅行者的角色转换」；
    /// 《术语汇总》· 2026-10-04 抓取；R-0060）。
    /// </remarks>
    internal static IReadOnlyList<CharacterId> SelectableCharacters() => SectsAndVioletsRoster.CharacterList;

    /// <summary>角色的中文名（未知角色原样回显，不猜）。</summary>
    internal static string DisplayNameOf(CharacterId character) =>
        SectsAndVioletsRoster.DisplayNameOf(character) ?? character.Value;
}
