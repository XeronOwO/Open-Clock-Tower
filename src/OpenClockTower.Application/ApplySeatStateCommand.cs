using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 座位状态发生变化（上游输入，说书人上报）。
/// </summary>
/// <remarks>
/// <para>
/// 只上报**本次观测到的维度**：`Life` 与 `Character` 都可为空（null = 本次未观测，不参与判定），
/// 至少给一个。这样"只看到生死变了"不会被硬塞一个角色值而误判（六维度相互独立）。
/// </para>
/// <para>
/// 本票据只交付步骤机所需的两个事实；完整的游戏状态与角色系统由后续票据接管。
/// 依据票据第 5 条：依赖失效的挂起请求会自动作废。
/// </para>
/// </remarks>
public sealed record ApplySeatStateCommand : GameCommand
{
    /// <summary>发生变化的座位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>本次观测到的生死；null = 未观测。</summary>
    public LifeState? Life { get; init; }

    /// <summary>本次观测到的角色；null = 未观测。</summary>
    public CharacterId? Character { get; init; }

    /// <summary>变化原因（谁的能力 / 哪个效果 / 人工修正）。</summary>
    public required string Reason { get; init; }

    /// <summary>导致变化的一方；人工修正可为空。</summary>
    public SeatId? CausedBy { get; init; }
}
