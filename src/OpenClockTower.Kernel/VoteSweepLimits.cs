namespace OpenClockTower.Kernel;

/// <summary>
/// 钟盘收票的节奏参数边界（R-0017 目标形态）：只约束呈现节奏，不参与任何判定。
/// </summary>
/// <remarks>
/// 合法性闸与内核用同一把尺子（<c>CommandGatePipeline</c> 引用这里），避免"闸放行、内核另有一套"。
/// 参数范围写在一处，回放时内核也能自证事件里的参数没有越界。
/// </remarks>
public static class VoteSweepLimits
{
    /// <summary>倒计时默认值（毫秒）：需求方 2026-10-04 口径。</summary>
    public const int DefaultCountdownMilliseconds = 3000;

    /// <summary>逐席间隔默认值（毫秒）：需求方 2026-10-04 口径。</summary>
    public const int DefaultIntervalMilliseconds = 1000;

    /// <summary>倒计时下界（毫秒）。</summary>
    public const int MinCountdownMilliseconds = 1000;

    /// <summary>倒计时上界（毫秒）。</summary>
    public const int MaxCountdownMilliseconds = 10000;

    /// <summary>逐席间隔下界（毫秒）。</summary>
    public const int MinIntervalMilliseconds = 300;

    /// <summary>逐席间隔上界（毫秒）。</summary>
    public const int MaxIntervalMilliseconds = 5000;

    /// <summary>倒计时是否在允许范围内。</summary>
    public static bool IsCountdownValid(int milliseconds) =>
        milliseconds is >= MinCountdownMilliseconds and <= MaxCountdownMilliseconds;

    /// <summary>逐席间隔是否在允许范围内。</summary>
    public static bool IsIntervalValid(int milliseconds) =>
        milliseconds is >= MinIntervalMilliseconds and <= MaxIntervalMilliseconds;
}
