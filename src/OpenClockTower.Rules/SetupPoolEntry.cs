using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>配板用的一枚角色标记：角色 + 它的设置调整（没有修正时为空表）。</summary>
public sealed record SetupPoolEntry(CharacterId Character, IReadOnlyList<SetupAdjustment> Adjustments);
