using Microsoft.AspNetCore.SignalR;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Server;

/// <summary>
/// wire 参数 → 应用层命令的翻译：参数形状校验、枚举解析与**参数层拒绝审计**都在这里。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="GameHub"/> 拆出（单文件 600 行门禁）：Hub 只保留"连接 / 凭据 / 调用 / 推送"，
/// 命令怎么拼装、哪些参数必须长什么样归这里。以后新增命令改这里，不再让 Hub 无限增长。
/// </para>
/// <para>
/// 参数层拒绝与凭据层一样**写审计**（零信任矩阵行 11：每次拒绝都可定位），且不触达 Application。
/// 本类只做翻译，不做任何领域判断（D-0012：客户端声明一律不可信）。
/// </para>
/// </remarks>
internal sealed class GameCommandFactory
{
    private readonly ILogger _logger;
    private readonly string _connectionId;
    private readonly string _method;

    /// <summary>为一次 Hub 调用构造翻译器。</summary>
    /// <param name="logger">Hub 日志（参数层拒绝要写审计）。</param>
    /// <param name="connectionId">发起调用的连接。</param>
    /// <param name="method">Hub 方法名（审计用）。</param>
    internal GameCommandFactory(ILogger logger, string connectionId, string method)
    {
        _logger = logger;
        _connectionId = connectionId;
        _method = method;
    }

    /// <summary>玩家提交响应。</summary>
    internal GameCommand SubmitResponse(string requestId, string optionValue) =>
        new SubmitResponseCommand
        {
            RequestId = new OperationRequestId(requestId),
            OptionValue = optionValue,
        };

    /// <summary>玩家（艺术家）在白天提问（R-0040；席位由凭据推导，命令面不自称身份）。</summary>
    internal GameCommand AskArtistQuestion(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw Reject("问题不能为空");
        }

