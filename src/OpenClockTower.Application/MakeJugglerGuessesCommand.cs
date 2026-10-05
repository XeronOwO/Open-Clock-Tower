using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 玩家（杂耍艺人）在自己的首个白天**公开**猜测 0–5 名玩家的角色（R-0057-B）。
/// </summary>
/// <remarks>
/// 与操作请求方向相反：这条命令由玩家主动发出。席位**不在命令里自称**——由凭据推导
/// （D-0012：客户端声明一律不可信）。猜测内容才是参数：数量上限、「首个白天」、
/// 「这次持有还没猜过」与角色名合法性都在内核里判（<c>JugglerGuessMachine</c>）。
/// 猜对数是**当晚**由说书人给出的，不在这条命令的返回里（只到本人）。
/// </remarks>
public sealed record MakeJugglerGuessesCommand : GameCommand
{
    /// <summary>这一批公开猜测（0–5 条，按玩家提交顺序）。</summary>
    public required IReadOnlyList<JugglerGuess> Guesses { get; init; }
}
