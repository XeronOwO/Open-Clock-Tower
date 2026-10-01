namespace OpenClockTower.Kernel;

/// <summary>
/// 一次响应的来源：玩家本人，还是说书人代填。
/// </summary>
public enum ResponseSource
{
    /// <summary>玩家本人响应。</summary>
    Player,

    /// <summary>说书人在玩家卡住时代填（D-0011 代价条款，必须可审计）。</summary>
    StorytellerProxy,
}