        return new AskArtistQuestionCommand { Question = question };
    }

    /// <summary>玩家（博学者）在白天向说书人要两条信息（R-0057；席位由凭据推导，命令面带不出内容）。</summary>
    internal GameCommand AskSavantQuestion() => new AskSavantQuestionCommand();

    /// <summary>
    /// 玩家（杂耍艺人）在自己的首个白天公开猜测（R-0057-B）：席位由凭据推导，猜测内容随命令带上。
    /// 形状只做最低限度的守卫（角色名必须非空）；数量上限、首个白天与席位合法性都在内核里判。
    /// </summary>
    internal GameCommand MakeJugglerGuesses(JugglerGuessDto[]? guesses)
    {
        var mapped = new List<JugglerGuess>();
        foreach (var guess in guesses ?? [])
        {
            if (string.IsNullOrWhiteSpace(guess.Character))
            {
                throw Reject("猜测必须给出角色名");
            }

            mapped.Add(new JugglerGuess
            {
                Seat = new SeatId(guess.Seat),
                Character = new CharacterId(guess.Character),
            });
        }

        return new MakeJugglerGuessesCommand { Guesses = mapped };
    }

    /// <summary>说书人强制作废（原因按合法性闸的口径解析：非法值给未定义枚举，由闸拒绝）。</summary>
    internal GameCommand VoidRequest(string requestId, string reason, string? note) =>
        new VoidRequestCommand
        {
            RequestId = new OperationRequestId(requestId),
            Reason = ParseVoidReason(reason),
            Note = note,
        };

    /// <summary>说书人代填。</summary>
    internal GameCommand ProxyFill(string requestId, string optionValue, string? note) =>
        new ProxyFillCommand
        {
            RequestId = new OperationRequestId(requestId),
            OptionValue = optionValue,
            Note = note,
        };

    /// <summary>说书人了结裁定点（R-0009 自由决定）。</summary>
    internal GameCommand ResolveDecisionPoint(string decisionPointId, string? decision, string? note) =>
        new ResolveDecisionPointCommand
        {
            DecisionPointId = new DecisionPointId(decisionPointId),
            Decision = decision,
            Note = note,
        };

    /// <summary>说书人上报座位状态：只解析本次观测到的维度，至少给一个。</summary>
    internal GameCommand ReportSeatState(
        int seat,
        string? life,
        string? character,
        string? alignment,
        string? drunk,
        string? poison,
        string reason,
        int? causedBySeat)
    {
        var parsedLife = ParseDimension<LifeState>(life, "生死");
        var parsedAlignment = ParseDimension<Alignment>(alignment, "阵营");
        var parsedDrunk = ParseDimension<DrunkState>(drunk, "醉酒状态");
        var parsedPoison = ParseDimension<PoisonState>(poison, "中毒状态");

        if (parsedLife is null
            && character is null
            && parsedAlignment is null
            && parsedDrunk is null
            && parsedPoison is null)
        {
            throw Reject("至少需要给出一个观测到的状态维度（生死 / 角色 / 阵营 / 醉酒 / 中毒）");
        }

        return new ApplySeatStateCommand
        {
            Seat = new SeatId(seat),
            Life = parsedLife,
            Character = character is null ? null : new CharacterId(character),
            Alignment = parsedAlignment,
            Drunk = parsedDrunk,
            Poison = parsedPoison,
            Reason = reason,
            CausedBy = causedBySeat is { } causer ? new SeatId(causer) : null,
        };
    }

    /// <summary>说书人 / 宿主开局分配（角色 slug 由 Application 层按花名册复核）。</summary>
    internal GameCommand AssignCharacters(SeatCharacterAssignmentDto[]? assignments)
    {
        if (assignments is null)
        {
            throw Reject("分配列表不能为空");
        }

        return new AssignCharactersCommand
        {
            Assignments =
            [
                .. assignments.Select(item => new SeatCharacterAssignment
                {
                    Seat = new SeatId(item.Seat),
                    Character = new CharacterId(item.Character),
                }),
            ],
        };
    }

    /// <summary>
    /// 说书人 / 宿主把一名旅行者加入本局（票据 `traveller-and-exile` D1）：
    /// 席位可空 = 服务端追加新席位并签发票据（票据在命令结果里回给说书人）。
    /// </summary>
    internal GameCommand JoinTraveller(int? seat, string character, string alignment, int[]? revealDemonSeats)
    {
        if (string.IsNullOrWhiteSpace(character))
        {
            throw Reject("旅行者角色不能为空");
        }

        var parsedAlignment = ParseDimension<Alignment>(alignment, "阵营")
            ?? throw Reject("阵营不能为空（只接受 Good / Evil）");

        return new JoinTravellerCommand
        {
            Seat = seat is { } value ? new SeatId(value) : null,
            Character = new CharacterId(character),
            Alignment = parsedAlignment,
            RevealDemonSeats = [.. (revealDemonSeats ?? []).Select(value => new SeatId(value))],
        };
    }

    /// <summary>说书人 / 宿主把一名旅行者移出本局（D1）：席位与票据保留，不再计入任何人数口径。</summary>
    internal GameCommand RemoveTraveller(int seat, string? note) =>
        new RemoveTravellerCommand
        {
            Seat = new SeatId(seat),
            Note = note,
        };

    /// <summary>说书人 / 宿主开夜（口径是引擎输入，R-0014）。</summary>
    internal GameCommand StartNight(int nightNumber, string variant)
    {
        // 只认名字不认数字：给 Enum.TryParse 传数字会把序号当口径（与零信任相悖）。
        if (!Enum.TryParse<NightOrderVariant>(variant, ignoreCase: false, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw Reject($"未知的夜晚顺序口径：{variant}（只接受 Original / Recommended）");
        }

        return new StartNightCommand { NightNumber = nightNumber, Variant = parsed };
    }

    /// <summary>说书人 / 宿主处罚处决（来源只认枚举名，R-0020）。</summary>
    internal GameCommand PunishExecution(int seat, string source, string? note)
    {
        if (!Enum.TryParse<MadnessPunishmentSource>(source, ignoreCase: false, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw Reject($"未知的处罚来源：{source}（只接受 Cerenovus / Mutant）");
        }

        return new PunishExecutionCommand
        {
            Seat = new SeatId(seat),
            Source = parsed,
            Note = note,
        };
    }

    /// <summary>说书人 / 宿主在麻脸巫婆之夜追加死亡（R-0030）。</summary>
    internal GameCommand PitHagCasualty(int seat, string? note) =>
        new PitHagCasualtyCommand
        {
            Seat = new SeatId(seat),
            Note = note,
        };

    /// <summary>说书人 / 宿主裁定一条待定死亡（R-0030 第 2 条）。</summary>
    internal GameCommand ResolveDeferredDeath(int seat, bool killed, string? note) =>
        new ResolveDeferredDeathCommand
        {
            Seat = new SeatId(seat),
            Killed = killed,
            Note = note,
        };

    /// <summary>说书人 / 宿主给某席加一条注记（D-0019）。</summary>
    internal GameCommand AddSeatAnnotation(int seat, string text) =>
        new AddSeatAnnotationCommand
        {
            Seat = new SeatId(seat),
            Text = text,
        };

    /// <summary>说书人 / 宿主改一条注记的文本（D-0019）。</summary>
    internal GameCommand UpdateSeatAnnotation(int annotationId, string text) =>
        new UpdateSeatAnnotationCommand
        {
            Id = new SeatAnnotationId(annotationId),
            Text = text,
        };

    /// <summary>说书人 / 宿主删一条注记（D-0019）。</summary>
    internal GameCommand RemoveSeatAnnotation(int annotationId) =>
        new RemoveSeatAnnotationCommand
        {
            Id = new SeatAnnotationId(annotationId),
        };

    /// <summary>玩家发起流放提议（发起人由凭据推导；R-0044 第 2 条）。</summary>
    internal GameCommand ProposeExile(int targetSeat) =>
        new ProposeExileCommand { Target = new SeatId(targetSeat) };

    /// <summary>玩家（屠夫）在额外提名窗口里发起提名（发起人由凭据推导；R-0050）。</summary>
    internal GameCommand NominateExtra(int nomineeSeat) =>
        new NominateExtraCommand { Nominee = new SeatId(nomineeSeat) };

    /// <summary>玩家在当前开放的流放提议上举手 / 放下（R-0044 第 4 条）。</summary>
    internal GameCommand CastExileVote(int exileIndex, bool voted) =>
        new CastExileVoteCommand { ExileIndex = exileIndex, Voted = voted };

    /// <summary>说书人 / 宿主开始流放收票（节奏参数只作呈现，判定不读；R-0017 机制）。</summary>
    internal GameCommand StartExileSweep(int exileIndex, int countdownMilliseconds, int intervalMilliseconds) =>
        new StartExileSweepCommand
        {
            ExileIndex = exileIndex,
            CountdownMilliseconds = countdownMilliseconds,
            IntervalMilliseconds = intervalMilliseconds,
        };

    /// <summary>说书人 / 宿主继续中断的流放收票。</summary>
    internal GameCommand ResumeExileSweep(int exileIndex) =>
        new ResumeExileSweepCommand { ExileIndex = exileIndex };

    /// <summary>说书人 / 宿主在收票完成后给流放计票（R-0044 第 5 / 9 条）。</summary>
    internal GameCommand CountExileVotes(int exileIndex) =>
        new CountExileVotesCommand { ExileIndex = exileIndex };

    /// <summary>说书人 / 宿主裁定当天的死亡保护（R-0048）。</summary>
    internal GameCommand ResolveDayProtection(int seat, bool isProtected, string? note) =>
        new ResolveDayProtectionCommand
        {
            Seat = new SeatId(seat),
            Protected = isProtected,
            Note = note,
        };

    /// <summary>把客户端传来的维度字符串解析成枚举；null = 本次未观测，非法值当场拒绝（并写审计）。</summary>
    private TEnum? ParseDimension<TEnum>(string? raw, string label)
        where TEnum : struct, Enum
    {
        if (raw is null)
        {
            return null;
        }

        // 只认名字不认数字：Enum.TryParse 会把 "0" 解析成首个枚举值，那是"客户端说了算"，与零信任相悖。
        if (raw.Length == 0 || char.IsAsciiDigit(raw[0]))
        {
            throw Reject($"未知的{label}：{raw}（只接受枚举名）");
        }

        if (!Enum.TryParse<TEnum>(raw, ignoreCase: false, out var value) || !Enum.IsDefined(value))
        {
            throw Reject($"未知的{label}：{raw}");
        }

        return value;
    }

    /// <summary>作废原因：非法值给未定义枚举，由合法性闸按登记表拒绝（保持原有拒绝码与文案）。</summary>
    private static OperationRequestVoidReason ParseVoidReason(string reason) =>
        Enum.TryParse<OperationRequestVoidReason>(reason, ignoreCase: false, out var parsed)
            ? parsed
            : (OperationRequestVoidReason)(-1);

    /// <summary>
    /// 参数层拒绝：同一个拒绝对外只抛 <see cref="HubException"/>，对内存审计（连接 / 方法 / 原因）。
    /// </summary>
    /// <remarks>
    /// M4 / G-A5-10：这里的 <paramref name="reason"/> 常常**整段嵌着客户端原文**
    /// （"未知的{label}：{raw}"），所以进日志前必须过 <see cref="LogText.Clamp"/>——
    /// 逐字符转义 + 截断，换行伪造不出第二行日志，超长串也灌不爆日志。
    /// </remarks>
    private HubException Reject(string reason)
    {
        _logger.LogWarning(
            "命令被拒绝（参数）：connection={ConnectionId} 方法={Method} 原因={Reason}",
            _connectionId,
            _method,
            LogText.Clamp(reason));
        return new HubException(reason);
    }
}
