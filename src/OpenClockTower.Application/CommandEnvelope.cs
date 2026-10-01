namespace OpenClockTower.Application;

/// <summary>带身份与幂等信息的命令信封：四道闸与审计都以它为输入。</summary>
public sealed record CommandEnvelope
{
    /// <summary>命令本体。</summary>
    public required GameCommand Command { get; init; }

    /// <summary>服务端推导出的操作者。</summary>
    public required Actor Actor { get; init; }

    /// <summary>幂等键（同一命令重复投递必须复用同一个键）。</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>客户端序号（排查用；服务端判定重复以幂等键为准）。</summary>
    public long ClientSequence { get; init; }
}
