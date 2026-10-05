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
               && left.SlotPass == right.SlotPass
               && left.SlotAbilityResolved == right.SlotAbilityResolved
               && PlanEquivalent(left.Plan, right.Plan)
               && RequestEquivalent(left.PendingRequest, right.PendingRequest)
               && DecisionEquivalent(left.AwaitingDecision, right.AwaitingDecision)
               && left.AwaitingDecisionTriggerAbility == right.AwaitingDecisionTriggerAbility
               && left.AwaitingDecisionSeat == right.AwaitingDecisionSeat
               && string.Equals(left.Block?.Reason, right.Block?.Reason, StringComparison.Ordinal)
               && DayEquivalent(left.Day, right.Day)
               && OutcomeEquivalent(left.Outcome, right.Outcome)
               && KlutzChoicesEquivalent(left.KlutzChoices, right.KlutzChoices)
               && FangGuInfectionEquivalent(left.FangGuInfection, right.FangGuInfection)
               && PitHagNightEquivalent(left.PitHagNight, right.PitHagNight)
               && BarberNightEquivalent(left.BarberNight, right.BarberNight)
               && SageNightEquivalent(left.SageNight, right.SageNight)
               && SweetheartSkipsEquivalent(left.SweetheartSkips, right.SweetheartSkips)
               && ArtistQuestionEquivalent(left.ArtistQuestion, right.ArtistQuestion)
               && SavantQuestionEquivalent(left.SavantQuestion, right.SavantQuestion)
               && left.SavantAskedSeat == right.SavantAskedSeat;
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
                || !TransformationEquivalent(a.Transformation, b.Transformation)
                || !RetentionEquivalent(a.Retention, b.Retention))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>「保留能力」载荷（R-0056）进比较器：待定死亡表是否一致也取决于它。</summary>
    private static bool RetentionEquivalent(DeferredRetention? left, DeferredRetention? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Side == right.Side
               && PersistentEffectEquivalent(left.RetainEffect, right.RetainEffect);
    }

    /// <summary>
    /// 两条持续型效果是否等价：逐字段比（含终止事实）。
    /// 与状态账的比较同一把尺子——金额相同但归因不同的效果不是同一条效果。
    /// </summary>
    private static bool PersistentEffectEquivalent(PersistentEffect left, PersistentEffect right) =>
        left.Id == right.Id
        && left.Source == right.Source
        && left.Ability == right.Ability
        && left.Target == right.Target
        && left.SourceCharacter == right.SourceCharacter
        && left.GrantedCharacter == right.GrantedCharacter
        && left.Dimension == right.Dimension
        && left.Window == right.Window
        && left.SourceStateIndependent == right.SourceStateIndependent
        && (left.Termination is null) == (right.Termination is null);

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

    /// <summary>
    /// 贤者事实（R-0038）进比较器：击杀者、击杀时角色与生效判定都是"当晚要不要开展示"的输入，
    /// 漏比会让重建校验在这一族上失明。
    /// </summary>
    private static bool SageNightEquivalent(SageNight? left, SageNight? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (not null, null) or (null, not null) => false,
            ({ } a, { } b) => a.Sage == b.Sage
                && a.Demon == b.Demon
                && a.DemonCharacter == b.DemonCharacter
                && a.Effective == b.Effective
                && string.Equals(a.Note, b.Note, StringComparison.Ordinal),
        };

    /// <summary>心上人跳过账（R-0039）进比较器：它是触发器的幂等依据，漏比会让重建后重复触发。</summary>
    private static bool SweetheartSkipsEquivalent(
        IReadOnlyList<SweetheartSkipRecord> left,
        IReadOnlyList<SweetheartSkipRecord> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].Sweetheart != right[index].Sweetheart
                || !string.Equals(left[index].Reason, right[index].Reason, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>艺术家的进行中提问（R-0040）进比较器：席位、角色快照与问题全文都是投影输入，漏比会让重建校验在这里失明。</summary>
    private static bool ArtistQuestionEquivalent(ArtistQuestion? left, ArtistQuestion? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (not null, null) or (null, not null) => false,
            ({ } a, { } b) => a.Seat == b.Seat
                && a.Character == b.Character
                && string.Equals(a.Question, b.Question, StringComparison.Ordinal),
        };

    /// <summary>博学者的进行中提问（R-0057）进比较器：席位与角色快照是投影 / 结清输入，漏比会让重建校验在这里失明。</summary>
    private static bool SavantQuestionEquivalent(SavantQuestion? left, SavantQuestion? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (not null, null) or (null, not null) => false,
            ({ } a, { } b) => a.Seat == b.Seat && a.Character == b.Character,
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
                || dayLeft.Executions.Count != dayRight.Executions.Count
                || dayLeft.ExtraNomination?.Seat != dayRight.ExtraNomination?.Seat
                || dayLeft.ExtraNomination?.Status != dayRight.ExtraNomination?.Status
                || dayLeft.Nominations.Count != dayRight.Nominations.Count
                || dayLeft.Exiles.Count != dayRight.Exiles.Count
                || dayLeft.ProtectionDecisions.Count != dayRight.ProtectionDecisions.Count
                || dayLeft.VoteAttempts.Count != dayRight.VoteAttempts.Count)
            {
                return false;
            }

            // 两次处决的账（D4 / R-0050）：席位 + 来源分类逐条比对；漏比会让重建校验在处决上失明。
            for (var executionIndex = 0; executionIndex < dayLeft.Executions.Count; executionIndex++)
            {
                var executionLeft = dayLeft.Executions[executionIndex];
                var executionRight = dayRight.Executions[executionIndex];
                if (executionLeft.Seat != executionRight.Seat || executionLeft.Kind != executionRight.Kind)
                {
                    return false;
                }
            }

            for (var nominationIndex = 0; nominationIndex < dayLeft.Nominations.Count; nominationIndex++)
            {
                var nominationLeft = dayLeft.Nominations[nominationIndex];
                var nominationRight = dayRight.Nominations[nominationIndex];
                if (nominationLeft.Index != nominationRight.Index
                    || nominationLeft.Kind != nominationRight.Kind
                    || nominationLeft.Nominator != nominationRight.Nominator
                    || nominationLeft.Nominee != nominationRight.Nominee
                    || nominationLeft.NominatorCharacter != nominationRight.NominatorCharacter
                    || nominationLeft.Status != nominationRight.Status
                    || !SeatsEquivalent(nominationLeft.Ballot, nominationRight.Ballot)
                    || !SeatsEquivalent(nominationLeft.HandsRaised, nominationRight.HandsRaised)
                    || !SweepEquivalent(nominationLeft.Sweep, nominationRight.Sweep))
                {
                    return false;
                }
            }

            // 投票动作表（含角色快照）是回溯型信息能力的推演输入（R-0037）：漏比会让重建校验
            // 在"谁举过手"上失明——票面相同、动作表不同的两份账不该判等价。
            for (var attemptIndex = 0; attemptIndex < dayLeft.VoteAttempts.Count; attemptIndex++)
            {
                var attemptLeft = dayLeft.VoteAttempts[attemptIndex];
                var attemptRight = dayRight.VoteAttempts[attemptIndex];
                if (attemptLeft.NominationIndex != attemptRight.NominationIndex
                    || attemptLeft.Voter != attemptRight.Voter
                    || attemptLeft.VoterCharacter != attemptRight.VoterCharacter
                    || attemptLeft.Voted != attemptRight.Voted)
                {
                    return false;
                }
            }

            // 流放账（D2）：提议 / 状态 / 结论 / 票面 / 举手 / 收票进度 逐条比对；漏比会让重建校验
            // 在流放上失明（同样的席位与票数、不同的流放账不该判等价）。
            for (var exileIndex = 0; exileIndex < dayLeft.Exiles.Count; exileIndex++)
            {
                var exileLeft = dayLeft.Exiles[exileIndex];
                var exileRight = dayRight.Exiles[exileIndex];
                if (exileLeft.Index != exileRight.Index
                    || exileLeft.Proposer != exileRight.Proposer
                    || exileLeft.Target != exileRight.Target
                    || exileLeft.Status != exileRight.Status
                    || exileLeft.Conclusion != exileRight.Conclusion
                    || !SeatsEquivalent(exileLeft.Ballot, exileRight.Ballot)
                    || !SeatsEquivalent(exileLeft.HandsRaised, exileRight.HandsRaised)
                    || !SweepEquivalent(exileLeft.Sweep, exileRight.Sweep))
                {
                    return false;
                }
            }

            // 死亡保护裁定（D3 / R-0048）：逐条比对席位与结论；漏比会让重建校验在免死上失明。
            for (var decisionIndex = 0; decisionIndex < dayLeft.ProtectionDecisions.Count; decisionIndex++)
            {
                var decisionLeft = dayLeft.ProtectionDecisions[decisionIndex];
                var decisionRight = dayRight.ProtectionDecisions[decisionIndex];
                if (decisionLeft.Seat != decisionRight.Seat || decisionLeft.Protected != decisionRight.Protected)
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

    /// <summary>钟盘收票状态等价：顺序、呈现参数与逐席冻结结论（含角色快照）都要一致。</summary>
    private static bool SweepEquivalent(VoteSweepState? left, VoteSweepState? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.CountdownMilliseconds != right.CountdownMilliseconds
            || left.IntervalMilliseconds != right.IntervalMilliseconds
            || !SeatsEquivalent(left.Seats, right.Seats)
            || left.Collected.Count != right.Collected.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Collected.Count; index++)
        {
            var collectedLeft = left.Collected[index];
            var collectedRight = right.Collected[index];
            if (collectedLeft.Seat != collectedRight.Seat
                || collectedLeft.Voted != collectedRight.Voted
                || collectedLeft.VoterCharacter != collectedRight.VoterCharacter)
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
            || left.Audience != right.Audience
            || left.TruthRule != right.TruthRule
            || !string.Equals(left.TruthNote, right.TruthNote, StringComparison.Ordinal)
            || left.Options.Count != right.Options.Count
            || left.SecondaryOptions.Count != right.SecondaryOptions.Count)
        {
            return false;
        }

        return OptionsEquivalent(left.Options, right.Options)
            && OptionsEquivalent(left.SecondaryOptions, right.SecondaryOptions);
    }

    /// <summary>
    /// 两份候选列表是否同源：值、预览、真值、分组、事实编码、互斥组与徽章**逐项按顺序**比较。
    /// 新增候选元数据（真值 / 分组 / 编码 / 互斥组 / 徽章）必须一并比较，否则重放与实时视图会被判成"等价"。
    /// </summary>
    private static bool OptionsEquivalent(IReadOnlyList<DecisionOption> left, IReadOnlyList<DecisionOption> right)
    {
        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Value, right[index].Value, StringComparison.Ordinal)
                || !string.Equals(left[index].Preview, right[index].Preview, StringComparison.Ordinal)
                || left[index].Truth != right[index].Truth
                || !string.Equals(left[index].Group, right[index].Group, StringComparison.Ordinal)
                || !string.Equals(left[index].Code, right[index].Code, StringComparison.Ordinal)
                || !string.Equals(left[index].ExclusionGroup, right[index].ExclusionGroup, StringComparison.Ordinal)
                || !left[index].Tags.SequenceEqual(right[index].Tags, StringComparer.Ordinal))
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
