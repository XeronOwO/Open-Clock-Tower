using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 杂耍艺人的猜测依据（R-0057-B）：「首个白天」怎么起算、哪些角色名是合法猜测。
/// </summary>
/// <remarks>
/// 与 <see cref="SavantQuestionSource"/> 同族：内核不认角色 slug（D-0008），规则语义在规则层。
/// 「首个白天」的口径与依据见 <see cref="JugglerGuessWindow"/>；角色名以剧本花名册为准
/// （<see cref="SectsAndVioletsRoster"/>）——猜不存在的角色名是打错字，不是策略。
/// </remarks>
internal sealed class JugglerGuessSource : IJugglerGuessSource
{
    private static readonly CharacterId Juggler = new("juggler");

    /// <inheritdoc />
    public CharacterId Character => Juggler;

    /// <inheritdoc />
    public int? FirstHeldDay(GameState state, SeatId seat) => JugglerGuessWindow.FirstHeldDay(state, seat);

    /// <inheritdoc />
    public bool IsKnownCharacter(CharacterId character) => SectsAndVioletsRoster.Contains(character);
}
