using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 配板建议：抽取出的角色袋 + 净分布 + 显式说明（修正 / 钳制）。**未绑席位**——
/// 席位映射由 Application 按席位升序完成（建议是瞬态的，不落账）。
/// </summary>
public sealed record SetupProposal(
    IReadOnlyList<CharacterId> Bag,
    SetupCounts Counts,
    IReadOnlyList<string> Notes);
