namespace OpenClockTower.Contracts;

/// <summary>开桌结果（D-0026：登录即可开，开桌者凭票据成为这一桌说书人）。</summary>
/// <remarks>
/// 失败也是正常结果（<see cref="Ok"/> = false + 中性 <see cref="Message"/>），不是异常。
/// <see cref="StorytellerTicket"/> 是**秘密**：只回给开桌的那个人，用于进入说书人台。
/// </remarks>
public sealed record LobbyCreateResultDto
{
    /// <summary>是否成功。</summary>
    public required bool Ok { get; init; }

    /// <summary>机器可读结果码：ok / not_allowed / invalid_session / invalid_name / invalid_seat_count / id_conflict。</summary>
    public required string Code { get; init; }

    /// <summary>中性说明（可直接展示）。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>新桌标识；失败时为空串。</summary>
    public string GameId { get; init; } = string.Empty;

    /// <summary>说书人票据（仅成功时返回一次；这是成为该桌说书人的凭据）。</summary>
    public string? StorytellerTicket { get; init; }

    /// <summary>席位数；失败时为 0。</summary>
    public int SeatCount { get; init; }
}
