using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 投影：把步骤机状态折算成"某个受众该看到什么"。
/// </summary>
/// <remarks>
/// 依据 D-0012 §4.3：越权信息**根本不下发**——玩家投影里没有槽位、进度、他人活动（D-0013 §5）。
/// </remarks>
public static class GameProjection
{
    /// <summary>某个玩家的投影。</summary>
    /// <param name="machine">步骤机状态。</param>
    /// <param name="state">状态账（白天权限判定要读生死）。</param>
    /// <param name="seats">本局完整座次（算可提名目标用）。</param>
    /// <param name="sequence">投影对应的事件序号。</param>
    /// <param name="now">应用层当前时刻（算收票剩余时间；不驱动推进）。</param>
    /// <param name="voteSweepStartedAt">收票时间轴锚点；为空 = 未开始或已中断。</param>
    /// <param name="seat">接收者席位。</param>
    /// <param name="trackers">会话派生跟踪器：发给该席位的信息结果与公开生死面（R-0022）。</param>
    /// <param name="seatNames">公开的「席位 → 玩家名」映射（D-0021；无名字的席位不出现）。</param>
    /// <param name="characters">角色事实端口（判定流放目标是不是旅行者；R-0044）；缺失 = 不给流放候选（不猜）。</param>
    public static PlayerView ForSeat(
        StepMachineState? machine,
        GameState state,
        IReadOnlyList<SeatId> seats,
        long sequence,
        DateTimeOffset now,
        DateTimeOffset? voteSweepStartedAt,
        SeatId seat,
        SessionTrackers trackers,
        IReadOnlyList<SeatDisplayName> seatNames,
        IWinConditionFacts? characters = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(trackers);
        ArgumentNullException.ThrowIfNull(seatNames);

        var pending = machine?.PendingRequest;
        var ended = machine?.Outcome is not null;

        // 结束态不再下发任何请求：终局快照可能还留着最后一条请求（作废要重排结束批次的管线），
        // 而它对玩家已经答不了（一切提交都被 phase.game_ended 拒）——推给他就是一条死信。
        var deliverable = !ended && pending is { Status: OperationRequestStatus.Pending } && pending.Addressee == seat
            ? pending
            : null;

        return new PlayerView
        {
            Seat = seat,
            Phase = machine?.Plan.Phase,
            PendingRequest = deliverable,
            InformationResults = trackers.InformationResultsFor(seat),
            Day = DayProjection.ForSeat(
                machine?.Day,
                state,
                seats,
                seat,
                trackers.PublicLife,
                now,
                voteSweepStartedAt,
                characters),
            Outcome = machine?.Outcome,
            KlutzChoices = [.. (machine?.KlutzChoices ?? []).Select(PublicKlutzChoice)],
            SeatNames = seatNames,
            Sequence = sequence,

            // 艺术家的进行中提问（R-0040）：只对本人可见——问题全文不进任何他人投影（D-0012）。
            PendingQuestion = machine?.ArtistQuestion is { } question && question.Seat == seat
                ? question.Question
                : null,

            // 本人能不能发起提问（白天 + 本人是艺术家 + 还没用过）：前端据此显示入口，服务端仍逐项校验。
            CanAskArtistQuestion = CanAskArtistQuestion(machine, state, seat),

            // 本人此刻能不能向说书人要两条信息（白天 + 本人是博学者 + 今天还没要过）：同款权限位（R-0057）。
            CanAskSavantQuestion = CanAskSavantQuestion(machine, state, seat),

            // 本人有一条博学者提问在等说书人：等待态只对本人可见（入口据此显示「等待说书人」）。
            AwaitingSavantQuestion = machine?.SavantQuestion is { } savantQuestion && savantQuestion.Seat == seat,

            // 本人已用尽的一次性能力（重连后恢复"已用"状态，R-0040）：只列自己那一份。
            ExhaustedAbilities = ExhaustedFor(state, seat),
        };
    }

