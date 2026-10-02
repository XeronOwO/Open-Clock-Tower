namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤机拒绝一条输入的原因（拒绝：状态不变、事件为空）。
/// </summary>
public enum StepMachineRejectionReason
{
    /// <summary>请求不是当前挂起的那个。</summary>
    NotCurrentRequest,

    /// <summary>请求已经了结（已响应或已作废）。</summary>
    RequestAlreadyResolved,

    /// <summary>选项不在服务端算出的合法集合里。</summary>
    OptionNotLegal,

    /// <summary>当前没有等待响应的请求。</summary>
    NoPendingRequest,

    /// <summary>当前没有等待说书人的裁定点。</summary>
    NoPendingDecision,

    /// <summary>控制模式本来就是目标模式。</summary>
    ControlModeUnchanged,

    /// <summary>本计划已走完，不能再推进。</summary>
    PlanAlreadyCompleted,

    /// <summary>未知输入。</summary>
    UnexpectedInput,

    /// <summary>
    /// 能力是否生效判定不了：行动者的生死 / 醉酒 / 中毒还没观测齐。
    /// 结算拒绝整条输入，等说书人把状态补上（D-0015：不猜）。
    /// </summary>
    LedgerIncomplete,

    /// <summary>本局已经结束：一切输入都被拒（R-0024）。</summary>
    GameEnded,
}
