namespace OpenClockTower.Contracts;

/// <summary>
/// 推给玩家的操作请求。
/// </summary>
/// <remarks>
/// 刻意**不含**槽位 / 轮次 / 进度字段（D-0013 §5）：客户端拿不到的东西，也显示不出来。
/// </remarks>
public sealed record OperationRequestDto
{
    /// <summary>
    /// 这条请求状态对应的事件流序号（推送 = 背书事件序号；快照 = 快照序号）。
    /// 客户端只接受序号更大的请求状态，杜绝补齐响应让已了结的请求"复活"。
    /// </summary>
    public required long Sequence { get; init; }

    /// <summary>请求稳定标识。</summary>
    public required string RequestId { get; init; }

    /// <summary>接收者席位（客户端可校验这是给自己的）。</summary>
    public required int Seat { get; init; }

    /// <summary>为什么需要做这个选择。</summary>
    public required string Context { get; init; }

    /// <summary>合法选项。</summary>
    public required DecisionOptionDto[] Options { get; init; }

    /// <summary>
    /// 第二维合法选项（为空 = 单维选择）。两维时答案编码为 <c>{第一维}|{第二维}</c>
    /// （依据 <c>docs/standard/rulings.md</c> R-0021：洗脑师的一次行动是玩家 × 善良角色的原子选择）。
    /// </summary>
    public required DecisionOptionDto[] SecondaryOptions { get; init; }
}
