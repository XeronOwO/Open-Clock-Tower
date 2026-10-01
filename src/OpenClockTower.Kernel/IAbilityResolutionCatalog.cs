namespace OpenClockTower.Kernel;

/// <summary>
/// 按角色检索能力结算契约。
/// </summary>
/// <remarks>
/// 未实现返回 null。建表器在角色没有行动契约时**显式拒绝开夜**（架构 §2.6 能力边界）；
/// 结算期遇到「槽位有角色但没有结算契约」同样属于数据缺陷，由结算管线显式拒绝，不静默跳过。
/// </remarks>
public interface IAbilityResolutionCatalog
{
    /// <summary>取该角色的结算契约；未实现返回 null。</summary>
    IAbilityResolution? Find(CharacterId character);
}
