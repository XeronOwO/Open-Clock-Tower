namespace OpenClockTower.Application;

/// <summary>一条命令被拒绝的原因（拒绝 = 状态不变 + 有日志）。</summary>
public sealed record CommandRejection
{
    /// <summary>稳定错误码（如 <c>phase.no_request_for_you</c>），便于客户端与日志定位。</summary>
    public required string Code { get; init; }

    /// <summary>人类可读说明。</summary>
    public required string Message { get; init; }

    /// <summary>拒绝发生在哪道闸：<c>identity</c> / <c>idempotency</c> / <c>phase</c> / <c>legality</c> / <c>kernel</c>。</summary>
    public required string Gate { get; init; }
}
