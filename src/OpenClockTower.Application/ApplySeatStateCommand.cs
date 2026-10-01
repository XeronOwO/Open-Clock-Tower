using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 座位状态发生变化（上游输入，现阶段由说书人上报；结算引擎落地后由引擎产出同样的事件）。
/// </summary>
/// <remarks>
/// <para>
/// 只上报**本次观测到的维度**：六个维度都可为空（null = 本次未观测，不参与判定），至少给一个。
/// 这样"只看到生死变了"不会被硬塞一个角色值而误判（六维度相互独立，见
/// <c>docs/architecture/current.md</c> §2.1）。
/// </para>
/// <para>
/// 报进来的维度会折进状态账（<see cref="GameState"/>），每个维度带着本次的
/// <see cref="Reason"/> 与 <see cref="CausedBy"/> 单独留痕；依赖失效的挂起请求仍按票据第 5 条自动作废。
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

    /// <summary>本次观测到的阵营；null = 未观测。</summary>
    public Alignment? Alignment { get; init; }

    /// <summary>本次观测到的醉酒状态；null = 未观测。</summary>
    public DrunkState? Drunk { get; init; }

    /// <summary>本次观测到的中毒状态；null = 未观测。</summary>
    public PoisonState? Poison { get; init; }

    /// <summary>变化原因（谁的能力 / 哪个效果 / 人工修正）。</summary>
    public required string Reason { get; init; }

    /// <summary>导致变化的一方；人工修正可为空。</summary>
    public SeatId? CausedBy { get; init; }
}
