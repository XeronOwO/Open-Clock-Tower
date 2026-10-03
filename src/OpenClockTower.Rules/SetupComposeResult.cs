namespace OpenClockTower.Rules;

/// <summary>配板求解结果：成功给建议、失败给机器可读原因（R-0041 / R-0042）。</summary>
public sealed record SetupComposeResult
{
    /// <summary>配板建议；失败时为 null。</summary>
    public SetupProposal? Proposal { get; init; }

    /// <summary>失败原因；成功时为 null。</summary>
    public Failure? Error { get; init; }

    /// <summary>求解是否成功。</summary>
    public bool Ok => Proposal is not null;

    /// <summary>求解失败的机器可读原因（Application 翻成 wire 上的 `setup.*` 串）。</summary>
    public sealed record Failure(FailureCode Code, string Message);

    /// <summary>失败码。</summary>
    public enum FailureCode
    {
        /// <summary>人数不在 5–15 的分布表内（R-0041）。</summary>
        PlayerCountUnsupported,

        /// <summary>剧本的角色池撑不起（含钳制后的）净分布（R-0042 第 3 条）。</summary>
        PoolExhausted,

        /// <summary>净分布自相矛盾，或设置调整在迭代上限内不收敛（R-0042 第 3 条）。</summary>
        DistributionConflict,
    }

    internal static SetupComposeResult Success(SetupProposal proposal) => new() { Proposal = proposal };

    internal static SetupComposeResult Failed(FailureCode code, string message) =>
        new() { Error = new Failure(code, message) };
}
