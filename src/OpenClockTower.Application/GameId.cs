namespace OpenClockTower.Application;

/// <summary>一局游戏的稳定标识。</summary>
public readonly record struct GameId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
