using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 胜负求值的规则回归：常规条件、特殊条件、优先级与"镜像双子阻断"（R-0024 / R-0025 / R-0026 / R-0027）。
/// </summary>
/// <remarks>
/// 全部是纯函数用例：不建真宿主、不掷时间。规则来源见各用例注释（百科页 + 抓取日期）。
/// 角色事实端口用测试替身，避免把 Rules 的花名册耦合进内核用例。
/// </remarks>
public sealed class OutcomeEvaluatorTests
{
    private sealed class FakeFacts : IWinConditionFacts
    {
        internal static readonly FakeFacts Instance = new();

        public bool IsDemon(CharacterId character) => character.Value is "no-dashii" or "vortox";

        public bool IsTraveller(CharacterId character) => character.Value is "deviant" or "barista" or "butcher";

        public bool IsVortox(CharacterId character) => character.Value == "vortox";

        public bool IsKlutz(CharacterId character) => character.Value == "klutz";

        public bool IsEvilTwinPair(AbilityId ability) => ability.Value == "evil-twin.pair";
    }

    /// <summary>常规 · 善良：最后一名恶魔死亡 → 善良获胜（《规则概要》四）。</summary>
    [Fact]
    public void DemonsAllDead_GoodWins()
    {
        var state = State(
            (1, "clockmaker", Alignment.Good, LifeState.Alive),
            (2, "no-dashii", Alignment.Evil, LifeState.Dead));

        var outcome = OutcomeEvaluator.Evaluate(Context(state));

        Assert.NotNull(outcome);
        Assert.Equal(Alignment.Good, outcome!.Winner);
        Assert.Equal(OutcomeCondition.DemonsAllDead, outcome.Condition);
    }

    /// <summary>恶魔还活着 → 不结束；观测不齐（席位缺生死）→ 不猜、不结束。</summary>
    [Fact]
    public void AliveDemon_OrUnobservedSeat_DoesNotEnd()
    {
        var aliveDemon = State(
            (1, "clockmaker", Alignment.Good, LifeState.Alive),
            (2, "no-dashii", Alignment.Evil, LifeState.Alive),
            (3, "dreamer", Alignment.Good, LifeState.Alive),
            (4, "sage", Alignment.Good, LifeState.Alive));
        Assert.Null(OutcomeEvaluator.Evaluate(Context(aliveDemon)));

        var incomplete = State(
            (1, "clockmaker", Alignment.Good, LifeState.Alive),
            (2, "no-dashii", Alignment.Evil, LifeState.Alive),
            (3, "dreamer", Alignment.Good, LifeState.Alive),
            (4, "sage", Alignment.Good, LifeState.Alive));
        incomplete = incomplete with { Seats = [.. incomplete.Seats.Take(3)] };
        var context = new OutcomeContext
        {
            State = incomplete,
            Seats = [new SeatId(1), new SeatId(2), new SeatId(3), new SeatId(4)],
            Characters = FakeFacts.Instance,
        };
        Assert.Null(OutcomeEvaluator.Evaluate(context));
    }

    /// <summary>没有任何恶魔角色 → 不算"所有恶魔均死亡"（防止配置错误被静默判成一局结束）。</summary>
    [Fact]
    public void NoDemonsAtAll_DoesNotEnd()
    {
        var state = State(
            (1, "clockmaker", Alignment.Good, LifeState.Alive),
            (2, "dreamer", Alignment.Good, LifeState.Alive),
            (3, "klutz", Alignment.Good, LifeState.Alive));

        Assert.Null(OutcomeEvaluator.Evaluate(Context(state)));
    }

    /// <summary>常规 · 邪恶：场上仅剩两名玩家存活（《规则概要》四；一步跨过 2 也算，R-0008）。</summary>
    [Fact]
    public void TwoPlayersAlive_EvilWins()
    {
        var state = State(
            (1, "clockmaker", Alignment.Good, LifeState.Alive),
            (2, "no-dashii", Alignment.Evil, LifeState.Alive),
            (3, "dreamer", Alignment.Good, LifeState.Dead));

        var outcome = OutcomeEvaluator.Evaluate(Context(state));

        Assert.NotNull(outcome);
        Assert.Equal(Alignment.Evil, outcome!.Winner);
        Assert.Equal(OutcomeCondition.TwoPlayersAlive, outcome.Condition);
    }

