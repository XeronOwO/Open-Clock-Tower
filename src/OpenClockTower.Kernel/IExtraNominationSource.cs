namespace OpenClockTower.Kernel;

/// <summary>
/// 额外提名窗口来源契约（规则层实现）：回答"这次首次处决后有没有可用屠夫、窗口授予谁"（R-0050）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IDeathProtectionSource"/> 同族：内核声明需要的外部能力，规则层实现（屠夫：在局 + 存活 +
/// 能力生效），内核因此不认识角色 slug（D-0008）。多个来源由 <see cref="ExtraNominationQuery"/> 聚合，
/// 命中「可用」即开窗。
/// </para>
/// <para>
/// **纯函数**：只读账、座次与当天账，不写账、不产事件。返回 null 表示"与本来源无关"（不参与聚合）。
/// </para>
/// </remarks>
public interface IExtraNominationSource
{
    /// <summary>本契约负责的角色（诊断与目录自检用；内核不按 slug 分派，D-0008）。</summary>
    CharacterId Character { get; }

    /// <summary>判定是否有可用屠夫；与本来源无关时返回 null。</summary>
    ExtraNominationAssessment? Evaluate(ExtraNominationContext context);
}
