using OpenClockTower.Application;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 白天投影的旅行者口径（票据 traveller-and-exile · D7）：流放候选与权限、额外提名窗口、
/// 效果窗口字段——服务端算好的**使能条件**，与内核判定同源（R-0044 / R-0048 / R-0050 / R-0054）。
/// </summary>
/// <remarks>
/// 纯投影用例：不建宿主，直接给 <see cref="DayProjection"/> 喂白天账与状态账，断言"该给什么、
/// 不该给什么"（真宿主链路证据在 ExileHostTests / TravellerHostTests）。
/// </remarks>
public sealed class DayProjectionTests
{
    /// <summary>收票快照：四人局 1–4 号。</summary>
    private static readonly SeatId[] BallotSeats = [new(1), new(2), new(3), new(4)];

    /// <summary>开着的白天（第 1 天，没有提名 / 流放 / 窗口）。</summary>
    private static DayRecord OpenDay() => new()
    {
        DayNumber = 1,
        Status = DayStatus.Open,
    };

    /// <summary>四人局状态账：1 号筑梦师、2 号怪咖、3 号集骨者、4 号钟表匠（全员存活）。</summary>
    private static GameState Baseline() => State(
        (1, "dreamer", LifeState.Alive),
        (2, "deviant", LifeState.Alive),
        (3, "bone-collector", LifeState.Alive),
        (4, "clockmaker", LifeState.Alive));

