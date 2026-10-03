namespace OpenClockTower.Kernel;

/// <summary>
/// 艺术家提问依据契约（规则层实现）：回答"这个席位此刻能不能提问"与"一条裁决怎么结清"。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IAdjudicatedExecutionSource"/> 同族：内核声明它需要的外部能力，规则层实现
/// （谁是艺术家、用没用过、回答文案、涡流干扰），依赖图保持无环——内核因此不必认识任何角色 slug
/// （D-0008：规则数据属于 Rules）。平台口径见 <c>docs/standard/rulings.md</c> R-0040。
/// </para>
/// <para>
/// <see cref="Resolve"/> 返回 null 表示判不了（席位维度没观测齐），由内核显式拒绝（D-0015：不猜）。
/// </para>
/// </remarks>
public interface IArtistQuestionSource
{
    /// <summary>本契约负责的角色。</summary>
    CharacterId Character { get; }

    /// <summary>结清时记账用的能力标识（进能力使用账本）。</summary>
    AbilityId Ability { get; }

    /// <summary>
    /// 构造提问的裁定点提示（选项：是 / 不是 / 我不知道 / 要求重问）。
    /// 「要求重问」不消耗能力，必须与另外三项**同列**（R-0040 第 4 条）。
    /// </summary>
    ChoicePrompt BuildPrompt(string question);

    /// <summary>结清一条裁决（回答 / 要求重问）；返回 null = 判不了（不猜）。</summary>
    ArtistQuestionResolution? Resolve(ArtistQuestionResolutionContext context);
}