    /// <summary>
    /// R-0045 第 4 条 / 百科《旅行者》· 2026-10-04 抓取：「仅有两名玩家存活」不计旅行者——
    /// 5 人存活里有 3 名旅行者时，非旅行者存活数是 2，邪恶获胜；非旅行者 3 名时条件不成立。
    /// </summary>
    [Fact]
    public void TwoPlayersAlive_DoesNotCountTravellers()
    {
        var twoNonTravellersAlive = State(
            (1, "clockmaker", Alignment.Good, LifeState.Alive),
            (2, "dreamer", Alignment.Good, LifeState.Alive),
            (3, "deviant", Alignment.Good, LifeState.Alive),
            (4, "barista", Alignment.Evil, LifeState.Alive),
            (5, "butcher", Alignment.Evil, LifeState.Dead));

        var outcome = OutcomeEvaluator.Evaluate(Context(twoNonTravellersAlive));

        Assert.NotNull(outcome);
        Assert.Equal(Alignment.Evil, outcome!.Winner);
        Assert.Equal(OutcomeCondition.TwoPlayersAlive, outcome.Condition);

        var threeNonTravellersAlive = State(
            (1, "clockmaker", Alignment.Good, LifeState.Alive),
            (2, "dreamer", Alignment.Good, LifeState.Alive),
            (3, "sage", Alignment.Good, LifeState.Alive),
            (4, "deviant", Alignment.Good, LifeState.Alive),
            (5, "barista", Alignment.Evil, LifeState.Dead));

        Assert.Null(OutcomeEvaluator.Evaluate(Context(threeNonTravellersAlive)));
    }

    /// <summary>同层同时满足 → 善良获胜：恶魔死亡 + 仅剩两名存活同时成立时善良胜（《规则概要》四）。</summary>
    [Fact]
    public void BothRegularConditions_GoodWinsTheTie()
    {
        var state = State(
            (1, "clockmaker", Alignment.Good, LifeState.Alive),
            (2, "no-dashii", Alignment.Evil, LifeState.Dead));

        var outcome = OutcomeEvaluator.Evaluate(Context(state));

        Assert.NotNull(outcome);
        Assert.Equal(Alignment.Good, outcome!.Winner);
        Assert.Equal(OutcomeCondition.DemonsAllDead, outcome.Condition);
    }

    /// <summary>镜像双子：配对生效且两名双子都存活 → 善良无法获胜，恶魔全死也不结束（《镜像双子》）。</summary>
    [Fact]
    public void EvilTwinPair_BlocksGoodWin_WhileBothAlive()
    {
        var state = State(
            (1, "evil-twin", Alignment.Evil, LifeState.Alive),
            (2, "clockmaker", Alignment.Good, LifeState.Alive),
            (3, "no-dashii", Alignment.Evil, LifeState.Dead),
            (4, "dreamer", Alignment.Good, LifeState.Alive));
        state = state with { PersistentEffects = [PairEffect(source: 1, target: 2)] };

        Assert.Null(OutcomeEvaluator.Evaluate(Context(state)));

        // 配对终止（镜像双子死亡）→ 阻断消失，善良按常规条件获胜。
        var terminated = state with
        {
            PersistentEffects =
            [
                state.PersistentEffects[0].Terminate(new EffectTermination
                {
                    Kind = EffectTerminationKind.SourceDied,
                    Reason = "测试：来源死亡",
                }),
            ],
        };
        var outcome = OutcomeEvaluator.Evaluate(Context(terminated));
        Assert.NotNull(outcome);
        Assert.Equal(Alignment.Good, outcome!.Winner);
    }

    /// <summary>镜像双子：善良方被处决 → 邪恶立即获胜；处决事实即可，不看是否死亡（R-0025 第 3 条）。</summary>
    [Fact]
    public void GoodTwinExecuted_EvilWins()
    {
        var state = State(
            (1, "evil-twin", Alignment.Evil, LifeState.Alive),
            (2, "clockmaker", Alignment.Good, LifeState.Alive),
            (3, "no-dashii", Alignment.Evil, LifeState.Alive));
        state = state with { PersistentEffects = [PairEffect(source: 1, target: 2)] };

        var outcome = OutcomeEvaluator.Evaluate(Context(
            state,
            new ExecutedEvent { DayNumber = 1, Seat = new SeatId(2), Kind = ExecutionKind.Day }));

        Assert.NotNull(outcome);
        Assert.Equal(Alignment.Evil, outcome!.Winner);
        Assert.Equal(OutcomeCondition.EvilTwinGoodTwinExecuted, outcome.Condition);
    }

