namespace OpenClockTower.Application;

/// <summary>
/// 配板建议（只读）：成功带种子、席位映射、净分布与说明；失败带机器可读码。
/// </summary>
/// <remarks>
/// 建议是**瞬态**的——不落账、不进事件流（R-0042 第 5 条）；说书人可重摇 / 手改，
/// 提交仍走既有分配命令面（D-0017），重放只折显式分配（D-0008 / D-0011）。
/// </remarks>
public sealed record SetupProposalResult
{
    /// <summary>前置条件或求解失败的机器可读码（`setup.*`）；成功为 null。</summary>
    public string? FailureCode { get; init; }

    /// <summary>失败说明（中文，直接给说书人看）。</summary>
    public string? FailureMessage { get; init; }

    /// <summary>本次使用的显式随机输入（种子）：重摇 = 新种子；复现 = 原样传回。</summary>
    public string Seed { get; init; } = string.Empty;

    /// <summary>本次配板覆盖的非旅行者人数（R-0046：分布表按它取行）。</summary>
    public int NonTravellerCount { get; init; }

    /// <summary>本局旅行者人数（席位总数 − 非旅行者人数）：不参与配板、不占四类型名额（R-0046）。</summary>
    public int TravellerCount { get; init; }

    /// <summary>席位 → 角色（按席位升序）。</summary>
    public IReadOnlyList<SeatCharacterAssignment> Assignments { get; init; } = [];

    /// <summary>净分布（镇民 / 外来者 / 爪牙 / 恶魔）。</summary>
    public IReadOnlyList<SetupTypeCount> Distribution { get; init; } = [];

    /// <summary>设置调整与钳制的显式说明（R-0042 第 2 条：不静默改写）。</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>是否给出建议。</summary>
    public bool Ok => FailureCode is null;

    /// <summary>失败结果；人数已知时一并回显，便于说书人面解释（R-0046）。</summary>
    public static SetupProposalResult Failed(
        string code,
        string message,
        int nonTravellerCount = 0,
        int travellerCount = 0) =>
        new()
        {
            FailureCode = code,
            FailureMessage = message,
            NonTravellerCount = nonTravellerCount,
            TravellerCount = travellerCount,
        };
}
