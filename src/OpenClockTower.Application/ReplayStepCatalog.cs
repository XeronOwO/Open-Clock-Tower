using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 复盘步骤目录：事件类型 → 投影器 的只读路由表 + 显式排除清单。
/// </summary>
/// <remarks>
/// <para>
/// D-0020：每个 <c>GameEvent</c> 类型必须被**恰好一个**投影器认领，或在排除清单里（附规则引用）；
/// 未登记类型一律显式抛错（不静默跳过），覆盖率由门禁测试锁死——新增事件类型忘了登记会红。
/// </para>
/// <para>
/// 排除项只有两类：说书人注记（D-0019：自由文本永远只说书人可见，任何形态的复盘都不呈现）与
/// 步骤机节拍内部事件（`SlotEntered` / `SlotQuotaElapsed` / `SlotAdvanced` / `SlotForceAdvanced`——
/// D-0013 / D-0014 的驱动机制，不是"对局里发生了什么"）。
/// </para>
/// </remarks>
public static class ReplayStepCatalog
{
    private static readonly IReadOnlyList<IReplayStepPresenter> Presenters =
    [
        new PhaseReplayPresenter(),
        new SlotReplayPresenter(),
        new ChoiceReplayPresenter(),
        new AbilityReplayPresenter(),
        new StateReplayPresenter(),
        new DayReplayPresenter(),
        new TriggerReplayPresenter(),
        new ControlReplayPresenter(),
        new OutcomeReplayPresenter(),
        new TravellerReplayPresenter(),
    ];

    /// <summary>显式排除清单（附规则引用；改它必须同时改门禁测试）。</summary>
    private static readonly HashSet<Type> ExcludedTypes =
    [
        // 说书人注记（D-0019）：只说书人可见，玩家永不下发；复盘也不呈现。
        typeof(SeatAnnotationAddedEvent),
        typeof(SeatAnnotationUpdatedEvent),
        typeof(SeatAnnotationRemovedEvent),

        // 步骤机节拍内部事件（D-0013 / D-0014）：配额与推进的驱动机制，不是对局事实。
        typeof(SlotEnteredEvent),
        typeof(SlotQuotaElapsedEvent),
        typeof(SlotAdvancedEvent),
        typeof(SlotForceAdvancedEvent),
    ];

    private static readonly IReadOnlyDictionary<Type, IReplayStepPresenter> ByType = BuildRoutes();

    /// <summary>该事件类型是否被显式排除（不进时间轴）。</summary>
    public static bool IsExcluded(Type eventType) => ExcludedTypes.Contains(eventType);

    /// <summary>投影一条事件；未登记类型显式抛错（D-0020：禁止默认丢弃）。</summary>
    public static ReplayStep Present(ReplayStepContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var type = context.Stored.Event.GetType();
        if (ByType.TryGetValue(type, out var presenter))
        {
            return presenter.Present(context);
        }

        throw new InvalidOperationException(
            $"复盘步骤目录没有登记事件类型 {type.Name}：要么让某个投影器认领它，要么写进排除清单并给规则依据（D-0020）");
    }

    /// <summary>被投影器认领的事件类型（覆盖率门禁用）。</summary>
    public static IReadOnlyCollection<Type> HandledTypes => [.. ByType.Keys];

    /// <summary>被显式排除的事件类型（覆盖率门禁用）。</summary>
    public static IReadOnlyCollection<Type> ExcludedTypeList => ExcludedTypes;

    private static IReadOnlyDictionary<Type, IReplayStepPresenter> BuildRoutes()
    {
        var routes = new Dictionary<Type, IReplayStepPresenter>();
        foreach (var presenter in Presenters)
        {
            foreach (var type in presenter.HandledTypes)
            {
                if (!routes.TryAdd(type, presenter))
                {
                    throw new InvalidOperationException(
                        $"复盘步骤目录重复认领事件类型 {type.Name}（D-0020：恰好一个投影器）");
                }

                if (ExcludedTypes.Contains(type))
                {
                    throw new InvalidOperationException(
                        $"复盘步骤目录把排除项 {type.Name} 又认领了一次（D-0020：排除项与投影器互斥）");
                }
            }
        }

        return routes;
    }
}
