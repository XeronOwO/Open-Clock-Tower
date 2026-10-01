namespace OpenClockTower.Application;

/// <summary>四道闸的判定结果类别。</summary>
public enum GateDecisionKind
{
    /// <summary>全过，可以进内核。</summary>
    Pass,

    /// <summary>被某道闸拒绝。</summary>
    Reject,

    /// <summary>重复投递：按回执返回首次结果。</summary>
    Duplicate,
}
