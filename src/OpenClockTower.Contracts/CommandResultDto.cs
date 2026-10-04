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
    /// 本次为旅行者加入签发的席位号；仅"加入旅行者且服务端分配席位"的命令非空
    /// （重复投递也按首次签发的席位回填）。
    /// </summary>
    public int? IssuedSeat { get; init; }

    /// <summary>
    /// 本次签发的席位票据（说书人转交给新到场的玩家）；仅"加入旅行者且服务端分配席位"的命令非空。
    /// 票据是入场凭据：只回给出命令的说书人，不进事件流、不进任何投影。
    /// </summary>
    public string? IssuedSeatTicket { get; init; }
}
