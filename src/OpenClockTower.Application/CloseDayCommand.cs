namespace OpenClockTower.Application;

/// <summary>说书人 / 宿主结束白天：处决当前「即将被处决」者（如果有），然后关闭白天。</summary>
public sealed record CloseDayCommand : GameCommand;