    /// <summary>处决的是邪恶方 → 游戏继续；配对来源已死 → 不再触发（《镜像双子》角色简介）。</summary>
    [Fact]
    public void EvilTwinExecuted_OrDeadSource_DoesNotEnd()
    {
        var state = State(
            (1, "evil-twin", Alignment.Evil, LifeState.Alive),
            (2, "clockmaker", Alignment.Good, LifeState.Alive),
            (3, "no-dashii", Alignment.Evil, LifeState.Alive),
            (4, "dreamer", Alignment.Good, LifeState.Alive));
        state = state with { PersistentEffects = [PairEffect(source: 1, target: 2)] };

        Assert.Null(OutcomeEvaluator.Evaluate(Context(
            state,
            new ExecutedEvent { DayNumber = 1, Seat = new SeatId(1), Kind = ExecutionKind.Day })));

        var deadSource = state with
        {
            Seats = [.. state.Seats.Select(entry => entry.Seat == new SeatId(1)
                ? entry with { Life = Fact(LifeState.Dead) }
                : entry)],
        };
        Assert.Null(OutcomeEvaluator.Evaluate(Context(
            deadSource,
            new ExecutedEvent { DayNumber = 1, Seat = new SeatId(2), Kind = ExecutionKind.Day })));
    }

    /// <summary>涡流：黄昏无人被处决 → 邪恶获胜（《涡流》运作方式）；有处决 / 涡流已死 / 观测不齐都不触发。</summary>
    [Fact]
    public void VortoxNoExecution_EvilWins_OnlyWhenAliveAndNoExecution()
    {
        var state = State(
            (1, "vortox", Alignment.Evil, LifeState.Alive),
            (2, "clockmaker", Alignment.Good, LifeState.Alive),
            (3, "dreamer", Alignment.Good, LifeState.Alive),
            (4, "sage", Alignment.Good, LifeState.Alive));
        var day = new DayState
        {
            Days = [new DayRecord { DayNumber = 1, Status = DayStatus.Closed }],
        };

        var outcome = OutcomeEvaluator.Evaluate(Context(
            state,
            day,
            new DayClosedEvent { DayNumber = 1 }));

        Assert.NotNull(outcome);
        Assert.Equal(Alignment.Evil, outcome!.Winner);
        Assert.Equal(OutcomeCondition.VortoxNoExecution, outcome.Condition);

        // 有处决（处决 ≠ 死亡：Executions 非空即可）→ 不触发。
        var executedDay = day with
        {
            Days =
            [
                new DayRecord
                {
                    DayNumber = 1,
                    Status = DayStatus.Closed,
                    Executions = [new DayExecution { Seat = new SeatId(3), Kind = ExecutionKind.Day }],
                },
            ],
        };
        Assert.Null(OutcomeEvaluator.Evaluate(Context(
            state,
            executedDay,
            new DayClosedEvent { DayNumber = 1 })));

        // 涡流能力不生效（醉酒 / 中毒）→ 不触发（R-0026 第 4 条登记的平台口径）。
        var drunkVortox = state with
        {
            Seats = [.. state.Seats.Select(entry => entry.Seat == new SeatId(1)
                ? entry with { Drunk = Fact(DrunkState.Drunk) }
                : entry)],
        };
        Assert.Null(OutcomeEvaluator.Evaluate(Context(drunkVortox, day, new DayClosedEvent { DayNumber = 1 })));
    }

