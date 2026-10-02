namespace OpenClockTower.Kernel;

/// <summary>
/// 座位状态变化的事实记录：**谁**因为**什么原因**变成了什么样（说书人上帝视角的基础数据）。
/// </summary>
/// <remarks>
/// 这是审计与归因事件：它不改步骤机状态，但让"当前这一步的玩家为什么被中毒 / 解除中毒"
/// 这类问题在事件流里有据可查。观测维度可空：只记录本次真正观测到的维度。
/// </remarks>
public sealed record SeatStateChangedEvent : GameEvent
{
    /// <summary>发生变化的座位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>变化后的生死；null = 本次未观测。</summary>
    public LifeState? Life { get; init; }

    /// <summary>变化后的角色；null = 本次未观测。</summary>
    public CharacterId? Character { get; init; }

    /// <summary>
    /// 本次变化**之前**该席位已知的角色；null = 之前未观测到角色，或本次没有观测角色。
    /// </summary>
    /// <remarks>
    /// 由提交管线在落库前统一补全（用提交前的账），**产出方不必自己填**——与「维度 → 效果链接」
    /// 的补全同族。两个用途：① 说书人上帝视角的「从什么变成什么」；
    /// ② 胜负求值：角色维度被覆盖后，「恶魔 → 非恶魔」这一事实只能靠它读出来
    /// （<c>docs/standard/rulings.md</c> R-0029：运行期恶魔角色清零判善良获胜）。
    /// </remarks>
    public CharacterId? PreviousCharacter { get; init; }

    /// <summary>变化后的阵营；null = 本次未观测。</summary>
    public Alignment? Alignment { get; init; }

    /// <summary>变化后的醉酒状态；null = 本次未观测。</summary>
    public DrunkState? Drunk { get; init; }

    /// <summary>变化后的中毒状态；null = 本次未观测。</summary>
    public PoisonState? Poison { get; init; }

    /// <summary>变化原因。</summary>
    public required string Reason { get; init; }

    /// <summary>导致变化的一方。</summary>
    public SeatId? CausedBy { get; init; }

    /// <summary>
    /// 本次变化由哪条持续型效果导致（票据第 6 条的「维度 → 效果链接」）；
    /// null = 与效果无关（开局分配 / 说书人上报等）。说书人面板据此回答
    /// 「这一格中毒是哪条效果造成的」。
    /// </summary>
    public EffectId? EffectId { get; init; }
}
