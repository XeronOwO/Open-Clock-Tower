namespace OpenClockTower.Kernel;

/// <summary>当天额外提名窗口的两种状态（R-0050）。</summary>
public enum ExtraNominationWindowStatus
{
    /// <summary>窗口开着：授予席位还可以发起一次额外提名。</summary>
    Open,

    /// <summary>窗口已被用掉：额外提名已经发生，不再受理第二次（R-0050 第 2 条）。</summary>
    Used,
}
