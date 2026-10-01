using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>角色 → 夜间行动契约的只读目录。</summary>
/// <remarks>
/// 只做按键检索、不做枚举顺序依赖（D-0008）。未实现契约的角色返回 null，
/// 建表器会据此显式拒绝开夜——禁止把它当成"没有行动"静默跳过。
/// </remarks>
public interface INightActionCatalog
{
    /// <summary>取某角色的契约；未实现时返回 null。</summary>
    INightAction? Find(CharacterId character);
}
