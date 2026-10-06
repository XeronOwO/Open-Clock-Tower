namespace OpenClockTower.Contracts;

/// <summary>开桌结果（D-0026：登录即可开；D-0027：开桌即成为这一桌的说书人）。</summary>
/// <remarks>
/// 失败也是正常结果（<see cref="Ok"/> = false + 中性 <see cref="Message"/>），不是异常。
/// 回执里**没有凭据**（D-0027）：主持这一桌的依据是"你是开桌账号"，不再需要回执携带任何秘密。
/// </remarks>
public sealed record LobbyCreateResultDto
{
    /// <summary>是否成功。</summary>
    public required bool Ok { get; init; }

    /// <summary>机器可读结果码：ok / not_allowed / invalid_session / invalid_name / invalid_seat_count / id_conflict。</summary>
    public required string Code { get; init; }

    /// <summary>中性说明（可直接展示）。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>新桌标识；失败时为空串。前端拿它进主持台（连接声明 `?gameId=`）。</summary>
    public string GameId { get; init; } = string.Empty;

    /// <summary>席位数；失败时为 0。</summary>
    public int SeatCount { get; init; }
}