    /// <summary>呆瓜：选到邪恶 → 其阵营落败；邪恶呆瓜选邪恶 → 善良获胜；选善良 → 无事（《呆瓜》）。</summary>
    [Fact]
    public void KlutzChoice_FactionLoses()
    {
        var state = State(
            (1, "klutz", Alignment.Good, LifeState.Dead),
            (2, "no-dashii", Alignment.Evil, LifeState.Alive),
            (3, "clockmaker", Alignment.Good, LifeState.Alive),
            (4, "dreamer", Alignment.Good, LifeState.Alive));

        var evilWins = OutcomeEvaluator.Evaluate(Context(
            state,
            new KlutzChoiceMadeEvent { Klutz = new SeatId(1), Target = new SeatId(2) }));
        Assert.NotNull(evilWins);
        Assert.Equal(Alignment.Evil, evilWins!.Winner);
        Assert.Equal(OutcomeCondition.KlutzChoiceFactionLoses, evilWins.Condition);

        // 邪恶呆瓜（阵营转变）选到邪恶 → 改为善良获胜。
        var evilKlutz = state with
        {
            Seats = [.. state.Seats.Select(entry => entry.Seat == new SeatId(1)
                ? entry with { Alignment = Fact(Alignment.Evil) }
                : entry)],
        };
        var goodWins = OutcomeEvaluator.Evaluate(Context(
            evilKlutz,
            new KlutzChoiceMadeEvent { Klutz = new SeatId(1), Target = new SeatId(2) }));
        Assert.NotNull(goodWins);
        Assert.Equal(Alignment.Good, goodWins!.Winner);

        // 选到善良 → 无事发生。
        Assert.Null(OutcomeEvaluator.Evaluate(Context(
            state,
            new KlutzChoiceMadeEvent { Klutz = new SeatId(1), Target = new SeatId(3) })));
    }

    /// <summary>特殊条件优先于常规条件：呆瓜选中邪恶的同时恶魔全死 → 邪恶获胜（《特殊胜利失败条件》）。</summary>
    [Fact]
    public void SpecialCondition_BeatsRegularCondition()
    {
        var state = State(
            (1, "klutz", Alignment.Good, LifeState.Dead),
            (2, "no-dashii", Alignment.Evil, LifeState.Alive),
            (3, "vortox", Alignment.Evil, LifeState.Dead),
            (4, "dreamer", Alignment.Good, LifeState.Alive));

        var outcome = OutcomeEvaluator.Evaluate(Context(
            state,
            new KlutzChoiceMadeEvent { Klutz = new SeatId(1), Target = new SeatId(2) }));

        Assert.NotNull(outcome);
        Assert.Equal(Alignment.Evil, outcome!.Winner);
        Assert.Equal(OutcomeCondition.KlutzChoiceFactionLoses, outcome.Condition);
    }

    /// <summary>双方同时满足特殊条件 → 善良获胜（《特殊胜利失败条件》）。</summary>
    [Fact]
    public void BothSpecialConditions_GoodWinsTheTie()
    {
        var state = State(
            (1, "klutz", Alignment.Evil, LifeState.Dead),
            (2, "no-dashii", Alignment.Evil, LifeState.Alive),
            (3, "dreamer", Alignment.Good, LifeState.Alive),
            (4, "clockmaker", Alignment.Good, LifeState.Alive));
        var day = new DayState
        {
            Days = [new DayRecord { DayNumber = 1, Status = DayStatus.Closed }],
        };

        // 同时：邪恶呆瓜选中邪恶（善良特殊胜利）+ 涡流黄昏无人被处决（邪恶特殊胜利）。
        state = state with
        {
            Seats = [.. state.Seats.Select(entry => entry.Seat == new SeatId(2)
                ? entry with { Character = Fact(new CharacterId("vortox")) }
                : entry)],
        };
        var outcome = OutcomeEvaluator.Evaluate(Context(
            state,
            day,
            new KlutzChoiceMadeEvent { Klutz = new SeatId(1), Target = new SeatId(2) },
            new DayClosedEvent { DayNumber = 1 }));

        Assert.NotNull(outcome);
        Assert.Equal(Alignment.Good, outcome!.Winner);
    }

    /// <summary>
    /// R-0029：运行期「恶魔清零」——麻脸巫婆把最后一名恶魔变成非恶魔角色（人还活着）→ 善良获胜。
    /// 判据是本批事件里「恶魔 → 非恶魔」的角色变化（<see cref="SeatStateChangedEvent.PreviousCharacter"/>）。
    /// </summary>
    [Fact]
    public void DemonChangedIntoNonDemon_GoodWins()
    {
        var state = State(
            (1, "clockmaker", Alignment.Good, LifeState.Alive),
            (2, "sweetheart", Alignment.Evil, LifeState.Alive),
            (3, "dreamer", Alignment.Good, LifeState.Alive),
            (4, "sage", Alignment.Good, LifeState.Alive));

        var outcome = OutcomeEvaluator.Evaluate(Context(
            state,
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Character = new CharacterId("sweetheart"),
                PreviousCharacter = new CharacterId("no-dashii"),
                Reason = "麻脸巫婆角色变更",
                CausedBy = new SeatId(1),
            }));

