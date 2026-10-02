using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 哲学家（<c>philosopher</c>）的标识、选项与账本查询：每局限一次获得一名镇民 / 外来者的能力，
/// 被选角色在场时其持有者醉酒。
/// </summary>
/// <remarks>
/// 来源：百科《哲学家》· 2026-10-01 抓取 · 角色能力 / 角色简介 / 运作方式 / 提示标记；
/// 平台口径（获得能力的落格、醉酒范围、死亡触发型能力不在本票）见
/// <c>docs/standard/rulings.md</c> R-0036。
/// </remarks>
internal static class PhilosopherAbility
{
    /// <summary>角色标识。</summary>
    internal static readonly CharacterId Character = new("philosopher");

    /// <summary>「获得能力」事实（常驻标记效果）的能力标识：每局限一次，用过即废。</summary>
    internal static readonly AbilityId GrantAbility = new("philosopher.grant");

    /// <summary>常驻醉酒（被选角色的持有者）的能力标识：由常驻来源动态重算。</summary>
    internal static readonly AbilityId DrunkAbility = new("philosopher.grant.drunk");

    /// <summary>摇头不使用能力（百科《哲学家》· 运作方式 11：他要么摇头，要么指向一个角色图标）。</summary>
    internal const string Decline = "decline";

    /// <summary>可选角色：全部镇民与外来者，去掉哲学家自己（「获得**其他**角色的能力」）。</summary>
    internal static IReadOnlyList<CharacterId> SelectableCharacters() =>
    [
        .. SectsAndVioletsRoster.OfType(CharacterType.Townsfolk).Where(character => character != Character),
        .. SectsAndVioletsRoster.OfType(CharacterType.Outsider).Where(character => character != Character),
    ];

    /// <summary>该角色的中文名（选项预览用）；不在花名册里时回显 slug，不吞。</summary>
    internal static string DisplayNameOf(CharacterId character) =>
        SectsAndVioletsRoster.DisplayNameOf(character) ?? character.Value;

    /// <summary>账上那条「获得能力」事实（未终止的常驻效果）；null = 还没获得过，或它已经终止。</summary>
    internal static PersistentEffect? FindGrant(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.PersistentEffects.FirstOrDefault(effect =>
            effect.Ability == GrantAbility && !effect.IsTerminated);
    }

    /// <summary>被获得的角色；账上没有这条事实、或载荷缺失时为 null。</summary>
    internal static CharacterId? GrantedCharacterOf(GameState state) => FindGrant(state)?.GrantedCharacter;

    /// <summary>某个角色此刻的全部持有者（含已死亡——已死玩家的角色标记仍留在魔典上）。</summary>
    internal static IReadOnlyList<SeatStateEntry> HoldersOf(GameState state, CharacterId character)
    {
        ArgumentNullException.ThrowIfNull(state);
        return [.. state.Seats.Where(entry => entry.CharacterValue == character)];
    }
}
