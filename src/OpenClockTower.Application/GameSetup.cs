namespace OpenClockTower.Application;

/// <summary>一局的会话信息：谁拿着哪张席位票据、这张桌归谁 + 大厅元数据（服务端持久化，重启后仍有效）。</summary>
/// <remarks>
/// <para>
/// 大厅元数据（<see cref="Name"/> / <see cref="IsInviteOnly"/>）与归属（<see cref="CreatedByAccountId"/>）
/// 是**会话信息**，不进事件流：它们不影响任何规则判定，也不该出现在复盘里（D-0015：状态账只记事实与归因）。
/// </para>
/// <para>
/// 说书人票据已随 D-0027 整个退场：主持这一桌的唯一依据是本条的 <see cref="CreatedByAccountId"/>
/// ——开桌的那个账号。老库里的那一列留在原处不再读（见 D-0027 的残余）。
/// </para>
/// </remarks>
public sealed record GameSetup
{
    /// <summary>游戏标识。</summary>
    public required GameId GameId { get; init; }

    /// <summary>各席位的票据（邀请码里冒号之后那一段；D-0037 起它只作邀请码用）。</summary>
    public required IReadOnlyList<SeatTicket> Seats { get; init; }

    /// <summary>
    /// 开桌账号（这一桌归谁）。
    /// </summary>
    /// <remarks>
    /// 可空只为兼容**升级前就在库里的桌**（那时没有归属概念，也没有开桌账号）：
    /// 它们没有说书人，谁也不能凭账号进它的主持台——这正是"没有房主的桌"不该再产生的原因，
    /// 也是默认桌必须退场的原因（D-0027）。
    /// </remarks>
    public AccountId? CreatedByAccountId { get; init; }

    /// <summary>桌名（玩家在大厅里看到的标题；未命名为空串）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 是否**邀请制**：邀请制桌不接受自助入座，必须凭邀请码（D-0037）。
    /// </summary>
    /// <remarks>
    /// 这不是"锁桌"（旧名 `IsLocked`）：按新的访问模型，它表达的是**这一桌怎么进人**，
    /// 而不是"暂时封住"——公开桌与邀请制桌是两种并列的形态，说书人随时可切，切换即推。
    /// 开局之后自助入座本来就被拦（那是另一条闸，见 `SeatJoinCoordinator.JoinBySeatAsync`），
    /// 与本字段相互独立：开局不改写它，它也不代替开局闸。
    /// </remarks>
    public bool IsInviteOnly { get; init; }
}
