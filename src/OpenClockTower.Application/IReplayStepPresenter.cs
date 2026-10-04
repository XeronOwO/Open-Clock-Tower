namespace OpenClockTower.Application;

/// <summary>
/// 一个事件族的复盘步骤投影器：把事件翻成"这一步发生了什么"。
/// </summary>
/// <remarks>
/// 注册表见 <see cref="ReplayStepCatalog"/>：每个 <c>GameEvent</c> 类型必须被恰好一个投影器认领，
/// 或在显式排除清单里（D-0020），覆盖率由门禁测试锁死——新增事件类型忘了登记会红。
/// </remarks>
internal interface IReplayStepPresenter
{
    /// <summary>本投影器认领的事件类型。</summary>
    IReadOnlyList<Type> HandledTypes { get; }

    /// <summary>投影一个步骤。</summary>
    ReplayStep Present(ReplayStepContext context);
}
