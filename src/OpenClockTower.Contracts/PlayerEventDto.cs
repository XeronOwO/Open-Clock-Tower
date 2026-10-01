namespace OpenClockTower.Contracts;

/// <summary>
/// 重连补齐时下发的**玩家可见事件**（白名单投影，绝不含槽位 / 计划 / 他人请求）。
/// </summary>
public sealed record PlayerEventDto
{
    /// <summary>事件序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>PhaseStarted / RequestIssued / RequestAnswered / RequestVoided。</summary>
    public required string Kind { get; init; }

    /// <summary>阶段（仅 PhaseStarted）。</summary>
    public string? Phase { get; init; }

    /// <summary>发给该玩家的请求（仅 RequestIssued）。</summary>
    public OperationRequestDto? Request { get; init; }

    /// <summary>相关请求标识（响应 / 作废）。</summary>
    public string? RequestId { get; init; }

    /// <summary>他自己选择的值（仅 RequestAnswered）。</summary>
    public string? OptionValue { get; init; }

    /// <summary>作废原因（仅 RequestVoided）。</summary>
    public string? VoidReason { get; init; }

    /// <summary>作废说明（仅 RequestVoided）。</summary>
    public string? VoidNote { get; init; }

    /// <summary>信息类结果（仅 InformationResultIssued；只有内容，没有「可能为假」标记）。</summary>
    public InformationResultDto? Information { get; init; }
}
