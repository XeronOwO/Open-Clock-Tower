namespace OpenClockTower.Kernel;

/// <summary>
/// 杂耍艺人猜测依据契约（规则层实现）：回答"这个席位当前这次持有杂耍艺人是从哪个白天起算的"
/// 与"这个角色名是不是本剧本认得的取值"。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ISavantQuestionSource"/> 同族：内核只声明它需要的外部能力，规则语义（谁是杂耍艺人、
/// 「首个白天」怎么起算、哪些角色名合法）由规则层给出——内核因此不必认识任何角色 slug（D-0008）。
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0057-B。
/// </para>
/// <para>
/// 返回 null 表示**判不了**（账上说不清他什么时候拿到这个角色），由内核显式拒绝（D-0015：不猜）。
/// </para>
/// </remarks>
public interface IJugglerGuessSource
{
    /// <summary>本契约负责的角色。</summary>
    CharacterId Character { get; }

    /// <summary>
    /// 该席位**当前这次持有**本角色是从第几个白天起算（"首个白天"口径，R-0057-B 第 3 条）；
    /// 判不了返回 null。
    /// </summary>
    int? FirstHeldDay(GameState state, SeatId seat);

    /// <summary>这个角色名是不是本剧本认得的取值（花名册内）；不在册的猜测显式拒绝。</summary>
    bool IsKnownCharacter(CharacterId character);
}
