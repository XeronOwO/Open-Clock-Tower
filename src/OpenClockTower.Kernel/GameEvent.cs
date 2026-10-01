namespace OpenClockTower.Kernel;

/// <summary>
/// 内核产出的事件：进事件流后是**唯一事实来源**（D-0010），可折叠重放、可截断撤销。
/// </summary>
/// <remarks>
/// 事件只描述"发生了什么"，不携带墙上时间——时间戳由宿主在追加时记录（D-0008：内核无时间）。
/// </remarks>
public abstract record GameEvent;
