using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 每步摘要投影：把步骤机当前槽位与状态账折算成「这一步为什么是这样」（票据第 3 条）。
/// </summary>
/// <remarks>
/// 能力判定优先用本槽位的已结算结论；未结算时用与结算同一个
/// <see cref="AbilityEffectivenessEvaluator"/> 按当前账预览；账不全给
/// <see cref="SlotAbilityBasis.Unknown"/>，不猜（D-0015）。
/// </remarks>
public static class StepDigestProjection
{
    /// <summary>构造当前槽位的每步摘要；没有角色行动槽位时返回 null。</summary>
    /// <param name="machine">步骤机状态；未开局 / 计划走完为 null。</param>
    /// <param name="state">状态账。</param>
    /// <param name="slotAbility">本槽位角色的能力标识（由能力契约目录解析；取不到为 null）。</param>
    /// <param name="slotResolution">本槽位已结算的能力结论；还没结算为 null。</param>
    public static StepDigest? Build(
        StepMachineState? machine,
        GameState state,
        AbilityId? slotAbility,
        AbilityResolutionSnapshot? slotResolution)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (machine?.CurrentSlot is not { Kind: StepSlotKind.Action, Actor: { } actor } slot)
        {
            return null;
        }

        var actorState = state.Seat(actor);
        return new StepDigest
        {
            Seat = actor,
            Character = slot.Owner,
            State = actorState,
            Ability = BuildAbility(state, actorState, slotAbility, slotResolution),
            OptionCount = slot.Prompt?.Options.Count,
            OnNoOption = slot.Prompt?.OnNoOption,
        };
    }

    private static SlotAbilitySnapshot BuildAbility(
        GameState state,
        SeatStateEntry? actorState,
        AbilityId? slotAbility,
        AbilityResolutionSnapshot? slotResolution)
    {
        // 已结算结论优先：它才是引擎当时真正记下来的判定（预览可能被之后的账变化改写）。
        if (slotResolution is { } settled)
        {
            return new SlotAbilitySnapshot
            {
                Basis = SlotAbilityBasis.Settled,
                Ability = settled.Ability,
                Effective = settled.Effective,
                Malfunctions = settled.Malfunctions,
                Note = settled.Note,
                Sequence = settled.Sequence,
            };
        }

        // 未结算：按当前账给预览；账不全一律 Unknown，不映射成默认值（D-0015）。
        if (actorState is null || AbilityEffectivenessEvaluator.Evaluate(state, actorState) is not { } outcome)
        {
            return new SlotAbilitySnapshot
            {
                Basis = SlotAbilityBasis.Unknown,
                Ability = slotAbility,
                Note = "该席位的生死 / 醉酒 / 中毒还没有观测齐，无法判定（D-0015）",
            };
        }

        return new SlotAbilitySnapshot
        {
            Basis = SlotAbilityBasis.Preview,
            Ability = slotAbility,
            Effective = outcome.Effective,
            Malfunctions = outcome.Malfunctions,
            Note = outcome.Note,
        };
    }
}
