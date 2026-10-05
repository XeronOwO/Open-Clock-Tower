using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 候选事实库的规则回归（R-0057-C）：A–E 五组事实的真值求值、参数化候选的取值、
/// 判不了就不进候选，以及候选自带的真值 / 分组 / 高强度徽章。
/// </summary>
/// <remarks>
/// 走的是公开面（<see cref="RoleContracts.SavantQuestions"/> 的提示候选），与说书人端看到的是同一份数据：
/// 真值由平台按账求值，前端不做规则判断（web/AGENTS.md §4）。
/// </remarks>
public sealed class SavantFactCatalogTests
{
    private static readonly SeatId Savant = new(1);

    /// <summary>A 组：恶魔奇偶位、与最近爪牙的距离、相邻席位的角色与阵营。</summary>
    [Fact]
    public void SeatFacts_EvaluateAgainstTheLedger()
    {
        var state = NightLedger();

        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:demon-seat-parity:even"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:demon-seat-parity:odd"));
        Assert.Equal("恶魔坐在偶数位", TextOf(state, "fact:demon-seat-parity:even"));

        // 7 席圆桌上距离最多 3（隔着最多 2 名玩家）：4 号恶魔与 5 号爪牙相邻 → 距离 1 为真，2/3 为假。
        // 口径与钟表匠同一套（距离 = 隔着的人数 + 1，最小值 1；百科《钟表匠》· 规则细节 3）。
        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:demon-minion-distance:1"));
        Assert.Equal("恶魔与最近的爪牙相邻（距离 1）", TextOf(state, "fact:demon-minion-distance:1"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:demon-minion-distance:2"));
        Assert.Equal("恶魔与最近的爪牙相距 2", TextOf(state, "fact:demon-minion-distance:2"));
        Assert.False(HasOption(state, "fact:demon-minion-distance:4"));
        Assert.False(HasOption(state, "fact:demon-minion-distance:0"));

        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:minion-beside-demon"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:minions-adjacent"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:demon-beside-outsider"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:demon-neighbours-team:good"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:demon-neighbours-team:evil"));
    }

    /// <summary>B 组：存活人数与阵营读数（按存活席位算，人数类要求生死与阵营都观测齐）。</summary>
    [Fact]
    public void NumberFacts_EvaluateAgainstTheLedger()
    {
        var state = NightLedger();

        // 死 6 / 5，7 号转为邪恶：存活 5（1 / 2 / 3 / 4 / 7），善良 3、邪恶 2。
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:evil-not-fewer"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:evil-majority"));
        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:alive-count-parity:odd"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:alive-count-parity:even"));
        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:alive-count-equals:5"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:alive-count-equals:4"));
        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:good-lead:1"));
        Assert.Equal("善良阵营比邪恶阵营多 1 名存活玩家", TextOf(state, "fact:good-lead:1"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:traveller-present"));
    }

    /// <summary>C 组：昨晚与今天的变化（读近期活动账的两个窗口）。</summary>
    [Fact]
    public void ChangeFacts_ReadTheActivityWindows()
    {
        var state = NightLedger();

        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:death-last-night"));
        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:character-changed-last-night"));
        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:alignment-changed-last-night"));
        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:malfunction-last-night"));

        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:death-today"));
        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:execution-today"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:character-changed-today"));
    }

    /// <summary>C 组：还没有结束过任何夜晚时，「昨晚」类事实判不了 → 不进候选（不猜）。</summary>
    [Fact]
    public void LastNightFacts_AreAbsentBeforeTheFirstDawn()
    {
        var state = BaseLedger();

        Assert.False(HasOption(state, "fact:death-last-night"));
        Assert.False(HasOption(state, "fact:character-changed-last-night"));
        Assert.False(HasOption(state, "fact:malfunction-last-night"));

        // 白天窗口从账的开头算起：今天没有死亡 / 处决。
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:death-today"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:execution-today"));
    }

    /// <summary>D 组：中毒 / 醉酒 / 恶魔是否受损 / 限次能力是否用尽。</summary>
    [Fact]
    public void StatusFacts_ReportWhatIsObserved()
    {
        var state = NightLedger();

        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:poisoned-present"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:drunk-present"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:demon-impaired"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:limited-ability-used"));
    }

    /// <summary>E 组：点名类（席位 / 阵营 / 角色在场与死亡 / 两席同阵营），并带「高强度」徽章。</summary>
    [Fact]
    public void AccusationFacts_NameSeatsAndRoles()
    {
        var state = NightLedger();

        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:seat-is-evil:4"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:seat-is-evil:2"));
        Assert.Equal("4 号玩家是邪恶阵营", TextOf(state, "fact:seat-is-evil:4"));

        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:two-seats-same-team:4:7"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:two-seats-same-team:2:4"));

        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:role-in-play:clockmaker"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:role-in-play:juggler"));
        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:role-dead:klutz"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:role-dead:savant"));
        Assert.Equal("角色「钟表匠」在场", TextOf(state, "fact:role-in-play:clockmaker"));

        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:seat-character:4:no-dashii"));
        Assert.Equal(OptionTruth.False, TruthOf(state, "fact:seat-character:4:artist"));
        Assert.Equal("4 号玩家的角色是「诺-达鲺」", TextOf(state, "fact:seat-character:4:no-dashii"));

        Assert.Equal("高强度", Assert.Single(Option(state, "fact:seat-is-evil:4").Tags));
        Assert.Equal("点名", Option(state, "fact:seat-is-evil:4").Group);
        Assert.Empty(Option(state, "fact:alive-count-equals:5").Tags);
    }

    /// <summary>没有唯一恶魔（一名都没有 / 不止一名）时，恶魔类事实判不了 → 不进候选。</summary>
    [Fact]
    public void DemonFacts_AreAbsentWithoutASingleDemon()
    {
        var state = BaseLedger(demon: false);

        Assert.False(HasOption(state, "fact:demon-seat-parity:odd"));
        Assert.False(HasOption(state, "fact:demon-minion-distance:1"));
        Assert.False(HasOption(state, "fact:minion-beside-demon"));
        Assert.False(HasOption(state, "fact:demon-impaired"));
    }

    /// <summary>角色维度没观测齐时，否定式断言（"场上没有 X"）判不了 → 不进候选；真话仍然给。</summary>
    [Fact]
    public void FalseRoleClaims_AreAbsentWhenCharactersAreUnobserved()
    {
        var state = BaseLedger(allCharactersKnown: false);

        Assert.Equal(OptionTruth.True, TruthOf(state, "fact:role-in-play:no-dashii"));
        Assert.False(HasOption(state, "fact:role-in-play:juggler"));
        Assert.False(HasOption(state, "fact:traveller-present"));
    }

    /// <summary>候选自带分组与真值：每一组都至少给出一条候选（说书人端据此分栏）。</summary>
    [Fact]
    public void Candidates_CarryTheirGroup()
    {
        var options = Options(NightLedger());

        foreach (var group in new[] { "座位关系", "阵营与人数", "昨晚与今天", "状态读数", "点名" })
        {
            Assert.Contains(options, option => option.Group == group);
        }

        Assert.All(options, option => Assert.NotNull(option.Truth));
        Assert.All(options, option => Assert.StartsWith("fact:", option.Value, StringComparison.Ordinal));
        Assert.All(options, option => Assert.False(string.IsNullOrWhiteSpace(option.Code)));
    }

    /// <summary>
    /// 互斥组随候选项下发（说书人端据此把"与另一槽位互斥"的那条预先灰掉）。组的不变量是两件事：
    /// 组里至少两个不同取值；**同一个编码在组里至多一条为真**（同一条事实的两个取值必然一真一假）。
    /// 组名可以跨编码——那是"同一个事实的两种说法"（见下一个用例）。
    /// </summary>
    [Fact]
    public void Candidates_CarryTheirExclusionGroup()
    {
        var options = Options(NightLedger());
        var grouped = options.Where(option => option.ExclusionGroup is not null).ToArray();

        Assert.NotEmpty(grouped);

        foreach (var family in grouped.GroupBy(option => option.ExclusionGroup!, StringComparer.Ordinal))
        {
            Assert.True(
                family.Select(option => option.Value).Distinct(StringComparer.Ordinal).Count() >= 2,
                $"互斥组 {family.Key} 里只有一条候选：灰掉它等于把唯一的选择也灰了");

            foreach (var byCode in family.GroupBy(option => option.Code, StringComparer.Ordinal))
            {
                Assert.True(
                    byCode.Count(option => option.Truth == OptionTruth.True) <= 1,
                    $"互斥组 {family.Key} 里 {byCode.Key} 有不止一条为真：同一条事实的两个取值是反面对");
            }
        }

        // 奇偶那一对是反面对：两条、取值不同、恰好一真一假。
        var parity = options.Where(option => option.ExclusionGroup == "demon-seat-parity").ToArray();
        Assert.Equal(2, parity.Length);
        Assert.Single(parity, option => option.Truth == OptionTruth.True);

        // 多取值的事实（爪牙距离 / 存活人数 / 席位阵营）里只有"奇偶"那一对是反面对，
        // 其余取值的互斥组为 null——前端据此只灰掉真正互为反面的那一条。
        foreach (var code in new[] { "demon-minion-distance", "alive-count-equals", "seat-is-evil" })
        {
            var family = options.Where(option => option.Code == code).ToArray();
            Assert.Contains(family, option => option.ExclusionGroup is null);
        }
    }

    /// <summary>
    /// 互斥组可以**跨编码**：爪牙距离 1（`demon-minion-distance:1`）与「恶魔旁边有爪牙」
    /// （`minion-beside-demon`）是同一个事实的两种说法，因此同组——说书人端把两者互相灰掉，
    /// 服务端在双真时也会拒（R-0057-C 的 C1）。
    /// </summary>
    [Fact]
    public void DemonAdjacencyAlternatives_ShareOneExclusionGroup()
    {
        var state = NightLedger();

        var beside = Option(state, "fact:minion-beside-demon");
        var adjacent = Option(state, "fact:demon-minion-distance:1");

        Assert.Equal("demon-minion-adjacency", beside.ExclusionGroup);
        Assert.Equal(beside.ExclusionGroup, adjacent.ExclusionGroup);

        // 两者在可判定的账上真值恒等（相邻 ⇔ 某个邻居是爪牙），互斥组表达的就是这件事；
        // 同组里**恰好**这两条为真——服务端据此在双真时拒绝。
        var trueInGroup = Options(state)
            .Where(option => option.ExclusionGroup == "demon-minion-adjacency"
                && option.Truth == OptionTruth.True)
            .Select(option => option.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { "fact:demon-minion-distance:1", "fact:minion-beside-demon" },
            trueInGroup);
    }

    private static IReadOnlyList<DecisionOption> Options(GameState state) =>
        RoleContracts.SavantQuestions
            .Single(source => source.Character == new CharacterId("savant"))
            .BuildPrompt(new SavantPromptContext { Seat = Savant, State = state, Seats = Seats() })
            .Options;

    private static DecisionOption Option(GameState state, string value) =>
        Assert.Single(Options(state), option => option.Value == value);

    private static bool HasOption(GameState state, string value) =>
        Options(state).Any(option => option.Value == value);

    private static string TextOf(GameState state, string value) => Option(state, value).Preview;

    private static OptionTruth? TruthOf(GameState state, string value) => Option(state, value).Truth;

    private static IReadOnlyList<SeatId> Seats() =>
        [.. Enumerable.Range(1, 7).Select(value => new SeatId(value))];

    /// <summary>七席开局账：1 博学者 / 2 钟表匠（中毒）/ 3 筑梦师 / 4 诺-达鲺 / 5 女巫 / 6 呆瓜 / 7 艺术家。</summary>
    private static GameState BaseLedger(bool demon = true, bool allCharactersKnown = true) =>
        GameStateMachine.Fold(Setup(demon, allCharactersKnown));

    /// <summary>
    /// 走完一夜再进白天：夜里 6 号死亡、3 号被理发师换角、7 号阵营被侵染、2 号能力未正常生效；
    /// 黎明后 5 号被处决并死亡。
    /// </summary>
    private static GameState NightLedger()
    {
        var events = new List<GameEvent>(Setup(demon: true, allCharactersKnown: true))
        {
            new PhaseStartedEvent
            {
                Plan = new StepPlan
                {
                    Label = "sv:night-2",
                    Phase = GamePhase.OtherNight,
                    Slots = [],
                },
                Control = ControlMode.Automatic,
            },
            new SeatStateChangedEvent { Seat = new SeatId(6), Life = LifeState.Dead, Reason = "被恶魔击杀" },
            new SeatStateChangedEvent
            {
                Seat = new SeatId(3),
                Character = new CharacterId("sage"),
                Reason = "理发师交换",
            },
            new SeatStateChangedEvent { Seat = new SeatId(7), Alignment = Alignment.Evil, Reason = "方古侵染" },
            new AbilityResolvedEvent
            {
                SlotId = new StepSlotId("sv:clockmaker"),
                Actor = new SeatId(2),
                Ability = new AbilityId("clockmaker"),
                Effective = false,
                Malfunctions = [MalfunctionKind.Poisoned],
                Note = "来源中毒：能力未生效",
            },
            new DayStartedEvent { DayNumber = 1 },
            new ExecutedEvent { DayNumber = 1, Seat = new SeatId(5), Kind = ExecutionKind.Day },
            new SeatStateChangedEvent { Seat = new SeatId(5), Life = LifeState.Dead, Reason = "被处决" },
        };

        return GameStateMachine.Fold(events);
    }

    private static IEnumerable<GameEvent> Setup(bool demon, bool allCharactersKnown)
    {
        var rows = new (int Seat, string Character, Alignment Alignment)[]
        {
            (1, "savant", Alignment.Good),
            (2, "clockmaker", Alignment.Good),
            (3, "dreamer", Alignment.Good),
            (4, demon ? "no-dashii" : "artist", Alignment.Evil),
            (5, "witch", Alignment.Evil),
            (6, "klutz", Alignment.Good),
            (7, "artist", Alignment.Good),
        };

        return rows.Select(row => (GameEvent)new SeatStateChangedEvent
        {
            Seat = new SeatId(row.Seat),
            Character = allCharactersKnown || row.Seat == 4 ? new CharacterId(row.Character) : null,
            Alignment = row.Alignment,
            Life = LifeState.Alive,
            Drunk = DrunkState.Sober,
            Poison = row.Seat == 2 ? PoisonState.Poisoned : PoisonState.Healthy,
            Reason = "测试夹具",
        });
    }
}
