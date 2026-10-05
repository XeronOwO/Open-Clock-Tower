using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 候选事实求值读的那一面世界（R-0057-C）：座次 + 状态账 + 常用派生读数。
/// </summary>
/// <remarks>
/// <para>
/// 全部读数都是**只读派生**、不缓存状态；判不了的一律返回 null 或空集合，由每条事实自己决定
/// "算不出来就别进候选"（D-0015：不猜）。
/// </para>
/// <para>
/// 「全知」类判断（例如"场上有旅行者"要说成假）必须**所有在局席位的该维度都观测齐**才成立：
/// 少一个人的角色，就不敢替他保证"没有"。
/// </para>
/// <para>
/// 圆桌只算**在局**席位：离场的旅行者已经不在镇上（R-0044 第 6 条），不占相邻位、不计人数。
/// </para>
/// </remarks>
internal sealed record SavantFactWorld
{
    /// <summary>当前状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>本局在局座次（按座位号升序 = 圆桌顺序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>圆桌上的席位（离场者剔除），顺序不变。</summary>
    internal IReadOnlyList<SeatId> Circle => [.. Seats.Where(seat => !State.HasDeparted(seat))];

    /// <summary>该席位的角色；没观测到返回 null。</summary>
    internal CharacterId? CharacterOf(SeatId seat) => State.Seat(seat)?.CharacterValue;

    /// <summary>该席位的角色类型（剧本花名册）；角色未观测或名册不认这个 slug 时返回 null。</summary>
    internal CharacterType? TypeOf(SeatId seat) =>
        CharacterOf(seat) is { } character ? SectsAndVioletsRoster.TypeOf(character) : null;

    /// <summary>该席位的阵营；没观测到返回 null。</summary>
    internal Alignment? AlignmentOf(SeatId seat) => State.Seat(seat)?.Alignment?.Value;

    /// <summary>该席位的生死；没观测到返回 null。</summary>
    internal LifeState? LifeOf(SeatId seat) => State.Seat(seat)?.LifeValue;

    /// <summary>所有在局席位的角色都观测齐了吗（说"没有某某角色"的前提）。</summary>
    internal bool AllCharactersKnown => Circle.All(seat => CharacterOf(seat) is not null);

    /// <summary>所有在局席位的阵营都观测齐了吗。</summary>
    internal bool AllAlignmentsKnown => Circle.All(seat => AlignmentOf(seat) is not null);

    /// <summary>所有在局席位的生死都观测齐了吗。</summary>
    internal bool AllLivesKnown => Circle.All(seat => LifeOf(seat) is not null);

    /// <summary>已知角色类型为 <paramref name="type"/> 的席位（按座位号升序）。</summary>
    internal IReadOnlyList<SeatId> SeatsOfType(CharacterType type) =>
        [.. Circle.Where(seat => TypeOf(seat) == type)];

    /// <summary>存活席位（生死未观测的不在内）。</summary>
    internal IReadOnlyList<SeatId> AliveSeats => [.. Circle.Where(seat => LifeOf(seat) == LifeState.Alive)];

    /// <summary>按阵营取存活席位（生死或阵营未观测的不在内）。</summary>
    internal IReadOnlyList<SeatId> AliveOf(Alignment alignment) =>
        [.. AliveSeats.Where(seat => AlignmentOf(seat) == alignment)];

    /// <summary>
    /// 唯一的恶魔席位；一名都没有、或不止一名（麻脸巫婆造得出第二个恶魔）时返回 null——不猜。
    /// </summary>
    internal SeatId? SingleDemon
    {
        get
        {
            var demons = SeatsOfType(CharacterType.Demon);
            return demons.Count == 1 ? demons[0] : null;
        }
    }

    /// <summary>圆桌上某席位的相邻席位（左右各一；只有两席时同一人，按序去重）。</summary>
    internal IReadOnlyList<SeatId> NeighboursOf(SeatId seat)
    {
        var circle = Circle;
        var index = IndexIn(circle, seat);
        if (index < 0 || circle.Count < 2)
        {
            return [];
        }

        var left = circle[(index + circle.Count - 1) % circle.Count];
        var right = circle[(index + 1) % circle.Count];
        return left == right ? [right] : [left, right];
    }

    /// <summary>
    /// 圆桌上两席位之间**隔着几名玩家**（两条弧里较短的一条；相邻 = 0，隔一个 = 1）。
    /// 同一席位或不在圆桌上时返回 null。
    /// </summary>
    internal int? GapBetween(SeatId left, SeatId right)
    {
        if (left == right)
        {
            return null;
        }

        var circle = Circle;
        var leftIndex = IndexIn(circle, left);
        var rightIndex = IndexIn(circle, right);
        if (leftIndex < 0 || rightIndex < 0)
        {
            return null;
        }

        var steps = Math.Abs(leftIndex - rightIndex);
        return Math.Min(steps, circle.Count - steps) - 1;
    }

    /// <summary>圆桌上一对席位里较短弧的最大"隔着几名玩家"（枚举 gap 参数时空集不出现）。</summary>
    internal int MaxGap => Math.Max(0, (Circle.Count - 2) / 2);

    /// <summary>
    /// 圆桌上两席位之间的最大**距离**（钟表匠口径 = 隔着的人数 + 1）——博学者的
    /// <c>demon-minion-distance</c> 据此枚举取值（与 <see cref="MaxGap"/> 差 1，两处口径都要留着）。
    /// </summary>
    internal int MaxDistance => MaxGap + 1;

    private static int IndexIn(IReadOnlyList<SeatId> seats, SeatId seat)
    {
        for (var index = 0; index < seats.Count; index++)
        {
            if (seats[index] == seat)
            {
                return index;
            }
        }

        return -1;
    }
}
