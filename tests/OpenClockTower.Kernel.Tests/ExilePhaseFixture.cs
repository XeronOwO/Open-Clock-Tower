using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 流放用例的共享夹具（票据 `traveller-and-exile` · D2）：带角色观测的账、角色事实端口
/// 与六个流放输入的便捷包装；判定本身走生产同一套 <see cref="StepMachine"/>。
/// </summary>
/// <remarks>
/// 五个旅行者 slug 与首版花名册一致；这里只实现"是不是旅行者"这一条判定所需的窄面，
/// 不复制规则层花名册（内核不认识剧本数据，事实端口由规则层提供——测试里等价地给一个假端口）。
/// </remarks>
internal static class ExilePhaseFixture
{
    /// <summary>默认节奏参数（判定不读，只进事件流；R-0017 第 6 条）。</summary>
    internal const int DefaultCountdownMilliseconds = VoteSweepLimits.DefaultCountdownMilliseconds;

    /// <summary>默认逐席间隔（毫秒）。</summary>
    internal const int DefaultIntervalMilliseconds = VoteSweepLimits.DefaultIntervalMilliseconds;

    private static readonly HashSet<string> TravellerSlugs =
        new(StringComparer.Ordinal) { "deviant", "bone-collector", "barista", "harlot", "butcher" };

