namespace OpenClockTower.Kernel;

/// <summary>
/// 当前占着钟盘的那一条收票（唯一「未收完」的一条）：提名 / 流放共用一个读取口。
/// </summary>
/// <remarks>
/// 口径见票据「D2 实施口径」：钟盘 =「未收完的那一条收票」。开始 / 继续收票的冲突判定与
/// 控制面节拍器都读它，避免两处各写一套「现在该收谁」的判断。收票已收完但未计票的选票
/// **不占**钟盘——计票是独立的一步，可以延后。
/// </remarks>
public sealed record ActiveBallot
{
    /// <summary>选票族。</summary>
    public required BallotKind Kind { get; init; }

    /// <summary>当天序内的序号（第 N 项提名 / 第 N 条流放）。</summary>
    public required int Index { get; init; }

    /// <summary>正在收的收票状态。</summary>
    public required VoteSweepState Sweep { get; init; }

    /// <summary>给人看的定位文本（拒绝提示与日志用）。</summary>
    public string Describe() =>
        Kind == BallotKind.Nomination ? $"第 {Index} 项提名" : $"第 {Index} 条流放";
}
