using Microsoft.Extensions.Logging;

namespace OpenClockTower.Application;

/// <summary>
/// 复盘读侧服务：可见性闸 + 按需从事件流重建（D-0020 / R-0043）。
/// </summary>
/// <remarks>
/// <para>
/// **不依赖宿主内存态**（R-0043 第 4 条）：只读持久化事件流并做纯投影，所以不需要会话锁——
/// 每次读到的是一条**已提交事件流的一致前缀**（提交按批原子落库），并发提交要么整体在其前、
/// 要么整体在其后；这里不存在"快照与补齐拼在一起"的撕裂面（那是重连包需要持锁的原因）。
/// </para>
/// <para>
/// 可见性闸按**事件流自己**判结束（<c>GameEndedEvent</c> 是否出现在流里），与宿主里那份步骤机状态无关：
/// 玩家在结束批次之前一律显式拒绝并记审计，数据一个字节都不返回（R-0043 第 1 条）。
/// </para>
/// </remarks>
public sealed class ReplayQueryService
{
    private readonly GameId _gameId;
    private readonly IGameStore _store;
    private readonly SeatNameDirectory _seatNames;
    private readonly ILogger<ReplayQueryService> _logger;

    /// <summary>构造复盘读侧服务。</summary>
    public ReplayQueryService(
        GameId gameId,
        IGameStore store,
        SeatNameDirectory seatNames,
        ILogger<ReplayQueryService> logger)
    {
        _gameId = gameId;
        _store = store;
        _seatNames = seatNames;
        _logger = logger;
    }

    /// <summary>
    /// 读一页复盘：先按事件流重建，再按身份与结束态放行 / 拒绝。
    /// </summary>
    /// <param name="actor">服务端推导出的操作者（客户端声明不可信，D-0012）。</param>
    /// <param name="afterSequence">客户端已拿到的最大事件序号；首次传 0。</param>
    /// <param name="pageSize">本页最多返回的步骤数（由 <see cref="ReplayProjection"/> 钳制）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ReplayAccessDeniedException">玩家面在结束批次之前（或身份没有读取面）被拒。</exception>
    public async Task<ReplayView> ReadAsync(
        Actor actor,
        long afterSequence,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var storedEvents = await _store.ReadEventsAsync(_gameId, afterSequence: 0, cancellationToken);
        var replay = ReplayProjection.Build(storedEvents, afterSequence, _seatNames.Snapshot(), pageSize);

        if (actor.Kind is not (ActorKind.Player or ActorKind.Storyteller))
        {
            throw Deny(actor, replay, "该身份没有复盘读取面");
        }

        if (actor.Kind == ActorKind.Player && !replay.Ended)
        {
            throw Deny(actor, replay, "本局尚未结束，复盘在结束批次之后才对局内玩家开放（R-0043）");
        }

        _logger.LogInformation(
            "复盘读取：game={GameId} kind={Kind} seat={Seat} 本页步骤={StepCount} 序号={Sequence} 已结束={Ended}",
            _gameId,
            actor.Kind,
            actor.Seat,
            replay.Steps.Count,
            replay.Sequence,
            replay.Ended);
        return replay;
    }

    /// <summary>拒绝 + 审计：拒绝文案中性（只说明什么时候可以看），不泄露任何局面信息。</summary>
    private ReplayAccessDeniedException Deny(Actor actor, ReplayView replay, string reason)
    {
        _logger.LogWarning(
            "复盘读取被拒：game={GameId} kind={Kind} seat={Seat} 最新序号={Sequence} 已结束={Ended} 原因={Reason}",
            _gameId,
            actor.Kind,
            actor.Seat,
            replay.Sequence,
            replay.Ended,
            reason);
        return new ReplayAccessDeniedException(reason);
    }
}
