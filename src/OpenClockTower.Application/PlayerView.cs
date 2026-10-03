using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 发给某个玩家的投影：他只能看到自己该看到的东西。
/// </summary>
/// <remarks>
/// 依据 D-0013 §5 与 D-0012 §4.3：这里**没有**轮次、进度、槽位、他人活动等信息——
/// 玩家端在夜晚只有统一界面。信息隔离在服务端投影强制，不依赖前端不显示。
/// </remarks>
public sealed record PlayerView
{
    /// <summary>接收者席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>当前大阶段（昼夜属公开信息）；未开局为 null。</summary>
    public GamePhase? Phase { get; init; }

    /// <summary>只包含发给该席位、且仍在等待响应的请求。</summary>
    public OperationRequest? PendingRequest { get; init; }

    /// <summary>
    /// 本局发给该席位的信息类结果（自己能力得到的信息），按发生顺序。
    /// 只含内容；「可能为假」标记只说书人可见（百科《重要细节》三-1：不要告诉玩家他醉酒或中毒）。
    /// </summary>
    public required IReadOnlyList<InformationResultSnapshot> InformationResults { get; init; }

    /// <summary>
    /// 白天投影（公开事实 + 我能做什么）；还没有开过白天时为 null。
    /// 只含公开事实与"自己的"权限位——没有任何说书人专属字段。
    /// </summary>
    public PlayerDay? Day { get; init; }

    /// <summary>胜负结论；null = 游戏仍在进行。对局结束后对全体玩家一致可见（R-0024）。</summary>
    public GameOutcome? Outcome { get; init; }

    /// <summary>呆瓜的公开选择（含"没选"的跳过），按发生顺序；公开事实（R-0027）。</summary>
    public IReadOnlyList<KlutzChoiceRecord> KlutzChoices { get; init; } = [];

    /// <summary>投影对应的事件流序号（重连补齐用）。</summary>
    public required long Sequence { get; init; }

    /// <summary>
    /// 本人进行中的艺术家提问全文；null = 没有（R-0040）。
    /// 只对本人生效——其他玩家的投影里没有这条字段。
    /// </summary>
    public string? PendingQuestion { get; init; }

    /// <summary>
    /// 本人此刻能不能发起艺术家的白天提问（白天开着、本人是艺术家且还没用过；R-0040）。
    /// 只对本人生效——它是权限位，不是其他人的观察面。
    /// </summary>
    public bool CanAskArtistQuestion { get; init; }

    /// <summary>
    /// 本人已经用尽的一次性能力 slug（如 <c>artist</c> / <c>seamstress</c>）；空数组 = 没有用尽（R-0040）。
    /// 只列本人的：其他人的用度是私密信息（D-0012 §4.3）。
    /// </summary>
    public IReadOnlyList<string> ExhaustedAbilities { get; init; } = [];
}