    /// <summary>
    /// 玩家面的呆瓜选择记录：**只公开"选了什么"**。跳过记录的原因写的是能力为何没生效
    /// （醉酒 / 中毒 / 被谁作废）——那是说书人专属维度，按 R-0012 与 D-0012 §4.3 不下发；
    /// 说书人视图保留完整 <see cref="KlutzChoiceRecord.Detail"/>。
    /// </summary>
    private static KlutzChoiceRecord PublicKlutzChoice(KlutzChoiceRecord record) =>
        record.IsMade ? record : record with { Detail = SkippedChoiceDetail };

    /// <summary>跳过记录的公开文案：只说"没做出选择"，不解释为什么（原因只在说书人视图）。</summary>
    private const string SkippedChoiceDetail = "呆瓜本次没有做出选择";

    /// <summary>
    /// 「失去能力」标记：从能力使用账本派生——限次能力只要使用过（含未生效）就永久失去能力
    /// （R-0040；三-3「使用机会被浪费」）。按（席位，能力）去重，顺序按登记表。
    /// </summary>
    private static IReadOnlyList<LostAbilityMarker> BuildLostAbilityMarkers(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var markers = new List<LostAbilityMarker>();
        foreach (var registered in OnceAbilities.Registry)
        {
            foreach (var use in state.AbilityUses.Entries.Where(entry => entry.Ability == registered.Ability))
            {
                if (markers.Any(marker => marker.Seat == use.Seat && marker.Ability == use.Ability))
                {
                    continue;
                }

                markers.Add(new LostAbilityMarker
                {
                    Seat = use.Seat,
                    Ability = use.Ability,
                    Note = $"{registered.DisplayName}（{use.Seat.Value} 号）：能力已用尽——失去能力（R-0040）",
                });
            }
        }

        return markers;
    }

    /// <summary>本人已用尽的一次性能力 slug（只列自己，不泄露他人的用度）。</summary>
    private static string[] ExhaustedFor(GameState state, SeatId seat)
    {
        ArgumentNullException.ThrowIfNull(state);

        return
        [
            .. OnceAbilities.Registry
                .Where(registered => state.AbilityUses.WasUsed(seat, registered.Ability))
                .Select(registered => registered.Ability.Value),
        ];
    }

    /// <summary>
    /// 本人此刻能不能发起艺术家的白天提问：白天开着、本人是艺术家（按注册的提问来源）且还没用过（R-0040）。
    /// 这只是**本人的权限位**；真正的合法性由内核按同一份账再判一次（D-0012：前端不做领域判断）。
    /// </summary>
    private static bool CanAskArtistQuestion(StepMachineState? machine, GameState state, SeatId seat)
    {
        if (machine?.Plan.Phase != GamePhase.Day || machine.Day?.OpenDay is null)
        {
            return false;
        }

        // 已有未结清的问题：此刻不能再问（与内核的 artist.question_pending 同款判定）。
        if (machine.ArtistQuestion is not null)
        {
            return false;
        }

        var character = state.Seat(seat)?.CharacterValue;
        return character is not null
            && RoleContracts.ArtistQuestions.Any(source =>
                source.Character == character && !state.AbilityUses.WasUsed(seat, source.Ability));
    }

    /// <summary>
    /// 本人此刻能不能向说书人要两条信息：白天开着、本人是博学者（按注册的提问来源）、**今天还没要过**（R-0057）。
    /// 同 <see cref="CanAskArtistQuestion"/>：这只是本人的权限位，真正的合法性由内核按同一份账再判一次。
    /// </summary>
    private static bool CanAskSavantQuestion(StepMachineState? machine, GameState state, SeatId seat)
    {
        if (machine?.Plan.Phase != GamePhase.Day || machine.Day?.OpenDay is null)
        {
            return false;
        }

        // 已有未结清的提问：此刻不能再要（与内核的 savant.question_pending 同款判定）。
        if (machine.SavantQuestion is not null)
        {
            return false;
        }

        // 今天已经要过：要等下一个白天（与内核的 savant.already_asked_today 同款判定）。
        if (machine.SavantAskedSeat == seat)
        {
            return false;
        }

        var character = state.Seat(seat)?.CharacterValue;
        return character is not null
            && RoleContracts.SavantQuestions.Any(source => source.Character == character);
    }

