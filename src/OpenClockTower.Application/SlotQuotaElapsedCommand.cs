namespace OpenClockTower.Application;

/// <summary>
/// 当前槽位的最短配额已走完（由宿主节拍器按服务端时钟判定后送入）。
/// </summary>
/// <remarks>
/// 时间不是内核输入来源（D-0008）：这个命令就是"时间到点"的翻译结果。
/// </remarks>
public sealed record SlotQuotaElapsedCommand : GameCommand;
