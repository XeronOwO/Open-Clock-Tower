using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 数学家（R-0004 / R-0028）：窗口推演（上一个黎明起、按玩家去重、不含本人）与说书人裁定面
/// （数字由说书人给出；涡流在场必须为假）。
/// </summary>
public sealed class MathematicianTests
{
    private static readonly SeatId MathematicianSeat = new(1);
    private static readonly CharacterId Mathematician = new("mathematician");
    private static readonly CharacterId Vortox = new("vortox");
    private static readonly AbilityId InfoAbility = new("mathematician");

    /// <summary>建表与结算两个目录都要按 slug 取到数学家——同一份注册，避免两条入口分叉。</summary>
    [Fact]
    public void Contract_IsRegistered_ForBuildingAndSettlement()
    {
        var action = NightActions.Default.Find(Mathematician);

        Assert.NotNull(action);
        Assert.Equal(Mathematician, action!.Character);
        Assert.NotNull(NightActions.Resolutions.Find(Mathematician));
    }

    /// <summary>推演 = 窗口内计入分类的玩家去重数，不含数学家本人（R-0004 第 1 / 2 / 3 条）。</summary>
    [Fact]
    public void Prompt_CountsWindowedPlayers_ExcludingSelf()
    {
        var prompt = Prompt(State(
            sinceDawnStart: 1,
            Malfunction(seat: 9, MalfunctionKind.Poisoned),   // 窗口外（上个黎明之前）
            Malfunction(seat: 2, MalfunctionKind.Poisoned),
            Malfunction(seat: 2, MalfunctionKind.Drunk),      // 同一玩家只算 1
            Malfunction(seat: 3, MalfunctionKind.Jinx),       // R-0004 对表：不计入
            Malfunction(seat: 1, MalfunctionKind.Drunk),      // 数学家本人：不计
            Malfunction(seat: 4, MalfunctionKind.Vortox)));

        Assert.Contains("推演：2", prompt.Context, StringComparison.Ordinal);
        Assert.Empty(prompt.Options);
        Assert.Equal(NoOptionBehavior.StorytellerDecides, prompt.OnNoOption);
    }

