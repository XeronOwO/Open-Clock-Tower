using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 呆瓜的死亡触发：得知自己死亡的那一刻开出一条「公开选择一名存活玩家」的操作请求（R-0027）。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《呆瓜》· 2026-10-01 抓取 · 角色能力 / 运作方式——「当你得知你死亡时，你要公开选择一名
/// 存活的玩家：如果他是邪恶的，你的阵营落败」「当呆瓜玩家被宣布死亡时，他必须宣布他是呆瓜，
/// 然后选择一名存活玩家」。
/// </para>
/// <para>
/// **时点**（R-0027 第 1 条）：公开公告的那一刻——夜间死亡累积到黎明（本批出现 <see cref="DayStartedEvent"/>），
/// 白天死亡随提交即时（本批白天开着、死的正是呆瓜）。未公告不算（R-0022 第 4 条）。
/// 首版没有复活类机制，因此"黎明时呆瓜已死"等价于"这一夜公告了他"；将来引入复活类机制时
/// 要按公开面的**净变化**判据重做这一条（已记入票据残余）。
/// </para>
/// <para>
/// **幂等**：选择记录（<see cref="StepMachineState.KlutzChoices"/>，含"没选"的跳过）与未了结的请求
/// 都会阻止重复开出；选择的胜负后果由 <see cref="OutcomeEvaluator"/> 在提交前统一求值（R-0024）。
/// </para>
/// </remarks>
internal sealed class KlutzChoiceTrigger : IEventTrigger
{
    /// <summary>死亡选择的能力标识（归因 / 幂等 / 投影用）。</summary>
    internal static readonly AbilityId ChoiceAbility = new("klutz.choice");

    private static readonly CharacterId Klutz = new("klutz");

    /// <inheritdoc />
    public AbilityId Ability => ChoiceAbility;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Evaluate(EventTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 已经结束：不再开任何选择（调用方在结束时本来就会跳过触发管线；这里是第二道保险，R-0024）。
        if (context.Machine?.Outcome is not null)
        {
            return [];
        }

        var events = new List<GameEvent>();
        foreach (var gameEvent in context.Events)
        {
            switch (gameEvent)
            {
                case DayStartedEvent:
                    OpenChoiceIfDue(context, events, seat: null);
                    break;
                case SeatStateChangedEvent { Life: LifeState.Dead } death when context.DayWasOpen:
                    OpenChoiceIfDue(context, events, death.Seat);
                    break;
                case OperationRequestAnsweredEvent answered:
                    ResolveAnswer(context, events, answered);
                    break;
                case OperationRequestVoidedEvent voided:
                    RecordVoid(context, events, voided);
                    break;
            }
        }

        return events;
    }

    /// <summary>开选择：呆瓜已死、尚未处理、能力确认生效；能力不生效时写一条可归因的跳过。</summary>
    private static void OpenChoiceIfDue(EventTriggerContext context, List<GameEvent> events, SeatId? seat)
    {
        var klutz = FindDeadKlutz(context, seat);
        if (klutz is null || IsHandled(context, klutz.Value))
        {
            return;
        }

        var entry = context.State.Seat(klutz.Value);
        if (entry is null)
        {
            return;
        }

        if (entry.DrunkValue != DrunkState.Sober || entry.PoisonValue != PoisonState.Healthy)
        {
            events.Add(new KlutzChoiceSkippedEvent
            {
                Klutz = klutz.Value,
                Reason = "呆瓜死亡时能力未生效（醉酒 / 中毒，或这两维之一尚未观测）："
                    + "不进行死亡选择（R-0027 第 5 条）",
            });
            return;
        }

        var options = context.Seats
            .Where(candidate => context.State.Seat(candidate)?.LifeValue == LifeState.Alive)
            .OrderBy(candidate => candidate.Value)
            .Select(candidate => new DecisionOption
            {
                Value = SeatChoice.Format(candidate),
                Preview = $"{candidate.Value} 号玩家",
            })
            .ToArray();

        events.Add(new OperationRequestIssuedEvent
        {
            Request = new OperationRequest
            {
                Id = RequestIdFor(klutz.Value),
                Addressee = klutz.Value,
                Origin = OperationRequestOrigin.ForTrigger(
                    ChoiceAbility,
                    "呆瓜得知自己死亡：公开选择一名存活玩家（R-0027）"),
                Prompt = new ChoicePrompt
                {
                    Context = "呆瓜必须公开选择一名存活玩家：选到邪恶玩家则你的阵营落败"
                        + "（百科《呆瓜》· 2026-10-01 抓取 · 角色能力）",
                    Options = options,
                    OnNoOption = NoOptionBehavior.BlockAndAlert,
                },
            },
        });
    }