    /// <summary>按"席位 + 角色 + 生死"折叠出状态账（与 ExilePhaseFixture 同款；其余维度不观测）。</summary>
    private static GameState State(params (int Seat, string Character, LifeState Life)[] seats) =>
        GameStateMachine.Fold(
        [
            .. seats.Select(item => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(item.Seat),
                Character = new CharacterId(item.Character),
                Life = item.Life,
                Reason = "test.day-projection",
            }),
        ]);

    /// <summary>投影某个席位；座次默认 = 在局四人；跨天账默认只含这一天。</summary>
    /// <param name="days">跨天账（多天场景要连历史一起给：公开猜测的「这次持有猜过没有」读它）。</param>
    private static PlayerDay Project(
        DayRecord day,
        int seat,
        GameState? state = null,
        DayState? days = null,
        params int[] inGame)
    {
        var seats = inGame.Length == 0
            ? BallotSeats
            : inGame.Select(value => new SeatId(value)).ToArray();
        return DayProjection.ForSeat(
            days ?? new DayState { Days = [day] },
            state ?? Baseline(),
            seats,
            new SeatId(seat),
            PublicLifeBoard.Empty,
            DateTimeOffset.UnixEpoch,
            voteSweepStartedAt: null,
            WinConditionFacts.Instance)
            ?? throw new InvalidOperationException("白天账非空时投影不应为 null");
    }

    private static ExileRecord VotingExile(VoteSweepState? sweep = null, params int[] raised) => new()
    {
        Index = 1,
        Proposer = new SeatId(1),
        Target = new SeatId(2),
        Status = ExileStatus.Voting,
        HandsRaised = [.. raised.Select(value => new SeatId(value))],
        Sweep = sweep,
    };

    /// <summary>候选 = 在局旅行者里今天还没被提议过的（怪咖 / 集骨者），镇民不进候选。</summary>
    [Fact]
    public void ExileCandidates_AreInGameTravellers()
    {
        var projected = Project(OpenDay(), seat: 1);

        Assert.True(projected.CanProposeExile);
        Assert.Equal(new[] { 2, 3 }, projected.ExileCandidates.Select(candidate => candidate.Value));
        Assert.False(projected.CanVoteExile);
        Assert.False(projected.ExileSeatCollected);
        Assert.Null(projected.ExileSweep);
    }

    /// <summary>今天已被提议过的旅行者不再进候选；已有未结清流放时不能再提下一条（同日顺序进行）。</summary>
    [Fact]
    public void ExileCandidates_ExcludeAlreadyProposed_AndOpenExileBlocksNewProposal()
    {
        var day = OpenDay() with
        {
            Exiles =
            [
                VotingExile() with { Target = new SeatId(3) },
            ],
        };

        var projected = Project(day, seat: 1);

        Assert.Equal(new[] { 2 }, projected.ExileCandidates.Select(candidate => candidate.Value));
        Assert.False(projected.CanProposeExile);
    }

    /// <summary>候选按**在局座次**派生：已离场席位的观测残留在账里也不进候选（R-0044 第 6 条）。</summary>
    [Fact]
    public void ExileCandidates_ComeFromInGameSeatsOnly()
    {
        var state = State(
            (1, "dreamer", LifeState.Alive),
            (2, "deviant", LifeState.Alive),
            (3, "bone-collector", LifeState.Alive),
            (4, "harlot", LifeState.Alive));

        var projected = Project(OpenDay(), seat: 1, state: state, inGame: [1, 2, 3]);

        Assert.Equal(new[] { 2, 3 }, projected.ExileCandidates.Select(candidate => candidate.Value));
    }

    /// <summary>角色端口缺失（或目标角色未观测）时不给候选：不猜（与内核同一姿态）。</summary>
    [Fact]
    public void ExileCandidates_AreEmptyWithoutCharacterFacts()
    {
        var projected = DayProjection.ForSeat(
            new DayState { Days = [OpenDay()] },
            Baseline(),
            BallotSeats,
            new SeatId(1),
            PublicLifeBoard.Empty,
            DateTimeOffset.UnixEpoch,
            voteSweepStartedAt: null,
            characters: null) ?? throw new InvalidOperationException("白天账非空时投影不应为 null");

        Assert.True(projected.CanProposeExile);
        Assert.Empty(projected.ExileCandidates);
    }

    /// <summary>含死者：任何在局玩家（含死者）都可以发起流放提议（R-0044 第 2 条）。</summary>
    [Fact]
    public void DeadSeat_CanProposeExile()
    {
        var state = State(
            (1, "dreamer", LifeState.Alive),
            (2, "deviant", LifeState.Alive),
            (3, "bone-collector", LifeState.Alive),
            (4, "clockmaker", LifeState.Dead));

        var projected = Project(OpenDay(), seat: 4, state);

        Assert.True(projected.CanProposeExile);
    }

    /// <summary>
    /// 流放收票：已收票席位展示**冻结结论**且不能再改；未收票席位可举手（含死者——R-0044 第 4 条，
    /// 死者不查也不耗投票标记）。
    /// </summary>
    [Fact]
    public void ExileSweep_ExposesCollectedFreezeAndLiveHands()
    {
        var sweep = new VoteSweepState
        {
            Seats = BallotSeats,
            CountdownMilliseconds = 3000,
            IntervalMilliseconds = 1000,
            Collected = [new CollectedSeatVote { Seat = new SeatId(1), Voted = false }],
        };
        var day = OpenDay() with { Exiles = [VotingExile(sweep, 3)] };
        var state = State(
            (1, "dreamer", LifeState.Alive),
            (2, "deviant", LifeState.Alive),
            (3, "bone-collector", LifeState.Alive),
            (4, "clockmaker", LifeState.Dead));

        var collected = Project(day, seat: 1, state);
        Assert.True(collected.ExileSeatCollected);
        Assert.False(collected.ExileVoted);
        Assert.False(collected.CanVoteExile);

        var dead = Project(day, seat: 4, state);
        Assert.False(dead.ExileSeatCollected);
        Assert.True(dead.CanVoteExile);

        var raised = Project(day, seat: 3, state);
        Assert.True(raised.ExileVoted);
    }

    /// <summary>屠夫窗口只授予窗口席位本人；候选 = 在局座次全部（含今天已被提名过的人）。</summary>
    [Fact]
    public void ExtraNominationWindow_GrantsOnlyItsSeat()
    {
        var open = OpenDay() with
        {
            ExtraNomination = new ExtraNominationWindow
            {
                Seat = new SeatId(2),
                Status = ExtraNominationWindowStatus.Open,
            },
        };

        var granted = Project(open, seat: 2);
        Assert.True(granted.CanNominateExtra);
        Assert.Equal(new[] { 1, 2, 3, 4 }, granted.ExtraNominationCandidates.Select(seat => seat.Value));

        var other = Project(open, seat: 3);
        Assert.False(other.CanNominateExtra);
        Assert.Empty(other.ExtraNominationCandidates);

        var used = Project(
            open with
            {
                ExtraNomination = new ExtraNominationWindow
                {
                    Seat = new SeatId(2),
                    Status = ExtraNominationWindowStatus.Used,
                },
            },
            seat: 2);
        Assert.False(used.CanNominateExtra);
    }

    /// <summary>白天公开事实带出流放账、保护裁定与额外提名窗口（D7 的 wire 字段）。</summary>
    [Fact]
    public void DayViewDto_CarriesExilesProtectionsAndExtraNomination()
    {
        var day = OpenDay() with
        {
            Exiles = [VotingExile()],
            ProtectionDecisions = [new DayProtectionDecision { Seat = new SeatId(2), Protected = true }],
            ExtraNomination = new ExtraNominationWindow
            {
                Seat = new SeatId(4),
                Status = ExtraNominationWindowStatus.Open,
            },
        };

        var dto = ProjectionMapper.ToDto(day, nominationSweep: null, exileSweep: null);

        var exile = Assert.Single(dto.Exiles);
        Assert.Equal(1, exile.Index);
        Assert.Equal(1, exile.Proposer);
        Assert.Equal(2, exile.Target);
        Assert.Equal("Voting", exile.Status);
        Assert.Equal(0, exile.Votes);
        Assert.Null(exile.Sweep);
        Assert.Null(exile.Conclusion);
        Assert.Equal(1, dto.OpenExileIndex);

        var protection = Assert.Single(dto.Protections);
        Assert.Equal(2, protection.Seat);
        Assert.True(protection.Protected);

        Assert.Equal(4, dto.ExtraNomination?.Seat);
        Assert.Equal("Open", dto.ExtraNomination?.Status);
    }

    /// <summary>
    /// 集骨者「重获能力」窗口（R-0054 第 4 条）：死亡但重获能力的杂耍艺人**这一次持有从今天重新起算**，
    /// 因此第 2 天仍有公开猜测入口——即使他第 1 天已经猜过；没有窗口时不给（起算点回到第 1 天）。
    /// </summary>
    /// <remarks>与内核的 <c>JugglerGuessMachine</c> 同一处放宽：投影只给权限位，合法性仍由内核再判一次。</remarks>
    [Fact]
    public void JugglerGuesses_DeadButRegainedJuggler_GetsTheEntryOnTheRegainedDay()
    {
        var juggler = new SeatId(4);
        var secondDay = new DayRecord { DayNumber = 2, Status = DayStatus.Open };
        var guessedOnDayOne = new DayRecord
        {
            DayNumber = 1,
            Status = DayStatus.Closed,
            JugglerGuesses =
            [
                new JugglerGuessRecord
                {
                    Seat = juggler,
                    DayNumber = 1,
                    Guesses = [new JugglerGuess { Seat = new SeatId(2), Character = new CharacterId("deviant") }],
                },
            ],
        };
        var days = new DayState { Days = [guessedOnDayOne, secondDay] };
        var ledger = State(
            (1, "dreamer", LifeState.Alive),
            (2, "deviant", LifeState.Alive),
            (3, "bone-collector", LifeState.Alive),
            (4, "juggler", LifeState.Dead));
        var regained = GameStateMachine.Apply(
            ledger,
            new PersistentEffectAppliedEvent
            {
                Effect = new PersistentEffect
                {
                    Id = new EffectId("test:regain:4"),
                    Source = new SeatId(3),
                    Ability = new AbilityId("bone-collector.regain"),
                    Target = juggler,
                    SourceCharacter = new CharacterId("bone-collector"),
                    GrantedCharacter = new CharacterId("juggler"),
                    Window = EffectWindowKind.RegainedAbility,
                    SourceStateIndependent = true,
                },
            });

        var withWindow = Project(secondDay, seat: 4, regained, days);
        var withoutWindow = Project(secondDay, seat: 4, ledger, days);

        Assert.True(withWindow.CanMakeJugglerGuesses);
        Assert.False(withoutWindow.CanMakeJugglerGuesses);
    }

    /// <summary>
    /// 集骨者窗口**到期**之后（下个黄昏 = 新的一夜开始，<see cref="DuskExpiry"/>）：起算点回到原处
    /// ——第 3 天既不给公开猜测入口，也不认第 2 天那次窗口内的猜测（票据
    /// `review/bone-collector-regained-juggler-day-entry.md` 行 5，原先只有间接覆盖）。
    /// </summary>
    /// <remarks>
    /// 这里把"窗口到期"这一步真的走一遍（<see cref="DuskExpiry.ExpireAll"/> → 状态账折叠），
    /// 而不是手搓一个"没有窗口的账"——两者差在「窗口曾经存在、随后被终止」这条路径上。
    /// </remarks>
    [Fact]
    public void JugglerGuesses_WindowExpiresAtDusk_EntryDisappearsOnTheNextDay()
    {
        var juggler = new SeatId(4);
        var guessedOnDayOne = new DayRecord
        {
            DayNumber = 1,
            Status = DayStatus.Closed,
            JugglerGuesses =
            [
                new JugglerGuessRecord
                {
                    Seat = juggler,
                    DayNumber = 1,
                    Guesses = [new JugglerGuess { Seat = new SeatId(2), Character = new CharacterId("deviant") }],
                },
            ],
        };
        var guessedOnDayTwo = new DayRecord
        {
            DayNumber = 2,
            Status = DayStatus.Closed,
            JugglerGuesses =
            [
                new JugglerGuessRecord
                {
                    Seat = juggler,
                    DayNumber = 2,
                    Guesses = [new JugglerGuess { Seat = new SeatId(2), Character = new CharacterId("deviant") }],
                },
            ],
        };
        var thirdDay = new DayRecord { DayNumber = 3, Status = DayStatus.Open };
        var days = new DayState { Days = [guessedOnDayOne, guessedOnDayTwo, thirdDay] };
        var ledger = State(
            (1, "dreamer", LifeState.Alive),
            (2, "deviant", LifeState.Alive),
            (3, "bone-collector", LifeState.Alive),
            (4, "juggler", LifeState.Dead));
        var regained = GameStateMachine.Apply(
            ledger,
            new PersistentEffectAppliedEvent
            {
                Effect = new PersistentEffect
                {
                    Id = new EffectId("test:regain:4"),
                    Source = new SeatId(3),
                    Ability = new AbilityId("bone-collector.regain"),
                    Target = juggler,
                    SourceCharacter = new CharacterId("bone-collector"),
                    GrantedCharacter = new CharacterId("juggler"),
                    Window = EffectWindowKind.RegainedAbility,
                    SourceStateIndependent = true,
                },
            });

        var expired = DuskExpiry.ExpireAll(regained)
            .Aggregate(regained, GameStateMachine.Apply);

        Assert.True(regained.RegainedAbilityOn(juggler));
        Assert.False(expired.RegainedAbilityOn(juggler));
        Assert.False(Project(thirdDay, seat: 4, expired, days).CanMakeJugglerGuesses);
    }

    /// <summary>效果 DTO 带出窗口分类（咖啡师 / 集骨者窗口的说书人呈现面；R-0047 / R-0052 / R-0054）。</summary>
    [Fact]
    public void EffectDto_CarriesWindowKind()
    {
        var effect = new PersistentEffect
        {
            Id = new EffectId("test:bone-collector.gain:2#1"),
            Source = new SeatId(3),
            Ability = new AbilityId("bone-collector"),
            Target = new SeatId(2),
            SourceCharacter = new CharacterId("bone-collector"),
            GrantedCharacter = new CharacterId("deviant"),
            Window = EffectWindowKind.RegainedAbility,
            SourceStateIndependent = true,
        };

        Assert.Equal("RegainedAbility", SeatLedgerProjectionMapper.ToDto(effect).Window);

        var plain = effect with { Window = null, GrantedCharacter = null };
        Assert.Null(SeatLedgerProjectionMapper.ToDto(plain).Window);
    }
}