    /// <summary>首夜（还没有黎明）：窗口 = 全账，首夜更早槽位的失效计入（R-0004 补充）。</summary>
    [Fact]
    public void Prompt_FirstNight_CountsTheWholeLedger()
    {
        var prompt = Prompt(State(
            sinceDawnStart: 0,
            Malfunction(seat: 2, MalfunctionKind.Poisoned)));

        Assert.Contains("推演：1", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>涡流在场：提示明确「必须为假」并引用 R-0028。</summary>
    [Fact]
    public void Prompt_WithVortox_SaysTheNumberMustBeFalse()
    {
        var prompt = Prompt(WithVortox(State(sinceDawnStart: 0)));

        Assert.Contains("必须为假", prompt.Context, StringComparison.Ordinal);
        Assert.Contains("R-0028", prompt.Context, StringComparison.Ordinal);
    }

    /// <summary>说书人没给数字：显式拒绝（与钟表匠同族的非空校验），不产出信息事件。</summary>
    [Fact]
    public void Resolve_WithoutStorytellerNumber_IsRefused()
    {
        var context = Context(State(sinceDawnStart: 0), effective: true, decision: null);

        Assert.Throws<InvalidOperationException>(() => Resolution().Resolve(context));
    }

    /// <summary>正常生效：数字原样下发给数学家本人；说明里留平台推演值（只说书人可见）。</summary>
    [Fact]
    public void Resolve_Effective_DeliversNumberAndKeepsDeductionInNote()
    {
        var events = Resolution().Resolve(Context(
            State(sinceDawnStart: 0, Malfunction(seat: 2, MalfunctionKind.Poisoned)),
            effective: true,
            decision: "1"));

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.Equal(MathematicianSeat, information.Recipient);
        Assert.Equal(InfoAbility, information.Ability);
        Assert.Equal("1", information.Content);
        Assert.False(information.MayBeFalse);
        Assert.Contains("推演：1", information.Note, StringComparison.Ordinal);
    }

    /// <summary>能力未生效（中毒 / 醉酒）：照旧给信息但标「可能错误」，说明沿用生效判定的原因。</summary>
    [Fact]
    public void Resolve_Ineffective_MarksInfoAsPossiblyFalse()
    {
        var events = Resolution().Resolve(Context(
            State(sinceDawnStart: 0),
            effective: false,
            decision: "说书人给的数字"));

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.True(information.MayBeFalse);
        Assert.Contains("中毒", information.Note, StringComparison.Ordinal);
    }

    /// <summary>涡流在场：哪怕能力本身正常生效，信息也标「可能为假」并引用 R-0028。</summary>
    [Fact]
    public void Resolve_WithVortox_MarksInfoAsMustBeFalse()
    {
        var events = Resolution().Resolve(Context(
            WithVortox(State(sinceDawnStart: 0)),
            effective: true,
            decision: "0"));

        var information = Assert.Single(events.OfType<InformationResultIssuedEvent>());
        Assert.True(information.MayBeFalse);
        Assert.Contains("涡流", information.Note, StringComparison.Ordinal);
    }

    /// <summary>重建来源按能力归属角色取契约：数学家的槽位能重建出提示；陌生角色返回 null。</summary>
    [Fact]
    public void PromptSource_RebuildsMathematicianPrompt()
    {
        var rebuilt = NightActions.Prompts.Rebuild(new SlotPromptRequest
        {
            SlotId = new StepSlotId("mathematician"),
            Character = Mathematician,
            Actor = MathematicianSeat,
            Seats = [MathematicianSeat],
            State = State(sinceDawnStart: 0, Malfunction(seat: 2, MalfunctionKind.Drunk)),
        });

        Assert.NotNull(rebuilt);
        Assert.Contains("推演：1", rebuilt!.Context, StringComparison.Ordinal);
        // 「没有契约的角色返回 null」用仍未实现的贤者做样本：卖花女孩已随回溯型信息族实现（E23）。
        Assert.Null(NightActions.Prompts.Rebuild(new SlotPromptRequest
        {
            SlotId = new StepSlotId("unknown"),
            Character = new CharacterId("sage"),
            Actor = MathematicianSeat,
            Seats = [MathematicianSeat],
            State = State(sinceDawnStart: 0),
        }));
    }

    private static INightAction PromptAction => NightActions.Default.Find(Mathematician)!;

    private static IAbilityResolution Resolution() => NightActions.Resolutions.Find(Mathematician)!;

    private static ChoicePrompt Prompt(GameState state) => PromptAction.BuildPrompt(new NightActionContext
    {
        Actor = MathematicianSeat,
        Seats = [MathematicianSeat, new SeatId(2), new SeatId(3), new SeatId(4), new SeatId(9)],
        State = state,
    });

    private static AbilityResolutionContext Context(GameState state, bool effective, string? decision) => new()
    {
        SlotId = new StepSlotId("mathematician"),
        PlanLabel = "sv:night-1",
        Phase = GamePhase.FirstNight,
        Actor = MathematicianSeat,
        ActorCharacter = Mathematician,
        ActorOwnCharacter = Mathematician,
        Seats = [MathematicianSeat, new SeatId(2), new SeatId(3), new SeatId(4), new SeatId(9)],
        State = state,
        Outcome = new AbilityOutcome
        {
            Effective = effective,
            Malfunctions = effective ? [] : [MalfunctionKind.Poisoned],
            Note = effective ? null : "来源中毒：能力未生效",
        },
        Decision = decision,
        DaysStarted = 0,
    };

    private static GameState State(int sinceDawnStart, params Malfunction[] entries) => new()
    {
        Malfunctions = new MalfunctionLedger { Entries = entries, SinceDawnStart = sinceDawnStart },
    };

    private static Malfunction Malfunction(int seat, MalfunctionKind kind) => new()
    {
        Seat = new SeatId(seat),
        Ability = new AbilityId("test-ability"),
        Kind = kind,
    };

    private static GameState WithVortox(GameState state) => state with
    {
        Seats =
        [
            .. state.Seats,
            new SeatStateEntry
            {
                Seat = new SeatId(9),
                Character = new StateFact<CharacterId> { Value = Vortox, Reason = "测试：涡流在场" },
                Life = new StateFact<LifeState> { Value = LifeState.Alive, Reason = "测试：涡流在场" },
            },
        ],
    };
}
