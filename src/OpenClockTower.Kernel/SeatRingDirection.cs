namespace OpenClockTower.Kernel;

/// <summary>
/// 席位环上的方向：从某个席位出发沿圆桌往哪一侧数。
/// </summary>
/// <remarks>
/// 席位环 = 服务端席位名单的自然顺序（座位号升序 = 圆桌顺序，D-0008）。
/// 「顺时针 / 逆时针」在百科原文里就是这样成对使用的（百科《亡骨魔》· 2026-10-01 抓取 ·
/// 规则细节 14：「距离爪牙顺时针或逆时针最近的镇民玩家中毒」），
/// 因此平台把它记成**方向**而不是"第几个席位"：目标由方向 + 当前座次动态重算。
/// </remarks>
public enum SeatRingDirection
{
    /// <summary>顺时针：沿席位号升序方向。</summary>
    Clockwise,

    /// <summary>逆时针：沿席位号降序方向。</summary>
    CounterClockwise,
}
