namespace OpenClockTower.Application;

/// <summary>重连补齐时允许下发给玩家的**事件类别**。</summary>
/// <remarks>
/// 依据 D-0012 §4.3 与 D-0013 §5：原始事件流里有他人请求、槽位与推进进度，
/// 玩家只能收到与他有关的那几条；类别是白名单，不在其中一律不下发。
/// </remarks>
public enum PlayerEventKind
{
    /// <summary>阶段开始（玩家可观测的公开信息：昼夜）。</summary>
    PhaseStarted,

    /// <summary>发给他自己的操作请求。</summary>
    RequestIssued,

    /// <summary>他自己请求的响应结果。</summary>
    RequestAnswered,

    /// <summary>他自己请求的作废与原因。</summary>
    RequestVoided,

    /// <summary>发给他的信息类结果（他自己能力得到的信息）。</summary>
    InformationResultIssued,
}
