namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤机状态的**结构等价**比较：用于"按事件日志重建"之后的校验（D-0014 能力 3）。
/// </summary>
/// <remarks>
/// C# record 的自动相等只比较集合成员的**引用**，所以状态里的槽位表、选项表需要逐项比较；
/// 这个比较器是恢复路径"重建前后是否一致"的判定依据。
/// </remarks>
public static class StepMachineStateComparer
{
    /// <summary>两个状态在结构上是否等价（逐字段、逐项比较）。</summary>
    public static bool AreEquivalent(StepMachineState? left, StepMachineState? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.SlotIndex == right.SlotIndex
               && left.Quota == right.Quota
               && left.Control == right.Control
               && PlanEquivalent(left.Plan, right.Plan)
               && RequestEquivalent(left.PendingRequest, right.PendingRequest)
               && DecisionEquivalent(left.AwaitingDecision, right.AwaitingDecision)
               && string.Equals(left.Block?.Reason, right.Block?.Reason, StringComparison.Ordinal)
               && DayEquivalent(left.Day, right.Day)
               && OutcomeEquivalent(left.Outcome, right.Outcome)
               && KlutzChoicesEquivalent(left.KlutzChoices, right.KlutzChoices)
               && FangGuInfectionEquivalent(left.FangGuInfection, right.FangGuInfection)
               && PitHagNightEquivalent(left.PitHagNight, right.PitHagNight)
               && BarberNightEquivalent(left.BarberNight, right.BarberNight);
    }

    /// <summary>
    /// 方古的「限一次」整局事实必须进比较器：它是"要不要侵染"的判定输入，
    /// 漏比会让重建校验在整局标记上失明（R-0034）。
    /// </summary>
    private static bool FangGuInfectionEquivalent(FangGuInfection? left, FangGuInfection? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (not null, null) or (null, not null) => false,
            ({ } a, { } b) => a.Seat == b.Seat
                && a.Source == b.Source
                && string.Equals(a.Note, b.Note, StringComparison.Ordinal),
        };

    /// <summary>麻脸巫婆之夜的裁量窗口（含待定死亡与转化载荷）进比较器：窗口状态直接决定后续裁定结果。</summary>
    private static bool PitHagNightEquivalent(PitHagNight? left, PitHagNight? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Source == right.Source
               && left.ClosesAfterSlotIndex == right.ClosesAfterSlotIndex
               && left.CasualtyAbility == right.CasualtyAbility
               && DeferredDeathsEquivalent(left.Deferred, right.Deferred);
    }

