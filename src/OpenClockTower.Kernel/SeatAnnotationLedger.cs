namespace OpenClockTower.Kernel;

/// <summary>
/// 注记账：这一局说书人写的全部注记（**独立派生视图**，D-0019）。
/// </summary>
/// <remarks>
/// <para>
/// 它与状态账（<see cref="GameState"/>）来自**同一条事件流**，但刻意分开：D-0015 规定状态账
/// 只记事实与归因，自由文本注记不能混进去。所以这里是第三本账（第三张派生视图），
/// 只被说书人视图读取，玩家投影里没有它（D-0012 §4.3）。
/// </para>
/// <para>
/// 顺序确定性（D-0008）：注记按发生顺序用有序列表承载，标识只增不减、永不复用。
/// </para>
/// </remarks>
public sealed record SeatAnnotationLedger
{
    /// <summary>当前仍存在的注记，按发生顺序。</summary>
    public IReadOnlyList<SeatAnnotation> Annotations { get; init; } = [];

    /// <summary>已签发过的最大标识；**只增不减**——删除过的标识也不会被再次签发。</summary>
    public int LastIssuedId { get; init; }

    /// <summary>空账（"还没有写过任何注记"是合法状态）。</summary>
    public static SeatAnnotationLedger Empty { get; } = new();

    /// <summary>下一条注记的标识（签发方用；折叠时按事件里的标识推进 <see cref="LastIssuedId"/>）。</summary>
    public SeatAnnotationId NextId => new(LastIssuedId + 1);

    /// <summary>按标识取一条注记；不存在返回 null。</summary>
    public SeatAnnotation? Find(SeatAnnotationId id) =>
        Annotations.FirstOrDefault(annotation => annotation.Id == id);

    /// <summary>某个席位现有几条注记（每席上限用）。</summary>
    public int CountOn(SeatId seat) => Annotations.Count(annotation => annotation.Seat == seat);

    /// <summary>追加一条注记，并推进签发水位。</summary>
    /// <exception cref="InvalidOperationException">同一标识重复出现（事件流顺序损坏）。</exception>
    public SeatAnnotationLedger Add(SeatAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (Find(annotation.Id) is not null)
        {
            throw new InvalidOperationException($"事件流顺序损坏：注记 {annotation.Id} 重复出现");
        }

        return this with
        {
            Annotations = [.. Annotations, annotation],
            LastIssuedId = Math.Max(LastIssuedId, annotation.Id.Value),
        };
    }

    /// <summary>原地更新一条注记（位置不变）。</summary>
    /// <exception cref="InvalidOperationException">标识不存在（事件流顺序损坏）。</exception>
    public SeatAnnotationLedger Update(SeatAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        var index = IndexOf(annotation.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"事件流顺序损坏：注记 {annotation.Id} 不存在，无法更新");
        }

        var next = new List<SeatAnnotation>(Annotations) { [index] = annotation };
        return this with
        {
            Annotations = next,
            LastIssuedId = Math.Max(LastIssuedId, annotation.Id.Value),
        };
    }

    /// <summary>移除一条注记；历史留在事件流里。</summary>
    /// <exception cref="InvalidOperationException">标识不存在（事件流顺序损坏）。</exception>
    public SeatAnnotationLedger Remove(SeatAnnotationId id)
    {
        var index = IndexOf(id);
        if (index < 0)
        {
            throw new InvalidOperationException($"事件流顺序损坏：注记 {id} 不存在，无法删除");
        }

        var next = new List<SeatAnnotation>(Annotations);
        next.RemoveAt(index);
        return this with { Annotations = next };
    }

    private int IndexOf(SeatAnnotationId id)
    {
        for (var index = 0; index < Annotations.Count; index++)
        {
            if (Annotations[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }
}
