namespace OpenClockTower.Kernel;

/// <summary>操作请求的来源类别。</summary>
public enum OperationRequestOriginKind
{
    /// <summary>槽位来源：由步骤机的计划槽位开出。</summary>
    Slot,

    /// <summary>触发来源：由事件触发型能力开出（不占槽位、不消耗配额）。</summary>
    Trigger,
}
