namespace OpenClockTower.Kernel;

/// <summary>一条「杂耍艺人公开猜测」输入：谁、猜了哪几条。</summary>
/// <remarks>
/// 席位由凭据推导（D-0012）：命令面没有自称身份，玩家只能替自己猜。
/// 0–5 条的上限与「必须是自己的首个白天」在 <see cref="JugglerGuessMachine"/> 里统一校验（R-0057-B）。
/// </remarks>
public sealed record MakeJugglerGuessesInput : StepMachineInput
{
    /// <summary>猜测者席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>这一批猜测（0–5 条，按提交顺序）。</summary>
    public required IReadOnlyList<JugglerGuess> Guesses { get; init; }
}
