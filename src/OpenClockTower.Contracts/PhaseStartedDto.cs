namespace OpenClockTower.Contracts;

/// <summary>推给玩家的"阶段开始"（公开信息：首夜 / 其他夜晚 / 白天 / 结算中）。</summary>
/// <remarks>
/// 阶段是公开信息：向**全部已绑定席位**广播，不违反 D-0013 §5（那里约束的是他人请求 / 作废
/// 这类定向事实）。未连接的玩家重连时从 <c>PlayerViewDto.Phase</c> 快照取。
/// </remarks>
public sealed record PhaseStartedDto
{
    /// <summary>GamePhase 名。</summary>
    public required string Phase { get; init; }
}
