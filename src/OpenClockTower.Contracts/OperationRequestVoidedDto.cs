namespace OpenClockTower.Contracts;

/// <summary>推给玩家的"请求已作废"及其原因。</summary>
public sealed record OperationRequestVoidedDto
{
    /// <summary>被作废的请求标识。</summary>
    public required string RequestId { get; init; }

    /// <summary>作废原因。</summary>
    public required string Reason { get; init; }

    /// <summary>说明。</summary>
    public string? Note { get; init; }
}
