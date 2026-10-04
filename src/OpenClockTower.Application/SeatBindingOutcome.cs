namespace OpenClockTower.Application;

/// <summary>席位认领的结果（D-0021）：拒绝带机器可读原因，调用方据此写审计与中性文案。</summary>
public sealed record SeatBindingOutcome
{
    /// <summary>是否通过。</summary>
    public required bool Accepted { get; init; }

    /// <summary>机器可读结果码：<c>ok</c> / <c>seat_taken</c> / <c>account_already_seated</c> / <c>conflict</c>。</summary>
    public required string Code { get; init; }

    /// <summary>中性说明。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>生效后的绑定（通过时非空）。</summary>
    public SeatBinding? Binding { get; init; }

    /// <summary>本次调用是否**新建**了绑定（false = 幂等命中已有绑定）。</summary>
    public required bool Created { get; init; }
}