        Assert.NotNull(outcome);
        Assert.Equal(Alignment.Good, outcome!.Winner);
        Assert.Equal(OutcomeCondition.DemonsAllDead, outcome.Condition);
    }

    /// <summary>R-0029：恶魔 → 另一种恶魔不算清零（场上仍有活着的恶魔）。</summary>
    [Fact]
    public void DemonChangedIntoAnotherDemon_DoesNotEnd()
    {
        var state = State(
            (1, "clockmaker", Alignment.Good, LifeState.Alive),
            (2, "vortox", Alignment.Evil, LifeState.Alive),
            (3, "dreamer", Alignment.Good, LifeState.Alive),
            (4, "sage", Alignment.Good, LifeState.Alive));

        Assert.Null(OutcomeEvaluator.Evaluate(Context(
            state,
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Character = new CharacterId("vortox"),
                PreviousCharacter = new CharacterId("no-dashii"),
                Reason = "麻脸巫婆角色变更",
            })));
    }

    /// <summary>R-0029：一名恶魔被变成非恶魔，但场上还有另一名活着的恶魔 → 不结束。</summary>
    [Fact]
    public void DemonChangedIntoNonDemon_WhileAnotherDemonAlive_DoesNotEnd()
    {
        var state = State(
            (1, "vortox", Alignment.Evil, LifeState.Alive),
            (2, "sweetheart", Alignment.Evil, LifeState.Alive),
            (3, "dreamer", Alignment.Good, LifeState.Alive),
            (4, "sage", Alignment.Good, LifeState.Alive));

        Assert.Null(OutcomeEvaluator.Evaluate(Context(
            state,
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Character = new CharacterId("sweetheart"),
                PreviousCharacter = new CharacterId("no-dashii"),
                Reason = "麻脸巫婆角色变更",
            })));
    }

    /// <summary>
    /// R-0029 第 2 条：本局从未配置恶魔（配置异常 / 非剧本夹具）时，即使有角色变更也不判结束——
    /// 与「运行期清零」区分开的正是「变化前是恶魔」这一事实。
    /// </summary>
    [Fact]
    public void NoDemonConfigured_WithUnrelatedCharacterChange_DoesNotEnd()
    {
        var state = State(
            (1, "clockmaker", Alignment.Good, LifeState.Alive),
            (2, "dreamer", Alignment.Good, LifeState.Alive),
            (3, "sage", Alignment.Good, LifeState.Alive));

        Assert.Null(OutcomeEvaluator.Evaluate(Context(
            state,
            new SeatStateChangedEvent
            {
                Seat = new SeatId(3),
                Character = new CharacterId("sage"),
                PreviousCharacter = new CharacterId("artist"),
                Reason = "说书人上报",
            })));
    }

    private static OutcomeContext Context(GameState state, params GameEvent[] events) =>
        Context(state, day: null, events);

    private static OutcomeContext Context(GameState state, DayState? day, params GameEvent[] events) =>
        new()
        {
            State = state,
            Seats = [.. state.Seats.Select(entry => entry.Seat)],
            Characters = FakeFacts.Instance,
            Day = day,
            Events = events,
        };

    private static GameState State(params (int Seat, string Character, Alignment Alignment, LifeState Life)[] rows) =>
        new()
        {
            Seats =
            [
                .. rows.Select(row => new SeatStateEntry
                {
                    Seat = new SeatId(row.Seat),
                    Character = Fact(new CharacterId(row.Character)),
                    Alignment = Fact(row.Alignment),
                    Life = Fact(row.Life),
                    Drunk = Fact(DrunkState.Sober),
                    Poison = Fact(PoisonState.Healthy),
                }),
            ],
        };

    private static StateFact<T> Fact<T>(T value)
        where T : struct =>
        new()
        {
            Value = value,
            Reason = "测试夹具",
        };

    private static PersistentEffect PairEffect(int source, int target) =>
        new()
        {
            Id = new EffectId("test:pair"),
            Source = new SeatId(source),
            Ability = new AbilityId("evil-twin.pair"),
            Target = new SeatId(target),
            SourceCharacter = new CharacterId("evil-twin"),
            Dimension = null,
        };
}
