using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 一条「设置调整」（角色标记上的 `[...]`）：把某个角色类型的初始数量加减一个整数。
/// </summary>
/// <remarks>
/// 口径见 <c>docs/standard/rulings.md</c> R-0042：同类修正**先加总**，再按剧本池与人数约束钳制，
/// 缺额默认由镇民补偿（「+1 外来者」= 外来者 +1、镇民 −1）。本类型只表达**固定值**修正；
/// 范围型（「+0~1 外来者」）与特殊设置方式不在首版范围内（R-0042 第 4 条）。
/// </remarks>
public sealed record SetupAdjustment(CharacterType Type, int Delta);
