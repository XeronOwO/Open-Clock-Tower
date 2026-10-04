namespace OpenClockTower.Kernel;

/// <summary>
/// 白天账折叠的共享编辑原语：定位进行中的白天 / 提名 / 流放，校验状态并替换记录。
/// </summary>
/// <remarks>
/// 从 <see cref="DayLedgerFolder"/> 抽出（单文件 600 行门禁前的「先拆再改」）：提名与流放两族事件
/// 共用同一套「找到当前那条、校验状态、替换回去」的写法；错误信息保持同款——恢复必须失败，
/// 且失败原因要能一眼定位（D-0014 能力 3）。
/// </remarks>
internal static class DayLedgerEdit
{
    /// <summary>替换当前进行中的白天账（天数不符 = 事件流顺序损坏）。</summary>
    internal static DayState UpdateOpenDay(DayState state, int dayNumber, Func<DayRecord, DayRecord> update)
    {
        var day = state.OpenDay;
        if (day is null || day.DayNumber != dayNumber)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {dayNumber} 不是当前进行中的白天（当前：{(day is null ? "无" : day.DayNumber)}）");
        }

        var days = state.Days.ToArray();
        days[^1] = update(day);
        return state with { Days = days };
    }

    /// <summary>找到当前开放的第 N 项提名；不存在或已计票 = 事件流顺序损坏。</summary>
    internal static NominationRecord FindOpenNomination(DayRecord day, int index, string action)
    {
        var nomination = day.Nominations.FirstOrDefault(item => item.Index == index);
        if (nomination is null)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {day.DayNumber} 没有第 {index} 项提名，却收到{action}事件");
        }

        if (nomination.Status != NominationStatus.Voting)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {day.DayNumber} 第 {index} 项提名已经计票，不能再次{action}");
        }

        return nomination;
    }

    /// <summary>找到当前未结清的第 N 条流放；不存在或已计票 = 事件流顺序损坏。</summary>
    internal static ExileRecord FindOpenExile(DayRecord day, int index, string action)
    {
        var exile = day.Exiles.FirstOrDefault(item => item.Index == index);
        if (exile is null)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {day.DayNumber} 没有第 {index} 条流放，却收到{action}事件");
        }

        if (exile.Status != ExileStatus.Voting)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {day.DayNumber} 第 {index} 条流放已经计票，不能再次{action}");
        }

        return exile;
    }

    /// <summary>把替换后的提名写回当天账（按序号定位）。</summary>
    internal static DayRecord ReplaceNomination(DayRecord day, NominationRecord nomination)
    {
        var nominations = day.Nominations.ToArray();
        var position = Array.FindIndex(nominations, item => item.Index == nomination.Index);
        if (position < 0)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {day.DayNumber} 没有第 {nomination.Index} 项提名");
        }

        nominations[position] = nomination;
        return day with { Nominations = nominations };
    }

    /// <summary>把替换后的流放写回当天账（按序号定位）。</summary>
    internal static DayRecord ReplaceExile(DayRecord day, ExileRecord exile)
    {
        var exiles = day.Exiles.ToArray();
        var position = Array.FindIndex(exiles, item => item.Index == exile.Index);
        if (position < 0)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {day.DayNumber} 没有第 {exile.Index} 条流放");
        }

        exiles[position] = exile;
        return day with { Exiles = exiles };
    }
}
