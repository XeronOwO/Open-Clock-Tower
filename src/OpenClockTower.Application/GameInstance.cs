namespace OpenClockTower.Application;

/// <summary>
/// **一局游戏的全部实例束**：会话编排 + 席位名读模型 + 复盘读侧。
/// </summary>
/// <remarks>
/// <para>
/// 为什么要有这一层：多桌并行（D-0024）要求"局"从进程级单例降为**按标识解析的实例**。
/// 这三样东西都是一局一份、且必须彼此一致地指向同一局——尤其是
/// <see cref="SeatNameDirectory"/>：它若还是全局一份，两桌会互相看到对方的席位名。
/// </para>
/// <para>
/// 由 <see cref="GameRegistry"/> 创建并持有；本类只做"持有并暴露"，不含任何编排逻辑
/// （职责单一：它是束，不是服务）。
/// </para>
/// </remarks>
public sealed class GameInstance
{
    /// <summary>构造一局的实例束。</summary>
    public GameInstance(GameSession session, SeatNameDirectory seatNames, ReplayQueryService replay)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(seatNames);
        ArgumentNullException.ThrowIfNull(replay);

        Session = session;
        SeatNames = seatNames;
        Replay = replay;
    }

    /// <summary>本局标识。</summary>
    public GameId GameId => Session.GameId;

    /// <summary>本局的命令编排与状态持有者。</summary>
    public GameSession Session { get; }

    /// <summary>本局的席位名读模型（**一局一份**，不是全局）。</summary>
    public SeatNameDirectory SeatNames { get; }

    /// <summary>本局的复盘读侧。</summary>
    public ReplayQueryService Replay { get; }
}
