using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 首版可用的角色夜间行动契约目录：按角色 slug 检索，未实现返回 null。
/// </summary>
/// <remarks>
/// 契约随角色分批实现（<c>docs/backlog/in-progress/settlement-engine.md</c>：25 个角色的逐角色实现另立票）。
/// 目录不依赖枚举顺序（D-0008），只做按键检索。
/// </remarks>
public static class NightActions
{
    /// <summary>生产用目录：当前已实现的第一批契约（钟表匠 / 筑梦师）。</summary>
    public static INightActionCatalog Default { get; } = new Catalog();

    private sealed class Catalog : INightActionCatalog
    {
        private static readonly Dictionary<CharacterId, INightAction> ByCharacter = new()
        {
            [new CharacterId("clockmaker")] = new ClockmakerNightAction(),
            [new CharacterId("dreamer")] = new DreamerNightAction(),
        };

        public INightAction? Find(CharacterId character) =>
            ByCharacter.TryGetValue(character, out var action) ? action : null;
    }
}
