using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 首版可用的角色契约目录：按角色 slug 检索，未实现返回 null。
/// </summary>
/// <remarks>
/// <para>
/// 同一个角色对象同时实现提示契约（<see cref="INightAction"/>，建表用）与结算契约
/// （<see cref="IAbilityResolution"/>，结算用）——两个目录指向同一批实现，避免"建表能开夜、
/// 结算找不到人"的分叉。
/// </para>
/// <para>
/// 契约随角色分批实现（<c>docs/backlog/in-progress/settlement-engine.md</c>：25 个角色的逐角色实现另立票）。
/// 目录不依赖枚举顺序（D-0008），只做按键检索。
/// </para>
/// </remarks>
public static class NightActions
{
    private static readonly Catalog Registry = new();

    /// <summary>提示契约目录（建表用）。</summary>
    public static INightActionCatalog Default => Registry;

    /// <summary>结算契约目录（结算用）。</summary>
    public static IAbilityResolutionCatalog Resolutions => Registry;

    /// <summary>常驻效果来源（提交前对账用）：目前是诺-达鲺的中毒。</summary>
    public static IReadOnlyList<IStandingEffectSource> StandingEffects { get; } =
        [new NoDashiiPoisonSource()];

    private sealed class Catalog : INightActionCatalog, IAbilityResolutionCatalog
    {
        private static readonly Dictionary<CharacterId, INightAction> ByCharacter = new()
        {
            [new CharacterId("clockmaker")] = new ClockmakerNightAction(),
            [new CharacterId("dreamer")] = new DreamerNightAction(),
            [new CharacterId("no-dashii")] = new NoDashiiNightAction(),
            [new CharacterId("vortox")] = new VortoxNightAction(),
            [new CharacterId("witch")] = new WitchNightAction(),
            [new CharacterId("cerenovus")] = new CerenovusNightAction(),
            [new CharacterId("evil-twin")] = new EvilTwinNightAction(),
            [new CharacterId("pit-hag")] = new PitHagNightAction(),
            [new CharacterId("snake-charmer")] = new SnakeCharmerNightAction(),
        };

        public INightAction? Find(CharacterId character) =>
            ByCharacter.TryGetValue(character, out var action) ? action : null;

        IAbilityResolution? IAbilityResolutionCatalog.Find(CharacterId character) =>
            ByCharacter.TryGetValue(character, out var action) && action is IAbilityResolution resolution
                ? resolution
                : null;
    }
}
