using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 旅行者**向说书人提出离场申请**（本批 D-0037）：玩家发起，等说书人裁定。
/// </summary>
/// <remarks>
/// <para>
/// 平台口径：离开流程由说书人主持（百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式），
/// 但"谁先说出口"由旅行者自己决定——本命令只登记申请，**不改变任何席位状态**；
/// 批准与执行在 <see cref="ResolveTravellerDepartureCommand"/>（与"操作请求"方向相反：
/// 那个是服务端问玩家，这个是玩家问说书人）。
/// </para>
/// <para>
/// 命令面**不带席位**：申请者由连接凭据推导（D-0012：客户端声明一律不认），
/// 服务端只接受"这条连接自己那一席"的申请。
/// </para>
/// </remarks>
public sealed record RequestTravellerDepartureCommand : GameCommand
{
    /// <summary>旅行者给出的理由（自由文本；可空）。</summary>
    public string? Note { get; init; }
}
