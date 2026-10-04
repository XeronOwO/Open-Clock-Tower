using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>复盘文案的公共口径：角色 / 能力 / 阶段 / 失效原因 / 选项值的显示文本。</summary>
/// <remarks>
/// 席位文本（含玩家名，D-0021）单独由 <see cref="ReplaySeatText"/> 提供：它需要一份名册快照，
/// 不适合放在静态工具里。未知取值一律原样回显、不吞（项目约定）；角色中文名以花名册为准（术语表 §1）。
/// 这里只做「值 → 人话」，不推演任何规则（D-0020：复盘是事件流的呈现投影）。
/// </remarks>
internal static class ReplayText
{
    /// <summary>角色文本（中文名，未知原样回显）。</summary>
    internal static string Character(CharacterId? character) =>
        character is { } value ? CharacterValue(value) : "（未知角色）";

    /// <summary>已知角色的文本。</summary>
    internal static string CharacterValue(CharacterId character) =>
        SectsAndVioletsRoster.DisplayNameOf(character) ?? character.Value;

    /// <summary>能力文本：按角色 slug 取中文名，取不到回显 slug。</summary>
    internal static string Ability(AbilityId ability) =>
        SectsAndVioletsRoster.DisplayNameOf(new CharacterId(ability.Value)) ?? ability.Value;

    /// <summary>阶段文本。</summary>
    internal static string Phase(GamePhase phase) => phase switch
    {
        GamePhase.FirstNight => "首夜",
        GamePhase.OtherNight => "夜晚",
        GamePhase.Day => "白天",
        _ => "结算",
    };

    /// <summary>阵营文本。</summary>
    internal static string Alignment(Alignment alignment) =>
        alignment == Kernel.Alignment.Good ? "善良" : "邪恶";

    /// <summary>处决分类文本。</summary>
    internal static string Execution(ExecutionKind kind) => kind switch
    {
        ExecutionKind.Day => "常规处决",
        ExecutionKind.CerenovusMadness => "洗脑师处罚处决",
        ExecutionKind.MutantMadness => "畸形秀演员处罚处决",
        _ => kind.ToString(),
    };

    /// <summary>胜负条件文本。</summary>
    internal static string Outcome(OutcomeCondition condition) => condition switch
    {
        OutcomeCondition.DemonsAllDead => "恶魔全部死亡",
        OutcomeCondition.TwoPlayersAlive => "仅剩两名玩家存活",
        OutcomeCondition.EvilTwinGoodTwinExecuted => "镜像双子善良方被处决",
        OutcomeCondition.VortoxNoExecution => "涡流：黄昏时无人被处决",
        OutcomeCondition.KlutzChoiceFactionLoses => "呆瓜的选择使阵营落败",
        _ => condition.ToString(),
    };

    /// <summary>失效原因列表文本（R-0004 的分类）。</summary>
    internal static string Malfunctions(IReadOnlyList<MalfunctionKind> kinds) =>
        kinds.Count == 0 ? string.Empty : string.Join("、", kinds.Select(Malfunction));

    /// <summary>失效原因文本。</summary>
    internal static string Malfunction(MalfunctionKind kind) => kind switch
    {
        MalfunctionKind.Poisoned => "中毒",
        MalfunctionKind.Drunk => "醉酒",
        MalfunctionKind.Jinx => "相克规则",
        MalfunctionKind.Vortox => "涡流必假",
        MalfunctionKind.Barista => "咖啡师",
        MalfunctionKind.AbilityDesign => "能力设定",
        MalfunctionKind.StorytellerRuling => "说书人裁定",
        _ => "未定",
    };

    /// <summary>艺术家提问结清方式文本（R-0040）。</summary>
    internal static string ArtistClosure(ArtistQuestionClosure closure) => closure switch
    {
        ArtistQuestionClosure.Answered => "已回答",
        ArtistQuestionClosure.Returned => "要求重问",
        ArtistQuestionClosure.Abandoned => "越过作废",
        _ => closure.ToString(),
    };

    /// <summary>把选项值翻译成人话：seat:N / pair:A+B / decline / 两维编码 / 未知原样回显。</summary>
    internal static string Option(string? value, ReplaySeatText seatText)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "（空）";
        }

        if (value == "decline")
        {
            return "不交换";
        }

        if (value.Contains('|'))
        {
            return string.Join(" × ", value.Split('|').Select(part => OptionPart(part, seatText)));
        }

        return OptionPart(value, seatText);
    }

    /// <summary>单个选项分量：席位 / 玩家对（走同一席位口径） / 角色 slug / 原样回显。</summary>
    private static string OptionPart(string part, ReplaySeatText seatText)
    {
        if (seatText.TryFormatOptionSeatPart(part, out var seatPart))
        {
            return seatPart;
        }

        return SectsAndVioletsRoster.DisplayNameOf(new CharacterId(part)) ?? part;
    }
}
