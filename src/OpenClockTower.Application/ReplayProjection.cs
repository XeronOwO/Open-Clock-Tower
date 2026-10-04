using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 复盘投影：把整条事件流按"一条事件一个步骤"折成可回放的步骤序列（D-0020）。
/// </summary>
/// <remarks>
/// <para>
/// 复用与实时视图同源的折叠器（<see cref="StepMachine.Apply"/> / <see cref="GameStateMachine.Apply"/>）：
/// 顺序 = 事件序号；服务端按需从持久化事件流重建，不建第二账本、不依赖宿主内存态（R-0043 第 4 条）。
/// </para>
/// <para>
/// 每次调用都会从序号 0 折到最新——单机小规模下这是明账；分页只影响返回窗口，不影响"按序号重建"
/// 的正确性（将来若要优化，先读快照再折，不改变视图形状）。
/// </para>
/// </remarks>
public static class ReplayProjection
{
    /// <summary>单页最大步骤数（防客户端一次拉爆；超出按 <c>afterSequence</c> 翻页）。</summary>
    public const int MaxPageSize = 500;

    /// <summary>默认页大小。</summary>
    public const int DefaultPageSize = 200;

    /// <summary>
    /// 构建一页复盘视图：返回序号大于 <paramref name="afterSequence"/> 的前 <paramref name="pageSize"/> 步。
    /// </summary>
    /// <param name="events">整条事件流（按序号升序；实现按给定顺序折叠）。</param>
    /// <param name="afterSequence">客户端已拿到的最大序号；首次请传 0。</param>
    /// <param name="pageSize">本页最多返回多少步（钳制在 1..<see cref="MaxPageSize"/>）。</param>
    public static ReplayView Build(
        IReadOnlyList<StoredEvent> events,
        long afterSequence,
        int pageSize = DefaultPageSize)
    {
        ArgumentNullException.ThrowIfNull(events);
        var limit = Math.Clamp(pageSize, 1, MaxPageSize);

        var steps = new List<ReplayStep>();
        StepMachineState? machine = null;
        var state = GameState.Empty;
        var lastSequence = 0L;
        var ended = false;

        foreach (var stored in events)
        {
            var machineBefore = machine;
            var stateBefore = state;
            machine = StepMachine.Apply(machine, stored.Event);
            state = GameStateMachine.Apply(state, stored.Event);
            lastSequence = stored.Sequence;
            ended |= stored.Event is GameEndedEvent;

            if (ReplayStepCatalog.IsExcluded(stored.Event.GetType()))
            {
                continue;
            }

            steps.Add(ReplayStepCatalog.Present(new ReplayStepContext
            {
                Stored = stored,
                MachineBefore = machineBefore,
                MachineAfter = machine,
                StateBefore = stateBefore,
                StateAfter = state,
            }));
        }

        var startIndex = 0;
        while (startIndex < steps.Count && steps[startIndex].Sequence <= afterSequence)
        {
            startIndex++;
        }

        var page = new List<ReplayStep>(limit);
        for (var index = startIndex; index < steps.Count && page.Count < limit; index++)
        {
            page.Add(steps[index]);
        }

        return new ReplayView
        {
            Sequence = lastSequence,
            Ended = ended,
            HasMore = startIndex + page.Count < steps.Count,
            Steps = page,
        };
    }
}
