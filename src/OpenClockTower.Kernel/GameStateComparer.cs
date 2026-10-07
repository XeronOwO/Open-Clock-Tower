namespace OpenClockTower.Kernel;

/// <summary>
/// 状态账的**结构等价**比较：用于"按事件日志重建"之后的校验（D-0014 能力 3）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="StepMachineStateComparer"/> 同源同用途：C# record 的自动相等只比较集合成员的
/// **引用**，而状态账里的席位、效果与两本账都是有序列表；这个比较器按**业务键**配对，
/// 不依赖集合的枚举顺序——重建与重放枚举顺序相同只是巧合，等价判定不能建立在巧合上。
/// </para>
/// <para>
/// 覆盖 <see cref="GameState"/> 的全部集合：席位账（逐维度值 / 原因 / 导致方 / 效果链接）、
/// 离场账（按离场顺序）、待批的离场申请（D-0037）、持续型效果（含终止事实）、即时型效果、
/// 能力使用账、失效账（含黎明窗口起点）。
/// 任何一个不同都算分叉——重建报告回答的是"整本账是否与事件流一致"，不能只比其中几张表。
/// </para>
/// <para>
/// **集合顺序不在等价判定内**：席位按座位号、效果按效果标识配对，两本账与疯狂要求按多重集合比。
/// 重建关心的是"同一份账"而不是"同一个顺序"；视图里的排序由各自的折叠路径保证，不由本比较器证明。
/// </para>
/// </remarks>
public static class GameStateComparer
{
    /// <summary>两本账在结构上是否等价（逐字段、逐项、与集合顺序无关）。</summary>
    public static bool AreEquivalent(GameState? left, GameState? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return GroupwiseEquivalent(left.Seats, right.Seats, entry => entry.Seat, SeatEntryEquivalent)
               && left.DepartedSeats.SequenceEqual(right.DepartedSeats)
               && GroupwiseEquivalent(
                   left.DepartureRequests,
                   right.DepartureRequests,
                   request => request.Seat,
                   DepartureRequestEquivalent)
               && GroupwiseEquivalent(
                   left.PersistentEffects,
                   right.PersistentEffects,
                   effect => effect.Id,
                   PersistentEffectEquivalent)
               && GroupwiseEquivalent(
                   left.InstantaneousEffects,
                   right.InstantaneousEffects,
                   effect => effect.Id,
                   InstantaneousEffectEquivalent)
               && MultisetEquivalent(left.AbilityUses.Entries, right.AbilityUses.Entries)
               && left.Malfunctions.SinceDawnStart == right.Malfunctions.SinceDawnStart
               && MultisetEquivalent(left.Malfunctions.Entries, right.Malfunctions.Entries);
    }

    /// <summary>按业务键配对比较：键集合一致，且同键下的条目按出现顺序逐项等价。</summary>
    private static bool GroupwiseEquivalent<T, TKey>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, TKey> keyOf,
        Func<T, T, bool> equivalent)
        where TKey : notnull
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var leftGroups = GroupByKey(left, keyOf);
        var rightGroups = GroupByKey(right, keyOf);
        if (leftGroups.Count != rightGroups.Count)
        {
            return false;
        }

        foreach (var (key, leftItems) in leftGroups)
        {
            if (!rightGroups.TryGetValue(key, out var rightItems) || rightItems.Count != leftItems.Count)
            {
                return false;
            }

            for (var index = 0; index < leftItems.Count; index++)
            {
                if (!equivalent(leftItems[index], rightItems[index]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static Dictionary<TKey, List<T>> GroupByKey<T, TKey>(IReadOnlyList<T> items, Func<T, TKey> keyOf)
        where TKey : notnull
    {
        var groups = new Dictionary<TKey, List<T>>();
        foreach (var item in items)
        {
            var key = keyOf(item);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(item);
        }

        return groups;
    }

    /// <summary>多重集合比较：值相等（含重复次数），与顺序无关；用于两本账这类平坦记录。</summary>
    private static bool MultisetEquivalent<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
        where T : notnull
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var remaining = new Dictionary<T, int>();
        foreach (var item in left)
        {
            remaining[item] = remaining.GetValueOrDefault(item) + 1;
        }

        foreach (var item in right)
        {
            if (!remaining.TryGetValue(item, out var count) || count == 0)
            {
                return false;
            }

            remaining[item] = count - 1;
        }

        return true;
    }

    /// <summary>一条待批的离场申请逐字段比较（D-0037）：席位与理由都算——它们都进事件流。</summary>
    private static bool DepartureRequestEquivalent(TravellerDepartureRequest left, TravellerDepartureRequest right) =>
        left.Seat == right.Seat
        && string.Equals(left.Note, right.Note, StringComparison.Ordinal);

    private static bool SeatEntryEquivalent(SeatStateEntry left, SeatStateEntry right) =>
        left.Seat == right.Seat
        && FactEquivalent(left.Life, right.Life)
        && FactEquivalent(left.Character, right.Character)
        && FactEquivalent(left.Alignment, right.Alignment)
        && FactEquivalent(left.Drunk, right.Drunk)
        && FactEquivalent(left.Poison, right.Poison)
        && MultisetEquivalent(left.Madnesses, right.Madnesses);

    /// <summary>一条维度事实逐字段比较：值、原因、导致方、效果链接缺一不可。</summary>
    private static bool FactEquivalent<T>(StateFact<T>? left, StateFact<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return EqualityComparer<T>.Default.Equals(left.Value, right.Value)
               && string.Equals(left.Reason, right.Reason, StringComparison.Ordinal)
               && left.CausedBy == right.CausedBy
               && left.EffectId == right.EffectId;
    }

    private static bool PersistentEffectEquivalent(PersistentEffect left, PersistentEffect right) =>
        left.Id == right.Id
        && left.Source == right.Source
        && left.Ability == right.Ability
        && left.Target == right.Target
        && left.SourceCharacter == right.SourceCharacter
        && left.Dimension == right.Dimension
        && left.Window == right.Window
        && TerminationEquivalent(left.Termination, right.Termination);

    private static bool TerminationEquivalent(EffectTermination? left, EffectTermination? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Kind == right.Kind
               && string.Equals(left.Reason, right.Reason, StringComparison.Ordinal)
               && left.CausedBy == right.CausedBy;
    }

    private static bool InstantaneousEffectEquivalent(InstantaneousEffect left, InstantaneousEffect right) =>
        left.Id == right.Id
        && left.Source == right.Source
        && left.Ability == right.Ability
        && left.Target == right.Target;
}
