namespace OpenClockTower.Application;

/// <summary>
/// 复盘步骤的呈现族：时间线与圆盘用它决定图标 / 配色 / 分组，不参与任何规则判定。
/// </summary>
/// <remarks>
/// 口径见 D-0020：步骤与事件流 1:1（显式排除项见 <see cref="ReplayStepCatalog"/>），
/// 顺序 = 事件序号，不跳步、不合并。族只是呈现分类——同一族里的不同事件仍是一个事件一步。
/// </remarks>
public enum ReplayStepKind
{
    /// <summary>阶段边界（黄昏 / 夜晚 / 黎明 / 白天）。</summary>
    Phase,

    /// <summary>槽位激活 / 阻塞 / 跳过。</summary>
    Slot,

    /// <summary>面向玩家的操作请求（发出 / 作答 / 作废）。</summary>
    Request,

    /// <summary>面向说书人的裁定点（开出 / 结清）。</summary>
    Decision,

    /// <summary>能力结算。</summary>
    Ability,

    /// <summary>信息结果。</summary>
    Information,

    /// <summary>六维度状态变化（角色 / 阵营 / 生死 / 醉酒 / 中毒）。</summary>
    State,

    /// <summary>效果生命周期（施加 / 终止 / 即时生效）。</summary>
    Effect,

    /// <summary>白天流程（开始 / 提名 / 投票 / 计票 / 处决 / 结束）。</summary>
    Day,

    /// <summary>角色触发窗口与整局事实（理发师 / 贤者 / 心上人 / 麻脸巫婆 / 方古 / 呆瓜 / 疯狂要求）。</summary>
    Trigger,

    /// <summary>控制模式变化（接管 / 交还）。</summary>
    Control,

    /// <summary>终局结论。</summary>
    Outcome,
}
