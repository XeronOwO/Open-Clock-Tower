namespace OpenClockTower.Kernel;

/// <summary>
/// 博学者提问的折叠：提问状态的生命周期（提问 → 结清）、「今天已经问过」的账，
/// 以及阶段边界携带口径（R-0057）。
/// </summary>
/// <remarks>
/// 与 <see cref="ArtistQuestionFolder"/> 的分工：那个是"每局限一次"（用度由能力使用账本管），
/// 这个是"**每个白天**一次"——用度按白天记账，阶段边界自然清零（<see cref="CarryAcrossPhase"/>）。
/// </remarks>
internal static class SavantQuestionFolder
{
    /// <summary>提问：记下进行中提问与"今天问过的席位"；已有未结清提问时显式失败（同一时刻最多一条）。</summary>
    internal static StepMachineState ApplyAsked(StepMachineState? state, SavantQuestionAskedEvent asked)
    {
        var current = StepMachineFolder.Require(state, asked);
        if (current.SavantQuestion is { } open)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：{open.Seat.Value} 号的博学者提问还没有结清，"
                + $"不能直接覆盖为 {asked.Seat.Value} 号的新提问");
        }

        return current with
        {
            SavantQuestion = new SavantQuestion
            {
                Seat = asked.Seat,
                Character = asked.Character,
            },
            SavantAskedSeat = asked.Seat,
        };
    }

    /// <summary>结清：清空进行中提问；没有进行中提问（或席位对不上）时显式失败。</summary>
    internal static StepMachineState ApplyClosed(StepMachineState? state, SavantQuestionClosedEvent closed)
    {
        var current = StepMachineFolder.Require(state, closed);
        if (current.SavantQuestion is not { } open || open.Seat != closed.Seat)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：{closed.Seat.Value} 号的博学者提问结清找不到对应的进行中提问");
        }

        return current with { SavantQuestion = null };
    }

    /// <summary>
    /// 阶段边界携带：提问必须在本白天内结清（回答 / 强推作废）；
    /// 仍挂着说明收口缺失——显式失败，不顺延（D-0014 能力 3，与艺术家提问同族）。
    /// </summary>
    internal static SavantQuestion? CarryAcrossPhase(StepMachineState? state)
    {
        if (state?.SavantQuestion is { } open)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：{open.Seat.Value} 号的博学者提问还没有结清就进入了新阶段");
        }

        return null;
    }
}
