namespace OpenClockTower.Kernel;

/// <summary>
/// 死亡保护来源契约（规则层实现）：回答"这个席位的这次死亡是否被某个角色能力保护"。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IAdjudicatedExecutionSource"/> / <see cref="IEventTrigger"/> 同族：内核声明需要的外部能力，
/// 规则层实现（怪咖按当天裁定与能力生效状态判定，R-0048），内核因此不认识角色 slug（D-0008）。
/// </para>
/// <para>
/// **纯函数**：只读账、座次与当天账，不写账、不产事件；多个来源由 <see cref="DeathProtectionQuery"/> 聚合。
/// 返回 null 表示"与本来源无关"（不参与聚合）。
/// </para>
/// </remarks>
public interface IDeathProtectionSource
{
    /// <summary>本契约负责的角色（诊断与目录自检用；内核不按 slug 分派，D-0008）。</summary>
    CharacterId Character { get; }

    /// <summary>判定 <paramref name="context"/> 描述的那次死亡是否受本来源保护；无关时返回 null。</summary>
    DeathProtectionAssessment? Evaluate(DeathProtectionContext context);
}
