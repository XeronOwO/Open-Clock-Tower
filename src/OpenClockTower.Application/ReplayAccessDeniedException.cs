namespace OpenClockTower.Application;

/// <summary>
/// 复盘读取被可见性闸拒绝：玩家面只在结束批次（<c>GameEndedEvent</c>）之后开放（R-0043 / D-0020）。
/// </summary>
/// <remarks>
/// Server 把它翻译成 <c>HubException</c> 的**中性文案**：只说明"什么时候可以看"，
/// 不泄露任何局面信息；拒绝动作本身在 Application 侧留审计日志。
/// </remarks>
public sealed class ReplayAccessDeniedException : Exception
{
    /// <summary>构造拒绝异常。</summary>
    /// <param name="message">中性、可对玩家显示的说明。</param>
    public ReplayAccessDeniedException(string message)
        : base(message)
    {
    }
}
