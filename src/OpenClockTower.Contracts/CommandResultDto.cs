namespace OpenClockTower.Contracts;

/// <summary>命令回执：命令是否生效、当前序号、拒绝 / 失败原因。</summary>
public sealed record CommandResultDto
{
    /// <summary>Accepted / Rejected / Duplicate / Failed。</summary>
    public required string Kind { get; init; }

    /// <summary>提交后的最新序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>拒绝错误码；其它类别为 null。</summary>
    public string? RejectionCode { get; init; }

    /// <summary>拒绝说明；其它类别为 null。</summary>
    public string? RejectionMessage { get; init; }

    /// <summary>失败说明；其它类别为 null。</summary>
    public string? Failure { get; init; }

    /// <summary>重建结果与内存状态是否一致（仅重建命令）。</summary>
    public bool? MachineEquivalent { get; init; }

    /// <summary>重建结果与持久化快照是否一致（仅重建命令；无快照为 null）。</summary>
    public bool? SnapshotEquivalent { get; init; }
}
