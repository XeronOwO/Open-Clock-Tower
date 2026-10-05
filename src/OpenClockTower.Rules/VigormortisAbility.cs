using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 亡骨魔（<c>vigormortis</c>）的标识、效果标识派生与文案：除首个夜晚外每夜击杀一名玩家；
/// 被他杀死的爪牙保留能力，且该爪牙一侧的邻近镇民中毒。
/// </summary>
/// <remarks>
/// <para>
/// 来源（均为钟楼百科 · 2026-10-01 抓取）：《亡骨魔》· 角色能力——「每个夜晚*，你要选择一名玩家：
/// 他死亡。」「被你杀死的爪牙保留他的能力，且与他邻近的两名镇民之一中毒。[-1外来者]」；
/// · 规则细节 11–24（夜序、标记的放置条件与移除时机、中毒标记的动态检测）；
/// · 角色简介 1–9（爪牙死后仍能行动、说书人选侧、所有被杀的爪牙都保留能力、变成非爪牙即失效、
/// 醉酒 / 中毒期间失效）；旁证《死后能力保留》· 能力简介——这类能力「生效与否不关注玩家的生死状态」。
/// </para>
/// <para>
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0056：击杀事实（含说书人选的中毒侧）记进状态账，
/// 两条持续型效果由常驻来源按它派生——保留能力窗口落在爪牙身上（不随黄昏到期），
/// 中毒落在**选定那一侧**最近的镇民身上（席位或角色变化时按同侧重算）。
/// </para>
/// </remarks>
internal static class VigormortisAbility
{
    /// <summary>角色标识。</summary>
    internal static readonly CharacterId Character = new("vigormortis");

    /// <summary>夜间击杀的能力标识（进结算账与击杀效果的归因）。</summary>
    internal static readonly AbilityId KillAbility = new("vigormortis");

    /// <summary>
    /// 「保留能力 + 邻近镇民中毒」的能力标识：两条持续型效果都挂在它名下，
    /// 由 <see cref="VigormortisRetentionSource"/> 独占维护（D-0015 的单一写入方）。
    /// </summary>
    internal static readonly AbilityId RetentionAbility = new("vigormortis.retention");

    /// <summary>说书人选择的中毒侧存进事实时的取值（决策点的选项值）。</summary>
    internal static string SideValue(SeatRingDirection side) => side switch
    {
        SeatRingDirection.Clockwise => "clockwise",
        SeatRingDirection.CounterClockwise => "counter-clockwise",
        _ => throw new InvalidOperationException($"未知的中毒侧：{side}"),
    };

    /// <summary>解析说书人选的中毒侧；认不出的取值不猜，返回 null 由调用方显式失败。</summary>
    internal static SeatRingDirection? ParseSide(string? value) => value switch
    {
        "clockwise" => SeatRingDirection.Clockwise,
        "counter-clockwise" => SeatRingDirection.CounterClockwise,
        _ => null,
    };

    /// <summary>「保留能力」窗口的效果标识：由（亡骨魔，爪牙）派生，契约与常驻来源共用这一处。</summary>
    internal static EffectId RetainEffectId(SeatId demon, SeatId minion) =>
        new($"standing:{RetentionAbility.Value}:{demon.Value}:{minion.Value}");

    /// <summary>
    /// 「中毒」效果标识：由（亡骨魔，爪牙，**当前**中毒席位）派生——目标随座次变化重算时
    /// 标识随之改变，旧的按标识找不回、由对账终止，新的落下（与诺-达鲺同款）。
    /// </summary>
    internal static EffectId PoisonEffectId(SeatId demon, SeatId minion, SeatId target) =>
        new($"standing:{RetentionAbility.Value}:{demon.Value}:{minion.Value}:poison:{target.Value}");

    /// <summary>「保留能力」窗口效果：来源 = 亡骨魔、目标 = 被他杀死的爪牙。</summary>
    /// <remarks>
    /// 生效判定走来源状态（R-0012：亡骨魔醉酒 / 中毒时挂起，恢复后继续生效），
    /// 因此不声明 <see cref="PersistentEffect.SourceStateIndependent"/>——
    /// 「如果亡骨魔死亡或失去能力，受亡骨魔影响的中毒玩家恢复健康」（规则细节 4）正是这一路的自然结果。
    /// </remarks>
    internal static PersistentEffect RetainEffect(SeatId demon, SeatId minion) => new()
    {
        Id = RetainEffectId(demon, minion),
        Source = demon,
        Ability = RetentionAbility,
        Target = minion,
        SourceCharacter = Character,
        Window = EffectWindowKind.RetainedAbility,
    };

    /// <summary>击杀说明（进状态变化事实与审计）。</summary>
    internal static string KillReason(SeatId source, SeatId target) =>
        $"亡骨魔夜间击杀（{source.Value} 号 → {target.Value} 号）";

    /// <summary>保留能力说明（进事件流与说书人视图）。</summary>
    internal static string RetentionNote(SeatId demon, SeatId minion) =>
        $"亡骨魔（{demon.Value} 号）杀死了爪牙 {minion.Value} 号：他保留自己的角色能力，"
        + "只要亡骨魔还握有该能力、且他仍是爪牙角色，连夜晚行动也照常（R-0056）";
}
