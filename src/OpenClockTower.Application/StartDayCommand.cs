namespace OpenClockTower.Application;

/// <summary>说书人 / 宿主开启白天：天数由服务端按已开始的白天数推导，客户端不提供计划。</summary>
/// <remarks>
/// 依据百科《规则概要》三 · 2026-10-01 抓取：首个夜晚之后进入白天。
/// 计划形状（唯一的 DayWindow 槽位）由内核校验，命令本身不接受任何计划参数（D-0012）。
/// </remarks>
public sealed record StartDayCommand : GameCommand;
