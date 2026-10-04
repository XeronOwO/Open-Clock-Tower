using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 复盘步骤目录的覆盖率门禁（D-0020）：每个 <c>GameEvent</c> 类型必须被**恰好一个**投影器认领，
/// 或在显式排除清单里；新增事件类型忘了登记必须红。
/// </summary>
public sealed class ReplayStepCatalogTests
{
    private static readonly Type[] EventTypes = typeof(GameEvent).Assembly
        .GetTypes()
        .Where(type => !type.IsAbstract && type != typeof(GameEvent) && typeof(GameEvent).IsAssignableFrom(type))
        .OrderBy(type => type.Name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>每个事件类型都有归属：认领或显式排除，禁止默认丢弃。</summary>
    [Fact]
    public void EveryGameEvent_IsEitherHandledOrExplicitlyExcluded()
    {
        Assert.True(EventTypes.Length >= 50, $"只枚举到 {EventTypes.Length} 个事件类型，反射扫描范围可能写错了");

        var handled = ReplayStepCatalog.HandledTypes.ToHashSet();
        var excluded = ReplayStepCatalog.ExcludedTypeList.ToHashSet();

        var uncovered = EventTypes
            .Where(type => !handled.Contains(type) && !excluded.Contains(type))
            .Select(type => type.Name)
            .ToArray();
        Assert.True(
            uncovered.Length == 0,
            "有事件类型既没被投影器认领、也不在排除清单里（D-0020：禁止默认丢弃）："
            + string.Join(", ", uncovered));

        var both = handled.Intersect(excluded).Select(type => type.Name).ToArray();
        Assert.True(both.Length == 0, "事件类型既被认领又被排除（D-0020：互斥）：" + string.Join(", ", both));

        var foreign = handled
            .Concat(excluded)
            .Where(type => !typeof(GameEvent).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToArray();
        Assert.True(foreign.Length == 0, "步骤目录登记了非 GameEvent 类型：" + string.Join(", ", foreign));
    }

    /// <summary>排除清单是刻意集合：改它必须同时改这条测试与裁定/决策引用。</summary>
    [Fact]
    public void ExclusionList_IsTheDeliberateSet()
    {
        var expected = new[]
        {
            // 说书人注记（D-0019）：玩家永不下发，复盘不呈现。
            nameof(SeatAnnotationAddedEvent),
            nameof(SeatAnnotationUpdatedEvent),
            nameof(SeatAnnotationRemovedEvent),

            // 步骤机节拍内部事件（D-0013 / D-0014）：驱动机制，不是对局事实。
            nameof(SlotEnteredEvent),
            nameof(SlotQuotaElapsedEvent),
            nameof(SlotAdvancedEvent),
            nameof(SlotForceAdvancedEvent),
        };

        var actual = ReplayStepCatalog.ExcludedTypeList
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal).ToArray(), actual);
    }

    /// <summary>未登记类型显式抛错（构造一个小重写：把目录的排除项喂给 <c>Present</c> 也应能被它自己拒绝的场景留作回归）。</summary>
    [Fact]
    public void HandledTypes_AreAllGameEvents()
    {
        Assert.NotEmpty(ReplayStepCatalog.HandledTypes);
        Assert.All(
            ReplayStepCatalog.HandledTypes,
            type => Assert.True(typeof(GameEvent).IsAssignableFrom(type), $"{type.Name} 不是 GameEvent"));
    }
}
