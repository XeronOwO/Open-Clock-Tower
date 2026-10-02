using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 畸形秀演员处罚处决的依据：该席位此刻就是畸形秀演员，且其能力生效。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《畸形秀演员》· 2026-10-01 抓取 · 角色能力——「如果你"疯狂"地证明自己是外来者，
/// 你可能被处决」；· 角色简介 4/5——是否疯狂由说书人决定，可在任何时间点（含夜晚、含提名阶段之外）
/// 处决。口径见 <c>docs/standard/rulings.md</c> R-0020。
/// </para>
/// <para>
/// 畸形秀演员**没有提示标记、没有夜晚行动**：它的能力就是"说书人看着办"，因此这里只校验
/// 能力依据（角色是畸形秀演员、存活、清醒、健康）；死亡后失去能力（百科《疯狂》· 术语介绍），
/// 醉酒 / 中毒时能力不生效（百科《重要细节》三-3）。
/// </para>
/// </remarks>
internal sealed class MutantMadnessPunishment : IAdjudicatedExecutionSource
{
    /// <summary>畸形秀演员的角色标识。</summary>
    internal static readonly CharacterId Mutant = new("mutant");

    /// <summary>畸形秀演员的能力标识（它不产生效果，只用于归因与两本账口径）。</summary>
    internal static readonly AbilityId MutantAbility = new("mutant");

    /// <inheritdoc />
    public MadnessPunishmentSource Source => MadnessPunishmentSource.Mutant;

    /// <inheritdoc />
    public AdjudicatedExecutionEligibility Evaluate(GameState state, IReadOnlyList<SeatId> seats, SeatId seat)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(seats);

        var entry = state.Seat(seat);
        if (entry?.CharacterValue is not { } character)
        {
            return new AdjudicatedExecutionEligibility
            {
                Applicable = null,
                Note = $"席位 {seat.Value} 的角色还没有观测：无法判定他是不是畸形秀演员（不猜，D-0015）",
            };
        }

        if (character != Mutant)
        {
            return new AdjudicatedExecutionEligibility
            {
                Applicable = false,
                Note = $"席位 {seat.Value} 的角色是 {character.Value}，不是畸形秀演员："
                    + "不能以「畸形秀演员的疯狂处罚」处决",
            };
        }

        if (entry.LifeValue is not { } life
            || entry.DrunkValue is not { } drunk
            || entry.PoisonValue is not { } poison)
        {
            return new AdjudicatedExecutionEligibility
            {
                Applicable = null,
                Note = $"席位 {seat.Value} 的生死 / 醉酒 / 中毒还没有观测齐："
                    + "无法判定畸形秀演员的能力是否生效（不猜，D-0015）",
            };
        }

        if (life != LifeState.Alive)
        {
            return new AdjudicatedExecutionEligibility
            {
                Applicable = false,
                Note = "畸形秀演员已经死亡：死后失去能力，不再受疯狂后果约束（百科《疯狂》· 术语介绍）",
            };
        }

        if (drunk != DrunkState.Sober || poison != PoisonState.Healthy)
        {
            return new AdjudicatedExecutionEligibility
            {
                Applicable = false,
                Note = "畸形秀演员此刻醉酒 / 中毒：能力不生效，不能处罚处决（百科《重要细节》三-3）",
            };
        }

        return new AdjudicatedExecutionEligibility
        {
            Applicable = true,
            Note = "畸形秀演员的能力此刻生效：说书人可因其疯狂地证明自己是外来者而处罚处决",
            DeathReason = "mutant.madness：说书人判定他在疯狂地证明自己是外来者（R-0020）",
            CausedBy = seat,
        };
    }
}
