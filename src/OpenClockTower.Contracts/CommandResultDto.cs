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

    /// <summary>重建结果与内存状态账是否一致（仅重建命令；非重建命令为 null）。</summary>
    public bool? LedgerEquivalent { get; init; }

    /// <summary>
    /// 本次为旅行者加入**追加的席位号**；仅"加入旅行者且服务端分配席位"的命令非空
    /// （重复投递也按首次追加的席位回填）。
    /// </summary>
    /// <remarks>
    /// 凭据不在这里（D-0038）：邀请码由说书人另行签发（`IssueSeatInvitation`）并只在签发那一次出现。
    /// </remarks>
    public int? IssuedSeat { get; init; }
}
