namespace OpenClockTower.Kernel;

/// <summary>
/// 艺术家提问的折叠：问题状态的生命周期（提问 → 结清）与阶段边界携带口径（R-0040）。
/// </summary>
internal static class ArtistQuestionFolder
{
    /// <summary>提问：设置进行中问题；已有未结清的问题时显式失败（同一时刻最多一条）。</summary>
    internal static StepMachineState ApplyAsked(StepMachineState? state, ArtistQuestionAskedEvent asked)
    {
        var current = StepMachineFolder.Require(state, asked);
        if (current.ArtistQuestion is { } open)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：{open.Seat.Value} 号的艺术家提问还没有结清，"
                + $"不能直接覆盖为 {asked.Seat.Value} 号的新问题");
        }

        return current with
        {
            ArtistQuestion = new ArtistQuestion
            {
                Seat = asked.Seat,
                Character = asked.Character,
                Question = asked.Question,
            },
        };
    }

    /// <summary>结清：清空进行中问题；没有进行中问题（或席位对不上）时显式失败。</summary>
    internal static StepMachineState ApplyClosed(StepMachineState? state, ArtistQuestionClosedEvent closed)
    {
        var current = StepMachineFolder.Require(state, closed);
        if (current.ArtistQuestion is not { } open || open.Seat != closed.Seat)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：{closed.Seat.Value} 号的艺术家提问结清找不到对应的进行中问题");
        }

        return current with { ArtistQuestion = null };
    }

    /// <summary>
    /// 阶段边界携带：提问必须在本阶段内结清（回答 / 重问 / 强推作废）；
    /// 仍挂着说明收口缺失——显式失败，不顺延（D-0014 能力 3，与 BarberNight / SageNight 同族）。
    /// </summary>
    internal static ArtistQuestion? CarryAcrossPhase(StepMachineState? state)
    {
        if (state?.ArtistQuestion is { } open)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：{open.Seat.Value} 号的艺术家提问还没有结清就进入了新阶段");
        }

        return null;
    }
}
