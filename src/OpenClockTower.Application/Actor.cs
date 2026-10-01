using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>一次命令的操作者：身份闸只认服务端推导出的这个值，不认客户端声明。</summary>
public sealed record Actor
{
    /// <summary>身份类别。</summary>
    public required ActorKind Kind { get; init; }

    /// <summary>玩家操作者对应的席位；其它类别为 null。</summary>
    public SeatId? Seat { get; init; }

    /// <summary>展示名（日志与审计用），可为空。</summary>
    public string? Name { get; init; }

    /// <summary>构造一个玩家操作者。</summary>
    public static Actor Player(SeatId seat) => new() { Kind = ActorKind.Player, Seat = seat };

    /// <summary>构造一个说书人操作者。</summary>
    public static Actor Storyteller(string? name = null) => new() { Kind = ActorKind.Storyteller, Name = name };

    /// <summary>构造一个系统操作者（节拍器）。</summary>
    public static Actor System => new() { Kind = ActorKind.System };

    /// <summary>构造一个宿主操作者。</summary>
    public static Actor Host => new() { Kind = ActorKind.Host };
}
