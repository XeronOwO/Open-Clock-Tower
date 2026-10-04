using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>说书人 / 宿主开启一个夜晚阶段：服务端按规则表建表，客户端不提供计划。</summary>
/// <remarks>
/// <para>
/// 口径（原本 / 推荐）是引擎输入，并记录进计划的 <c>Variant</c>（R-0014 要求"本局实际口径"有处可查）；
/// 默认取官方魔典行动顺序 <see cref="NightOrderVariant.Recommended"/>（R-0014，2026-10-04 定案）；
/// 显式传 <see cref="NightOrderVariant.Original"/> 仍可按剧本页顺序结算。
/// </para>
/// <para>
/// 命令只带「第几夜 + 哪套口径」；计划的构造与校验都在服务端（<c>NightPlanBuilder</c>）。
/// </para>
/// </remarks>
public sealed record StartNightCommand : GameCommand
{
    /// <summary>夜晚序号：1 = 首夜。</summary>
    public required int NightNumber { get; init; }

    /// <summary>夜晚顺序口径；默认推荐口径 = 官方魔典顺序（R-0014）。</summary>
    public NightOrderVariant Variant { get; init; } = NightOrderVariant.Recommended;
}
