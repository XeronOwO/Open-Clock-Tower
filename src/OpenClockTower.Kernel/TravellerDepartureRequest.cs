namespace OpenClockTower.Kernel;

/// <summary>
/// 一条**待说书人裁定**的旅行者离场申请（本批 D-0037）。
/// </summary>
/// <remarks>
/// <para>
/// 折在状态账里而不是步骤机状态里，理由有两条，都是结构性的：
/// ① 申请可以发生在**任何阶段之外**（旅行者在开局前也能申请离开，此时步骤机还是 null），
///    而账事件不允许把 null 变成"已开始"；
/// ② 申请要跨阶段存活（说书人隔一夜才裁定是常事），步骤机状态在阶段边界上重建，
///    挂在那里就得每加一次"跨阶段携带"。
/// 与 <see cref="GameState.DepartedSeats"/> 同一族：都是席位级的事实，不属于六维度与效果。
/// </para>
/// <para>
/// 它不是"操作请求"（<see cref="OperationRequest"/>）：那个方向相反——服务端问玩家，
/// 玩家作答；这个是玩家问说书人，说书人裁定。
/// </para>
/// </remarks>
public sealed record TravellerDepartureRequest
{
    /// <summary>申请离场的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>旅行者给出的理由（自由文本；可空）。</summary>
    public string? Note { get; init; }
}