    /// <summary>说书人视图（含卡点时长、状态账、效果归因、能力结算结论、每步摘要与房间健康位；时长由应用层时钟算出）。</summary>
    public static StorytellerView ForStoryteller(
        StepMachineState? machine,
        GameState state,
        RoomHealth health,
        long sequence,
        DateTimeOffset? pendingSince,
        DateTimeOffset now,
        DateTimeOffset? voteSweepStartedAt,
        IReadOnlyList<SeatChangeSnapshot> recentSeatChanges,
        IReadOnlyList<SeatAnnotation> annotations,
        IReadOnlyList<SeatDisplayName> seatNames,
        AbilityResolutionSnapshot? lastResolution = null,
        StepDigest? stepDigest = null,
        VoidedRequestSnapshot? lastVoidedRequest = null)
    {
        PendingRequestSummary? pendingSummary = null;
        if (machine?.PendingRequest is { Status: OperationRequestStatus.Pending } request)
        {
            pendingSummary = new PendingRequestSummary
            {
                Seat = request.Addressee,
                RequestId = request.Id,
                SlotId = request.Origin.SlotId,
                SlotIndex = request.Origin.SlotIndex,
                TriggerReason = request.Origin.TriggerReason,
                Waiting = pendingSince is { } since ? now - since : null,
            };
        }

        return new StorytellerView
        {
            Sequence = sequence,
            Phase = machine?.Plan.Phase,
            Control = machine?.Control,
            Health = health,
            SlotIndex = machine?.SlotIndex ?? 0,
            SlotCount = machine?.Plan.Slots.Count ?? 0,
            CurrentSlotId = machine?.CurrentSlot?.Id,
            PlanCompleted = machine?.IsPlanCompleted ?? false,
            Pending = pendingSummary,
            AwaitingDecision = machine?.AwaitingDecision,
            AwaitingDecisionSeat = machine?.AwaitingDecisionSeat,
            BlockedReason = machine?.Block?.Reason,
            CurrentSlotActor = machine?.CurrentSlot?.Actor,
            CurrentSlotContext = machine?.CurrentSlot?.Prompt?.Context,
            RecentSeatChanges = recentSeatChanges,
            Seats = state.Seats,
            PersistentEffects = state.PersistentEffects,
            InstantaneousEffects = state.InstantaneousEffects,
            AbilityUses = state.AbilityUses.Entries,
            Malfunctions = state.Malfunctions.Entries,
            LastResolution = lastResolution,
            StepDigest = stepDigest,
            LastVoidedRequest = lastVoidedRequest,
            Day = machine?.Day?.Days.LastOrDefault(),

            // 钟盘收票的呈现相位（剩余时间在读取时算出；R-0017 目标形态）。
            VoteSweep = VoteSweepProjection.Build(machine?.Day?.OpenDay?.OpenNomination, now, voteSweepStartedAt),

            // 流放钟盘同款呈现（R-0044）：提名与流放各持一份收票状态，同一时刻至多一条未收完。
            ExileSweep = VoteSweepProjection.Build(machine?.Day?.OpenDay?.OpenExile, now, voteSweepStartedAt),
            Outcome = machine?.Outcome,
            KlutzChoices = machine?.KlutzChoices ?? [],
            SeatNames = seatNames,

            // 麻脸巫婆之夜的死亡裁量窗口（R-0030）：说书人要据此裁定待定死亡、
            // 并在窗口内追加死亡——玩家投影里没有它（D-0012 §4.3）。
            PitHagNight = machine?.PitHagNight,

            // 方古「限一次」/「今晚理发」：整局 / 跨阶段事实的说书人投影（R-0034 / R-0033）；
            // 玩家投影里没有它们（D-0012 §4.3）。
            FangGuInfection = machine?.FangGuInfection,
            BarberNight = machine?.BarberNight,

            // 说书人注记（D-0019）：自由文本提示标记只说书人可见；玩家投影里没有这条字段。
            Annotations = annotations,

            // 「失去能力」提示标记（R-0040）：限次能力用尽后挂在角色标记旁，由能力使用账本派生
            // （不新增事实，D-0010）；玩家投影里没有它（D-0012 §4.3）。
            LostAbilityMarkers = BuildLostAbilityMarkers(state),
        };
    }
}