    /// <summary>按"席位 + 角色 + 生死"构造状态账（其余维度不观测；角色为 null = 未观测）。</summary>
    internal static GameState StateOf(params (int Seat, string? Character, LifeState Life)[] seats) =>
        GameStateMachine.Fold(
        [
            .. seats.Select(item => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(item.Seat),
                Character = item.Character is null ? null : new CharacterId(item.Character),
                Life = item.Life,
                Reason = "test.exile.setup",
            }),
        ]);

    /// <summary>用现成的状态账构造上下文（座次 = 给定的在局席位，按升序）。</summary>
    internal static SettlementContext ContextOf(GameState state, params int[] inGameSeats) => new()
    {
        State = state,
        Seats = [.. inGameSeats.Select(value => new SeatId(value)).OrderBy(seat => seat.Value)],
        Abilities = NoAbilities.Instance,
        Characters = TestCharacterFacts.Instance,
    };

    /// <summary>按"席位 + 角色 + 生死"构造上下文（座次 = 同批席位）。</summary>
    internal static SettlementContext Context(params (int Seat, string? Character, LifeState Life)[] seats) =>
        ContextOf(StateOf(seats), [.. seats.Select(item => item.Seat)]);

    /// <summary>同样的账与座次，但不给角色事实端口（验证"契约缺失就拒绝，不猜"）。</summary>
    internal static SettlementContext ContextWithoutCharacterFacts(
        params (int Seat, string? Character, LifeState Life)[] seats) =>
        Context(seats) with { Characters = null };

    /// <summary>开一个白天（与生产一致的唯一 DayWindow 计划）。</summary>
    internal static StepMachineState StartDay(int dayNumber = 1) => DayPhaseFixture.StartDay(dayNumber);

    /// <summary>已关闭的白天（白天账保留、没有进行中的白天）。</summary>
    internal static StepMachineState ClosedDay() =>
        StepMachine.Apply(DayPhaseFixture.StartDay(), new DayClosedEvent { DayNumber = 1 })
        ?? throw new InvalidOperationException("关闭白天后丢失步骤机状态");

    /// <summary>发起流放提议。</summary>
    internal static StepMachineOutcome Propose(
        StepMachineState state,
        SettlementContext context,
        int proposer,
        int target) =>
        DayPhaseFixture.Apply(state, context, new ProposeExileInput
        {
            Proposer = new SeatId(proposer),
            Target = new SeatId(target),
        });

    /// <summary>开始流放收票。</summary>
    internal static StepMachineOutcome StartSweep(
        StepMachineState state,
        SettlementContext context,
        int index,
        int countdownMilliseconds = DefaultCountdownMilliseconds,
        int intervalMilliseconds = DefaultIntervalMilliseconds) =>
        DayPhaseFixture.Apply(state, context, new StartExileSweepInput
        {
            ExileIndex = index,
            CountdownMilliseconds = countdownMilliseconds,
            IntervalMilliseconds = intervalMilliseconds,
        });

    /// <summary>举手 / 放下。</summary>
    internal static StepMachineOutcome Vote(
        StepMachineState state,
        SettlementContext context,
        int voter,
        int index,
        bool voted) =>
        DayPhaseFixture.Apply(state, context, new CastExileVoteInput
        {
            Voter = new SeatId(voter),
            ExileIndex = index,
            Voted = voted,
        });

    /// <summary>收第 N 席的票（控制面到点）。</summary>
    internal static StepMachineOutcome Collect(
        StepMachineState state,
        SettlementContext context,
        int index,
        int seat) =>
        DayPhaseFixture.Apply(state, context, new CollectExileSeatVoteInput
        {
            ExileIndex = index,
            Seat = new SeatId(seat),
        });

    /// <summary>继续中断的流放收票。</summary>
    internal static StepMachineOutcome ResumeSweep(StepMachineState state, SettlementContext context, int index) =>
        DayPhaseFixture.Apply(state, context, new ResumeExileSweepInput { ExileIndex = index });

    /// <summary>计票。</summary>
    internal static StepMachineOutcome Count(StepMachineState state, SettlementContext context, int index) =>
        DayPhaseFixture.Apply(state, context, new CountExileVotesInput { ExileIndex = index });

    /// <summary>把一条流放收票按座次收完（已收过的席位跳过；座次取上下文当前名单）。</summary>
    internal static StepMachineOutcome CollectAll(StepMachineState state, SettlementContext context, int index)
    {
        var events = new List<GameEvent>();
        var after = state;
        foreach (var seat in context.Seats)
        {
            var alreadyCollected = after.Day?.OpenDay?.OpenExile?.Sweep?
                .Collected.Any(vote => vote.Seat == seat) ?? false;
            if (alreadyCollected)
            {
                continue;
            }

            var outcome = Collect(after, context, index, seat.Value);
            Assert.True(outcome.Kind == StepMachineOutcomeKind.Applied, $"收票被拒：{outcome.RejectionCode}");
            events.AddRange(outcome.Events);
            after = outcome.State;
        }

        return new StepMachineOutcome
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = after,
            Events = events,
        };
    }

    /// <summary>开始收票 → 指定席位举手 → 按座次收完；返回收完后的状态。</summary>
    internal static StepMachineState RunSweep(
        StepMachineState state,
        SettlementContext context,
        int index,
        params int[] raised)
    {
        var started = StartSweep(state, context, index);
        Assert.True(started.Kind == StepMachineOutcomeKind.Applied, $"开始收票被拒：{started.RejectionCode}");
        var after = started.State;

        foreach (var seat in raised)
        {
            var outcome = Vote(after, context, seat, index, voted: true);
            Assert.True(outcome.Kind == StepMachineOutcomeKind.Applied, $"举手被拒：{outcome.RejectionCode}");
            after = outcome.State;
        }

        return CollectAll(after, context, index).State;
    }

    /// <summary>走完收票并计票（最常用的一步到位）。</summary>
    internal static StepMachineOutcome SweepAndCount(
        StepMachineState state,
        SettlementContext context,
        int index,
        params int[] raised) =>
        Count(RunSweep(state, context, index, raised), context, index);

    private sealed class NoAbilities : IAbilityResolutionCatalog
    {
        internal static readonly NoAbilities Instance = new();

        public IAbilityResolution? Find(CharacterId character) => null;
    }

    private sealed class TestCharacterFacts : IWinConditionFacts
    {
        internal static readonly TestCharacterFacts Instance = new();

        public bool IsDemon(CharacterId character) => false;

        public bool IsTraveller(CharacterId character) => TravellerSlugs.Contains(character.Value);

        public bool IsVortox(CharacterId character) => false;

        public bool IsKlutz(CharacterId character) => false;

        public bool IsEvilTwinPair(AbilityId ability) => false;
    }
}