    /// <summary>把"答了什么"翻译成呆瓜选择的领域事实。</summary>
    private static void ResolveAnswer(
        EventTriggerContext context,
        List<GameEvent> events,
        OperationRequestAnsweredEvent answered)
    {
        var klutz = KlutzOfRequest(context, answered.RequestId);
        if (klutz is null || IsRecorded(context, klutz.Value))
        {
            return;
        }

        var value = answered.Answer.OptionValue;
        var target = SeatChoice.Parse(value)
            ?? throw new InvalidOperationException($"呆瓜选择的答案不是合法席位：{value}");

        events.Add(new KlutzChoiceMadeEvent
        {
            Klutz = klutz.Value,
            Target = target,
        });
    }

    /// <summary>请求被作废 = 这次没有做出选择；写一条可归因的跳过，避免后续黎明重复开。</summary>
    private static void RecordVoid(
        EventTriggerContext context,
        List<GameEvent> events,
        OperationRequestVoidedEvent voided)
    {
        var klutz = KlutzOfRequest(context, voided.RequestId);
        if (klutz is null || IsRecorded(context, klutz.Value))
        {
            return;
        }

        events.Add(new KlutzChoiceSkippedEvent
        {
            Klutz = klutz.Value,
            Reason = $"呆瓜选择被作废（{voided.Void.Reason}）：{voided.Void.Note ?? "未说明"}",
        });
    }

    private static SeatId? FindDeadKlutz(EventTriggerContext context, SeatId? seat)
    {
        foreach (var candidate in context.Seats)
        {
            if (seat is { } required && candidate != required)
            {
                continue;
            }

            var entry = context.State.Seat(candidate);
            if (entry?.CharacterValue == Klutz && entry.LifeValue == LifeState.Dead)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsHandled(EventTriggerContext context, SeatId klutz) =>
        IsRecorded(context, klutz)
        || context.Machine?.PendingRequest is
        {
            Status: OperationRequestStatus.Pending,
        } pending
            && pending.Addressee == klutz
            && pending.Origin.Kind == OperationRequestOriginKind.Trigger
            && pending.Origin.TriggerAbility == ChoiceAbility;

    private static bool IsRecorded(EventTriggerContext context, SeatId klutz) =>
        context.Machine is { } machine && machine.KlutzChoices.Any(record => record.Klutz == klutz);

    /// <summary>呆瓜选择的请求标识：由本触发器派生（同一名呆瓜一条），认领作废 / 作答时按它比对。</summary>
    private static OperationRequestId RequestIdFor(SeatId klutz) => new($"klutz:{klutz.Value}");

    /// <summary>
    /// 认领一条属于本触发器的请求：先用挂起请求确认（常规路径）；请求已经随槽位推进被清空时，
    /// 退回按**请求标识**认领——标识由本触发器自己派生（<c>klutz:{seat}</c>），且要求该席位
    /// 此刻确实是"已死的呆瓜"。没有这条退路，"强推越过白天"会让请求无声消失，
    /// 下一个黎明重复开选择（独立对抗性复核 F-1）。
    /// </summary>
    private static SeatId? KlutzOfRequest(EventTriggerContext context, OperationRequestId requestId)
    {
        if (context.Machine?.PendingRequest is { } pending && pending.Id == requestId)
        {
            return pending.Origin.Kind == OperationRequestOriginKind.Trigger
                   && pending.Origin.TriggerAbility == ChoiceAbility
                ? pending.Addressee
                : null;
        }

        foreach (var seat in context.Seats)
        {
            if (RequestIdFor(seat) == requestId && FindDeadKlutz(context, seat) == seat)
            {
                return seat;
            }
        }

        return null;
    }
}
