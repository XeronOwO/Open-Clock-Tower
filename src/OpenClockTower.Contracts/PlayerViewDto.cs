namespace OpenClockTower.Contracts;

/// <summary>
/// 玩家视图：只有他自己的席位、当前大阶段与他自己的挂起请求。
/// </summary>
public sealed record PlayerViewDto
{
    /// <summary>席位。</summary>
    public required int Seat { get; init; }

    /// <summary>当前大阶段（昼夜属公开信息）。</summary>
    public required string Phase { get; init; }

    /// <summary>
    /// **本席**当前的角色 slug；null = 还没有观测到（未分配 / 未上报）——不猜、不给默认值（D-0015）。
    /// </summary>
    /// <remarks>
    /// 只描述收件人本人：这是一名玩家**自己的**角色，不是"席位 → 角色"的映射（R-0059 第 4 条）。
    /// 中文名走呈现层（`web/src/display/labels.ts` 的花名册副本，`RosterMirrorGateTests` 对账）。
    /// 换角后随事件即时跟随（R-0059 第 2 条）。
    /// </remarks>
    public string? Character { get; init; }

    /// <summary>
    /// **本席**当前阵营（`Alignment` 枚举名 `Good` / `Evil`）；null = 还没有观测到。
    /// </summary>
    /// <remarks>
    /// 百科《术语汇总》：「玩家始终会得知其当前的阵营。」百科《重要细节》三-2：变化"第一时间秘密得知"，
    /// 且"得知自己的角色或阵营发生变化并不算是获得信息"，因此这两个字段**不是**信息结果、
    /// 不受醉酒 / 中毒 / 涡流伪造（R-0059 第 3 条）。
    /// </remarks>
    public string? Alignment { get; init; }

    /// <summary>发给该玩家的挂起请求；没有时为 null。</summary>
    public OperationRequestDto? PendingRequest { get; init; }

    /// <summary>发给该玩家的信息类结果（他自己能力得到的信息），按发生顺序。</summary>
    public required IReadOnlyList<InformationResultDto> InformationResults { get; init; }

    /// <summary>白天投影（公开事实 + 自己能做什么）；还没有开过白天时为 null。</summary>
    public PlayerDayDto? Day { get; init; }

    /// <summary>胜负结论；null = 游戏仍在进行。结束后对全体玩家一致可见（R-0024）。</summary>
    public GameOutcomeDto? Outcome { get; init; }

    /// <summary>呆瓜的公开选择（含跳过），按发生顺序（R-0027）。</summary>
    public required KlutzChoiceDto[] KlutzChoices { get; init; }

    /// <summary>本局公开的「席位 → 玩家名」映射（D-0021；无玩家名的席位不出现）。</summary>
    public required SeatDisplayNameDto[] SeatNames { get; init; }

    /// <summary>本人进行中的艺术家提问全文；null = 没有（R-0040）。只对本人生效。</summary>
    public string? PendingQuestion { get; init; }

    /// <summary>本人此刻能不能发起艺术家的白天提问（白天开着、本人是艺术家且还没用过）。只对本人生效。</summary>
    public bool CanAskArtistQuestion { get; init; }

    /// <summary>本人此刻能不能向说书人要两条信息（白天开着、本人是博学者且今天还没要过；R-0057）。只对本人生效。</summary>
    public bool CanAskSavantQuestion { get; init; }

    /// <summary>本人有一条博学者提问在等说书人给两条信息（R-0057）。只对本人生效。</summary>
    public bool AwaitingSavantQuestion { get; init; }

    /// <summary>本人已经用尽的一次性能力 slug（R-0040）；只列本人的。</summary>
    public string[] ExhaustedAbilities { get; init; } = [];

    /// <summary>
    /// 本人是否已经以旅行者身份离场（D-0037）：界面据此说"你已离场"，
    /// 而不是显示成"还没有分配角色"（那会把一个明确事实说成未知）。
    /// </summary>
    public bool Departed { get; init; }

    /// <summary>本人此刻能不能提出离场申请（在座的旅行者且没有待批申请；D-0037）。只对本人生效。</summary>
    public bool CanRequestDeparture { get; init; }

    /// <summary>
    /// 本人此刻**有没有待批的离场申请**（D-0037）。界面拿它决定等待态显不显示——
    /// 理由是可选字段，不能用"理由是不是 null"代替这件事。
    /// </summary>
    public bool HasPendingDeparture { get; init; }

    /// <summary>本人待批的离场申请理由；null = 没有待批申请**或**申请里没写理由（D-0037）。只对本人生效。</summary>
    public string? PendingDepartureNote { get; init; }

    /// <summary>最近一次**本人**离场裁定的结论；null = 还没有裁定过（D-0037）。只下发给申请人本人。</summary>
    public DepartureRulingDto? LastDepartureRuling { get; init; }
}
