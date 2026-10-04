using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 艺术家的白天提问依据（R-0040）：四种回答选项、结清后果与消耗口径。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《艺术家》· 2026-10-01 抓取 · 规则细节 1 / 角色简介 1–3 / 运作方式 1–11——
/// 「艺术家可以向说书人提出任一是非问题」「说书人需要诚实地回答是、不是，或我不知道」
/// 「如果你不能用这三种答案进行回答，让他重新问一个问题」
/// 「在使用他的能力之后，为他放置'失去能力'提示标记」「不论艺术家是否醉酒中毒，都要放置该标记」。
/// </para>
/// <para>
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0040：三种回答都记一次能力使用（含未生效）并落
/// 「失去能力」标记；「要求重问」不记使用、不落标记、可在同一白天重新提问。
/// 回答的「可能为假」按三-1 / 三-3；涡流在场时信息必须为假（R-0028）。
/// </para>
/// </remarks>
internal sealed class ArtistQuestionSource : IArtistQuestionSource
{
    /// <summary>三种回答的稳定编码（进事件流）。</summary>
    internal const string AnswerYes = "yes";

    internal const string AnswerNo = "no";

    internal const string AnswerUnknown = "unknown";

    /// <summary>「要求重问」的稳定编码（不消耗能力，R-0040 第 4 条）。</summary>
    internal const string Return = "retry";

    /// <summary>角色标识。</summary>
    private static readonly CharacterId Artist = new("artist");

    /// <inheritdoc />
    public CharacterId Character => Artist;

    /// <inheritdoc />
    public AbilityId Ability { get; } = new("artist");

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        return new ChoicePrompt
        {
            Context = $"艺术家的问题是：「{question}」。请如实回答 是 / 不是 / 我不知道"
                + "（任何一种回答都会用掉艺术家的能力）；若这个问题无法用三种答案回答，"
                + "可以要求重问——重问不消耗能力（R-0040）。",
            Options =
            [
                new DecisionOption { Value = AnswerYes, Preview = "是" },
                new DecisionOption { Value = AnswerNo, Preview = "不是" },
                new DecisionOption { Value = AnswerUnknown, Preview = "我不知道" },
                new DecisionOption { Value = Return, Preview = "要求重问（不消耗能力）" },
            ],
            OnNoOption = NoOptionBehavior.StorytellerDecides,
        };
    }

    /// <inheritdoc />
    public ArtistQuestionResolution? Resolve(ArtistQuestionResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.Equals(context.Decision, Return, StringComparison.Ordinal))
        {
            return new ArtistQuestionResolution
            {
                Ruling = ArtistQuestionRuling.Returned,
                Note = "说书人要求重新提问：不消耗艺术家的能力（R-0040 第 4 条）",
            };
        }

        if (context.Decision is not (AnswerYes or AnswerNo or AnswerUnknown))
        {
            throw new InvalidOperationException(
                $"艺术家的裁决只能是 是 / 不是 / 我不知道 / 要求重问：{context.Decision}");
        }

        var entry = context.State.Seat(context.Question.Seat);
        var outcome = entry is null ? null : AbilityEffectivenessEvaluator.Evaluate(context.State, entry);
        if (outcome is null)
        {
            // 维度没有观测齐：判不了就不猜——由内核显式拒绝（D-0015）。
            return null;
        }

        // 艺术家本人是镇民：涡流存活时她的信息必须为假（R-0028 / R-0004 对照）；
        // 处于咖啡师「清醒且健康」窗口内时不受涡流约束（R-0047 第 4 条）。
        var vortox = VortoxInterference.IsActiveFor(context.State, context.Question.Seat);
        var malfunctions = new List<MalfunctionKind>(outcome.Malfunctions);
        if (vortox)
        {
            malfunctions.Add(MalfunctionKind.Vortox);
        }

        return new ArtistQuestionResolution
        {
            Ruling = ArtistQuestionRuling.Answered,
            Effective = outcome.Effective,
            Malfunctions = malfunctions,
            Note = VortoxInterference.NoteFor(
                context.State,
                context.Question.Seat,
                outcome.Effective ? null : outcome.Note),
            Events =
            [
                new InformationResultIssuedEvent
                {
                    Recipient = context.Question.Seat,
                    Ability = Ability,
                    Content = ContentOf(context.Decision),
                    MayBeFalse = !outcome.Effective || vortox,
                    Note = VortoxInterference.NoteFor(
                        context.State,
                        context.Question.Seat,
                        outcome.Effective ? null : outcome.Note),
                },
            ],
        };
    }

    private static string ContentOf(string decision) => decision switch
    {
        AnswerYes => "是",
        AnswerNo => "不是",
        AnswerUnknown => "我不知道",
        _ => throw new InvalidOperationException($"未知的艺术家裁决：{decision}"),
    };
}
