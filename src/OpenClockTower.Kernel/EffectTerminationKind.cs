namespace OpenClockTower.Kernel;

/// <summary>
/// 持续型效果的终止原因分类。
/// </summary>
/// <remarks>
/// 依据百科《术语汇总》「死亡」与《重要细节》二-7：来源死亡、或来源换了角色而失去原角色能力时，
/// 「其角色能力所产生的任何持续性的效果也会立即终止」。说书人强制作废属于 D-0014 的兜底能力。
/// 终止**不可逆**：来源之后复活或以新角色回来，已终止的效果也不恢复。
/// </remarks>
public enum EffectTerminationKind
{
    /// <summary>来源死亡。</summary>
    SourceDied = 0,

    /// <summary>来源的角色发生变化，原角色能力不再存在。</summary>
    SourceLostAbility,

    /// <summary>说书人强制作废（D-0014 兜底）。</summary>
    StorytellerVoided,
}
