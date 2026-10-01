namespace OpenClockTower.Application;

/// <summary>四道闸的判定结果。</summary>
public sealed record GateDecision
{
    /// <summary>判定类别。</summary>
    public required GateDecisionKind Kind { get; init; }

    /// <summary>拒绝原因；仅 Reject 时非空。</summary>
    public CommandRejection? Rejection { get; init; }

    /// <summary>首次结果的回执；仅 Duplicate 时非空。</summary>
    public CommandReceipt? Receipt { get; init; }

    /// <summary>放行。</summary>
    public static GateDecision Pass() => new() { Kind = GateDecisionKind.Pass };

    /// <summary>拒绝。</summary>
    public static GateDecision Reject(CommandRejection rejection) =>
        new() { Kind = GateDecisionKind.Reject, Rejection = rejection };

    /// <summary>重复投递，按回执重放首次结果。</summary>
    public static GateDecision Duplicate(CommandReceipt receipt) =>
        new() { Kind = GateDecisionKind.Duplicate, Receipt = receipt };
}