    private static bool DeferredDeathsEquivalent(
        IReadOnlyList<DeferredDeath> left,
        IReadOnlyList<DeferredDeath> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var a = left[index];
            var b = right[index];
            if (a.Target != b.Target
                || a.Source != b.Source
                || a.Ability != b.Ability
                || !string.Equals(a.Note, b.Note, StringComparison.Ordinal)
                || !TransformationEquivalent(a.Transformation, b.Transformation))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TransformationEquivalent(DeferredTransformation? left, DeferredTransformation? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Target == right.Target
               && left.Character == right.Character
               && left.Alignment == right.Alignment
               && left.Dies == right.Dies
               && string.Equals(left.Note, right.Note, StringComparison.Ordinal);
    }

    /// <summary>「今晚理发」事实（R-0033）进比较器：跨阶段保留的事实决定当夜是否还有恶魔交互。</summary>
    private static bool BarberNightEquivalent(BarberNight? left, BarberNight? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (not null, null) or (null, not null) => false,
            ({ } a, { } b) => a.Source == b.Source
                && string.Equals(a.Note, b.Note, StringComparison.Ordinal),
        };

    private static bool OutcomeEquivalent(GameOutcome? left, GameOutcome? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (not null, null) or (null, not null) => false,
            ({ } a, { } b) => a.Winner == b.Winner
                && a.Condition == b.Condition
                && string.Equals(a.Detail, b.Detail, StringComparison.Ordinal),
        };

    private static bool KlutzChoicesEquivalent(
        IReadOnlyList<KlutzChoiceRecord> left,
        IReadOnlyList<KlutzChoiceRecord> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].Klutz != right[index].Klutz
                || left[index].Target != right[index].Target
                || !string.Equals(left[index].Detail, right[index].Detail, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool OriginEquivalent(OperationRequestOrigin left, OperationRequestOrigin right) =>
        left.Kind == right.Kind
        && left.SlotId == right.SlotId
        && string.Equals(left.PlanLabel, right.PlanLabel, StringComparison.Ordinal)
        && left.SlotIndex == right.SlotIndex
        && left.TriggerAbility == right.TriggerAbility
        && string.Equals(left.TriggerReason, right.TriggerReason, StringComparison.Ordinal);

    private static bool DayEquivalent(DayState? left, DayState? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (!SeatsEquivalent(left.SpentVoteTokens, right.SpentVoteTokens) || left.Days.Count != right.Days.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Days.Count; index++)
        {
            var dayLeft = left.Days[index];
            var dayRight = right.Days[index];
            if (dayLeft.DayNumber != dayRight.DayNumber
                || dayLeft.Status != dayRight.Status
                || dayLeft.AboutToBeExecuted != dayRight.AboutToBeExecuted
                || dayLeft.Executed != dayRight.Executed
                || dayLeft.ExecutedKind != dayRight.ExecutedKind
                || dayLeft.Nominations.Count != dayRight.Nominations.Count)
            {
                return false;
            }

            for (var nominationIndex = 0; nominationIndex < dayLeft.Nominations.Count; nominationIndex++)
            {
                var nominationLeft = dayLeft.Nominations[nominationIndex];
                var nominationRight = dayRight.Nominations[nominationIndex];
                if (nominationLeft.Index != nominationRight.Index
                    || nominationLeft.Nominator != nominationRight.Nominator
                    || nominationLeft.Nominee != nominationRight.Nominee
                    || nominationLeft.Status != nominationRight.Status
                    || !SeatsEquivalent(nominationLeft.Ballot, nominationRight.Ballot))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool SeatsEquivalent(IReadOnlyList<SeatId> left, IReadOnlyList<SeatId> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool PlanEquivalent(StepPlan left, StepPlan right)
    {
        if (!string.Equals(left.Label, right.Label, StringComparison.Ordinal)
            || left.Phase != right.Phase
            || !string.Equals(left.Variant, right.Variant, StringComparison.Ordinal))
        {
            return false;
        }

        if (left.Slots.Count != right.Slots.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Slots.Count; index++)
        {
            if (!SlotEquivalent(left.Slots[index], right.Slots[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SlotEquivalent(StepSlot left, StepSlot right) =>
        left.Id == right.Id
        && left.Kind == right.Kind
        && left.Actor == right.Actor
        && PromptEquivalent(left.Prompt, right.Prompt)
        && DependenciesEquivalent(left.Dependencies, right.Dependencies);

    private static bool RequestEquivalent(OperationRequest? left, OperationRequest? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Id == right.Id
               && left.Addressee == right.Addressee
               && OriginEquivalent(left.Origin, right.Origin)
               && left.Status == right.Status
               && PromptEquivalent(left.Prompt, right.Prompt)
               && DependenciesEquivalent(left.Dependencies, right.Dependencies)
               && string.Equals(left.Answer?.OptionValue, right.Answer?.OptionValue, StringComparison.Ordinal)
               && left.Answer?.Source == right.Answer?.Source
               && string.Equals(left.Answer?.Note, right.Answer?.Note, StringComparison.Ordinal)
               && left.Voided?.Reason == right.Voided?.Reason
               && string.Equals(left.Voided?.Note, right.Voided?.Note, StringComparison.Ordinal);
    }

    private static bool DecisionEquivalent(DecisionPoint? left, DecisionPoint? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Id == right.Id && PromptEquivalent(left.Prompt, right.Prompt);
    }

    private static bool PromptEquivalent(ChoicePrompt? left, ChoicePrompt? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (!string.Equals(left.Context, right.Context, StringComparison.Ordinal)
            || left.OnNoOption != right.OnNoOption
            || left.Options.Count != right.Options.Count
            || left.SecondaryOptions.Count != right.SecondaryOptions.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Options.Count; index++)
        {
            if (!string.Equals(left.Options[index].Value, right.Options[index].Value, StringComparison.Ordinal)
                || !string.Equals(left.Options[index].Preview, right.Options[index].Preview, StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (var index = 0; index < left.SecondaryOptions.Count; index++)
        {
            if (!string.Equals(
                    left.SecondaryOptions[index].Value,
                    right.SecondaryOptions[index].Value,
                    StringComparison.Ordinal)
                || !string.Equals(
                    left.SecondaryOptions[index].Preview,
                    right.SecondaryOptions[index].Preview,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DependenciesEquivalent(IReadOnlyList<SeatDependency> left, IReadOnlyList<SeatDependency> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].Seat != right[index].Seat
                || left[index].RequiredLife != right[index].RequiredLife
                || left[index].RequiredCharacter != right[index].RequiredCharacter)
            {
                return false;
            }
        }

        return true;
    }
}
