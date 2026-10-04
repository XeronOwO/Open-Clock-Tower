using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 呆瓜的死亡触发：得知自己死亡的那一刻开出一条「公开选择一名存活玩家」的操作请求（R-0027）；
/// 若他正处于咖啡师「行动两次」窗口内，则要选**两次**（范例：呆瓜需要行动两次，死亡后必须选择两名
/// 玩家，只要其中存在邪恶玩家，邪恶阵营获胜）。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《呆瓜》· 2026-10-01 抓取 · 角色能力 / 运作方式——「当你得知你死亡时，你要公开选择一名
/// 存活的玩家：如果他是邪恶的，你的阵营落败」「当呆瓜玩家被宣布死亡时，他必须宣布他是呆瓜，
/// 然后选择一名存活玩家」；百科《咖啡师》· 2026-10-04 抓取 · 范例——「呆瓜需要行动两次。他死亡了，
/// 且必须要选择两名玩家，只要其中存在邪恶玩家，邪恶阵营获胜」。
/// </para>
/// <para>
/// **时点**（R-0027 第 1 条）：公开公告的那一刻——夜间死亡累积到黎明（本批出现 <see cref="DayStartedEvent"/>），
/// 白天死亡随提交即时（本批白天开着、死的正是呆瓜）。未公告不算（R-0022 第 4 条）。
/// </para>
/// <para>
/// **次数**（R-0052 第 3 条）：窗口**确认生效**时允许 2 次选择（判定不了时按 1 次，不猜）；
/// 第二次请求在第一次选择被记录后立刻开出（同一批），请求标识带遍次（<c>klutz:{seat}#2</c>）——
/// 幂等与认领按"第几遍"对齐，不按"有没有记录"。
/// </para>
/// <para>
/// **幂等**：选择记录（<see cref="StepMachineState.KlutzChoices"/>，含"没选"的跳过）与未了结的请求
/// 都会阻止重复开出；选择的胜负后果由 <see cref="OutcomeEvaluator"/> 在提交前逐条求值（R-0024）。
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

        events.Add(new OperationRequestIssuedEvent
        {
            Request = BuildRequest(context, klutz.Value, RecordCount(context, klutz.Value) + 1),
        });
    }

    /// <summary>把"答了什么"翻译成呆瓜选择的领域事实；「行动两次」时紧接着开第二次选择。</summary>
    private static void ResolveAnswer(
        EventTriggerContext context,
        List<GameEvent> events,
        OperationRequestAnsweredEvent answered)
    {
        if (ClaimOfRequest(context, answered.RequestId) is not { } claim)
        {
            return;
        }

        // 幂等按"第几遍"对齐：第 N 遍的答案只在记录数还不足 N 条时被接受（重复投递 → 忽略）。
        if (RecordCount(context, claim.Seat) >= claim.Pass)
        {
            return;
        }

        var value = answered.Answer.OptionValue;
        var target = SeatChoice.Parse(value)
            ?? throw new InvalidOperationException($"呆瓜选择的答案不是合法席位：{value}");

        events.Add(new KlutzChoiceMadeEvent
        {
            Klutz = claim.Seat,
            Target = target,
        });

        // 咖啡师「行动两次」（R-0052 第 3 条）：第一次选择之后还欠一次 → 紧接着开第二条请求。
        // 窗口此刻**确认生效**才开（判定不了不开，不猜）；第二遍的标识带 #2，答第二遍时按它幂等。
        if (claim.Pass <= 1 && AllowedChoices(context, claim.Seat) > 1)
        {
            events.Add(new OperationRequestIssuedEvent
            {
                Request = BuildRequest(context, claim.Seat, claim.Pass + 1),
            });
        }
    }

    /// <summary>请求被作废 = 这一遍没有做出选择；写一条可归因的跳过（不再补开这一遍）。</summary>
    private static void RecordVoid(
        EventTriggerContext context,
        List<GameEvent> events,
        OperationRequestVoidedEvent voided)
    {
        if (ClaimOfRequest(context, voided.RequestId) is not { } claim)
        {
            return;
        }

        if (RecordCount(context, claim.Seat) >= claim.Pass)
        {
            return;
        }

        events.Add(new KlutzChoiceSkippedEvent
        {
            Klutz = claim.Seat,
            Reason = $"呆瓜选择被作废（{voided.Void.Reason}）：{voided.Void.Note ?? "未说明"}",
        });
    }

    /// <summary>构造一条选择请求（首次与「行动两次」的第二次共用；标识与文案带遍次）。</summary>
    private static OperationRequest BuildRequest(EventTriggerContext context, SeatId klutz, int pass)
    {
        var allowed = AllowedChoices(context, klutz);
        var boost = allowed > 1
            ? $"（咖啡师「行动两次」：本局共 {allowed} 次，这是第 {pass} 次；R-0052 第 3 条）"
            : string.Empty;

        var options = context.Seats
            .Where(candidate => context.State.Seat(candidate)?.LifeValue == LifeState.Alive)
            .OrderBy(candidate => candidate.Value)
            .Select(candidate => new DecisionOption
            {
                Value = SeatChoice.Format(candidate),
                Preview = $"{candidate.Value} 号玩家",
            })
            .ToArray();

        return new OperationRequest
        {
            Id = RequestIdFor(klutz, pass),
            Addressee = klutz,
            Origin = OperationRequestOrigin.ForTrigger(
                ChoiceAbility,
                $"呆瓜得知自己死亡：公开选择一名存活玩家{boost}（R-0027）"),
            Prompt = new ChoicePrompt
            {
                Context = "呆瓜必须公开选择一名存活玩家：选到邪恶玩家则你的阵营落败"
                    + $"{boost}（百科《呆瓜》· 2026-10-01 抓取 · 角色能力）",
                Options = options,
                OnNoOption = NoOptionBehavior.BlockAndAlert,
            },
        };
    }

    /// <summary>
    /// 这名呆瓜被允许做几次选择：咖啡师「行动两次」窗口**确认生效**时 2 次，否则 1 次
    /// （判定不了按 1 次处理，不猜；R-0052 第 3 条）。
    /// </summary>
    private static int AllowedChoices(EventTriggerContext context, SeatId klutz) =>
        context.State.WindowOn(klutz, EffectWindowKind.SecondAction) == true ? 2 : 1;

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

    /// <summary>这名呆瓜是否已经处理完（不再开新选择）。</summary>
    private static bool IsHandled(EventTriggerContext context, SeatId klutz)
    {
        var records = context.Machine?.KlutzChoices
            .Where(record => record.Klutz == klutz)
            .ToArray() ?? [];

        return records.Any(record => !record.IsMade)
            || records.Length >= AllowedChoices(context, klutz)
            || IsPending(context, klutz);
    }

    /// <summary>当前是否有一条属于这名呆瓜的、尚未了结的选择请求。</summary>
    private static bool IsPending(EventTriggerContext context, SeatId klutz) =>
        context.Machine?.PendingRequest is { Status: OperationRequestStatus.Pending } pending
        && pending.Addressee == klutz
        && pending.Origin.Kind == OperationRequestOriginKind.Trigger
        && pending.Origin.TriggerAbility == ChoiceAbility;

    /// <summary>这名呆瓜已经记下的记录数（含"没选"的跳过）。</summary>
    private static int RecordCount(EventTriggerContext context, SeatId klutz) =>
        context.Machine?.KlutzChoices.Count(record => record.Klutz == klutz) ?? 0;

    /// <summary>
    /// 呆瓜选择的请求标识：第 1 遍沿用 <c>klutz:{seat}</c>（旧事件流兼容），其后带 <c>#{遍次}</c>。
    /// </summary>
    private static OperationRequestId RequestIdFor(SeatId klutz, int pass) =>
        pass <= 1 ? new OperationRequestId($"klutz:{klutz.Value}") : new OperationRequestId($"klutz:{klutz.Value}#{pass}");

    /// <summary>从请求标识解析「哪个席位、第几遍」；形状不对时返回 null（不认领别人的请求）。</summary>
    private static (SeatId Seat, int Pass)? ParseRequestId(OperationRequestId requestId)
    {
        const string prefix = "klutz:";
        if (!requestId.Value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = requestId.Value[prefix.Length..];
        var hash = rest.IndexOf('#', StringComparison.Ordinal);
        var seatPart = hash < 0 ? rest : rest[..hash];
        var pass = 1;
        if (hash >= 0
            && (!int.TryParse(rest[(hash + 1)..], out pass) || pass < 1))
        {
            return null;
        }

        return int.TryParse(seatPart, out var seat) && seat >= 1
            ? (new SeatId(seat), pass)
            : null;
    }

    /// <summary>
    /// 认领一条属于本触发器的请求，并给出它的遍次：先用挂起请求确认（常规路径）；
    /// 请求已经随槽位推进被清空时，退回按**请求标识**认领——标识由本触发器自己派生，且要求该席位
    /// 此刻确实是"已死的呆瓜"。没有这条退路，"强推越过白天"会让请求无声消失，
    /// 下一个黎明重复开选择（独立对抗性复核 F-1）。
    /// </summary>
    private static (SeatId Seat, int Pass)? ClaimOfRequest(EventTriggerContext context, OperationRequestId requestId)
    {
        if (context.Machine?.PendingRequest is { } pending && pending.Id == requestId)
        {
            if (pending.Origin.Kind != OperationRequestOriginKind.Trigger
                || pending.Origin.TriggerAbility != ChoiceAbility
                || ParseRequestId(requestId) is not { } claimed)
            {
                return null;
            }

            return claimed;
        }

        return ParseRequestId(requestId) is { } parsed && FindDeadKlutz(context, parsed.Seat) == parsed.Seat
            ? parsed
            : null;
    }
}
