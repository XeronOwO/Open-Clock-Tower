namespace OpenClockTower.Server;

/// <summary>一次限速判定的结果：放行，或者拒绝并给出"多久之后再试"。</summary>
/// <param name="Allowed">是否放行。</param>
/// <param name="RetryAfterSeconds">拒绝时建议的重试间隔（秒）；放行时为 0。</param>
public readonly record struct ThrottleDecision(bool Allowed, int RetryAfterSeconds)
{
    /// <summary>放行。</summary>
    public static ThrottleDecision Allow { get; } = new(true, 0);

    /// <summary>按剩余窗口时间拒绝；至少 1 秒，免得给用户看"请 0 秒后再试"。</summary>
    public static ThrottleDecision Deny(double remainingSeconds) =>
        new(false, Math.Max(1, (int)Math.Ceiling(remainingSeconds)));
}
