using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 面向单个玩家的事件投影：重连补齐用。
/// </summary>
/// <remarks>
/// 这是一个**白名单投影**：只含公开阶段信息与"发给该玩家自己"的请求 / 响应 / 作废，
/// 刻意不带槽位、计划、进度与他人信息（D-0012 §4.3、D-0013 §5）。
/// </remarks>
public sealed record PlayerEvent
{
    /// <summary>事件序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>事件类别。</summary>
    public required PlayerEventKind Kind { get; init; }

    /// <summary>阶段（仅 PhaseStarted）。</summary>
    public GamePhase? Phase { get; init; }

    /// <summary>发给他自己的请求（仅 RequestIssued）。</summary>
    public OperationRequest? Request { get; init; }

    /// <summary>相关请求标识（响应 / 作废）。</summary>
    public OperationRequestId? RequestId { get; init; }

    /// <summary>他自己选择的值（仅 RequestAnswered）。</summary>
    public string? OptionValue { get; init; }

    /// <summary>作废原因与说明（仅 RequestVoided）。</summary>
    public OperationRequestVoid? Void { get; init; }

    /// <summary>
    /// 发给他的信息类结果（仅 InformationResultIssued）。
    /// 事件里的「可能为假」标记**不下发**：那会让玩家立刻知道自己醉酒 / 中毒（《重要细节》三-1）。
    /// </summary>
    public InformationResultIssuedEvent? Information { get; init; }

    /// <summary>旅行者加入 / 离场的席位（仅 TravellerJoined / TravellerDeparted；公开事实）。</summary>
    public SeatId? Seat { get; init; }

    /// <summary>
    /// 旅行者加入时的角色（仅 TravellerJoined）：公开宣告「谁 + 角色 + 能力」里的角色；
    /// **阵营不在事件里下发**（百科《旅行者》· 旅行者运作方式第 6 步）。
    /// </summary>
    public CharacterId? Character { get; init; }
}
