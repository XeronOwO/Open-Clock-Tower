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
/// 首版花名册 30 个角色已全部落地：本目录覆盖夜晚顺序表上**全部 21 个行动格**角色；
/// 另外 3 名死亡触发角色（理发师 / 心上人 / 贤者）走触发格与
/// <see cref="RoleContracts.EventTriggers"/>，不由行动槽位承载。覆盖判据由
/// <c>tests/OpenClockTower.Rules.Tests/CharacterContractCoverageTests.cs</c> 守住
/// （顺序表每个行动格都必须能取到两种契约）。
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

    /// <summary>说书人裁定类提示的实时重建来源（入槽时按当前账重算提示上下文）。</summary>
    public static ISlotPromptSource Prompts { get; } = new NightActionPromptSource(Registry);

    /// <summary>常驻效果来源（提交前对账用）：诺-达鲺的中毒、哲学家的醉酒、亡骨魔的保留能力。</summary>
    public static IReadOnlyList<IStandingEffectSource> StandingEffects { get; } =
        [new NoDashiiPoisonSource(), new PhilosopherDrunkSource(), new VigormortisRetentionSource()];

    private sealed class Catalog : INightActionCatalog, IAbilityResolutionCatalog
    {
        private static readonly Dictionary<CharacterId, INightAction> ByCharacter = new()
        {
            [new CharacterId("clockmaker")] = new ClockmakerNightAction(),
            [new CharacterId("dreamer")] = new DreamerNightAction(),
            [new CharacterId("flowergirl")] = new FlowergirlNightAction(),
            [new CharacterId("town-crier")] = new TownCrierNightAction(),
            [new CharacterId("oracle")] = new OracleNightAction(),
            [new CharacterId("mathematician")] = new MathematicianNightAction(),
            [new CharacterId("no-dashii")] = new NoDashiiNightAction(),
            [new CharacterId("vortox")] = new VortoxNightAction(),
            [new CharacterId("vigormortis")] = new VigormortisNightAction(),
            [new CharacterId("witch")] = new WitchNightAction(),
            [new CharacterId("cerenovus")] = new CerenovusNightAction(),
            [new CharacterId("evil-twin")] = new EvilTwinNightAction(),
            [new CharacterId("pit-hag")] = new PitHagNightAction(),
            [new CharacterId("snake-charmer")] = new SnakeCharmerNightAction(),
            [new CharacterId("fang-gu")] = new FangGuNightAction(),
            [new CharacterId("philosopher")] = new PhilosopherNightAction(),
            [new CharacterId("seamstress")] = new SeamstressNightAction(),
            [new CharacterId("juggler")] = new JugglerNightAction(),
            [new CharacterId("barista")] = new BaristaNightAction(),
            [new CharacterId("bone-collector")] = new BoneCollectorNightAction(),
            [new CharacterId("harlot")] = new HarlotNightAction(),
        };

        public INightAction? Find(CharacterId character) =>
            ByCharacter.TryGetValue(character, out var action) ? action : null;

        IAbilityResolution? IAbilityResolutionCatalog.Find(CharacterId character) =>
            ByCharacter.TryGetValue(character, out var action) && action is IAbilityResolution resolution
                ? resolution
                : null;
    }
}
