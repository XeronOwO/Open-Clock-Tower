using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// **局注册表**：按标识解析一局的实例（多桌并行，D-0024）。
/// </summary>
/// <remarks>
/// <para>
/// 它是"局"这一层的**组合根**：握住装配一局所需的全部依赖，按需造出
/// <see cref="GameInstance"/> 并各自从事件流恢复。
/// 用 <see cref="Lazy{T}"/> 包 <see cref="Task{TResult}"/>：并发取同一局只会恢复一次。
/// </para>
/// <para>
/// 隔离由结构保证：每局的实例自带席位名读模型；存储层本来就按 GameId 过滤。
/// 注册表只负责"按标识给对的那一束"。
/// </para>
/// <para>
/// 首版不做空闲桌回收：小圈子自用、桌数有限；要回收时在这里加计时即可。
/// </para>
/// </remarks>
public sealed class GameRegistry
{
    private readonly ConcurrentDictionary<GameId, Lazy<Task<GameInstance>>> _instances = new();
    private readonly IGameStore _store;
    private readonly IGameCatalog _catalog;
    private readonly ISeatBindingStore _bindings;
    private readonly IAccountStore _accounts;
    private readonly IAbilityResolutionCatalog _abilities;
    private readonly IReadOnlyList<IStandingEffectSource> _standingEffects;
    private readonly IClock _clock;
    private readonly PacingOptions _pacing;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<GameRegistry> _logger;
    private int _initialized;

    /// <summary>构造注册表（组合根：装配一局所需的依赖都在这里）。</summary>
    /// <param name="store">事件存储（按 GameId 过滤）。</param>
    /// <param name="catalog">会话目录（席位票据、归属与桌元数据）。</param>
    /// <param name="bindings">席位绑定（装载席位名读模型）。</param>
    /// <param name="accounts">账号（装载席位名读模型）。</param>
    /// <param name="abilities">角色契约目录（各局共用，无状态）。</param>
    /// <param name="standingEffects">常驻效果来源（各局共用，无状态）。</param>
    /// <param name="clock">时钟（各局共用）。</param>
    /// <param name="pacing">节奏配置（各局共用）。</param>
    /// <param name="loggerFactory">为每局创建独立类别的日志器。</param>
    public GameRegistry(
        IGameStore store,
        IGameCatalog catalog,
        ISeatBindingStore bindings,
        IAccountStore accounts,
        IAbilityResolutionCatalog abilities,
        IReadOnlyList<IStandingEffectSource> standingEffects,
        IClock clock,
        PacingOptions pacing,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(abilities);
        ArgumentNullException.ThrowIfNull(standingEffects);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(pacing);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _store = store;
        _catalog = catalog;
        _bindings = bindings;
        _accounts = accounts;
        _abilities = abilities;
        _standingEffects = standingEffects;
        _clock = clock;
        _pacing = pacing;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<GameRegistry>();
    }

    /// <summary>当前在册的桌（按标识升序；供大厅列表与启动恢复）。</summary>
    public IReadOnlyList<GameId> GameIds =>
        [.. _instances.Keys.OrderBy(id => id.Value, StringComparer.Ordinal)];

    /// <summary>
    /// 取一局的实例；不存在时**按需构造**（不落库——落库由建桌用例负责，见 D-0025）。
    /// </summary>
    public Task<GameInstance> GetOrCreateAsync(GameId gameId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameId.Value);

        var lazy = _instances.GetOrAdd(
            gameId,
            id => new Lazy<Task<GameInstance>>(
                () => CreateAsync(id, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    /// <summary>取一局的实例；**不存在时返回 null**（不做按需创建）。</summary>
    /// <remarks>入座、命令等路径用它：未知的桌应当被明确拒绝，而不是被凭空创建出来。</remarks>
    public async Task<GameInstance?> FindAsync(GameId gameId, CancellationToken cancellationToken)
    {
        if (!_instances.TryGetValue(gameId, out var lazy))
        {
            return null;
        }

        return await lazy.Value.WaitAsync(cancellationToken);
    }

    /// <summary>该局是否已在册。</summary>
    public bool Contains(GameId gameId) => _instances.ContainsKey(gameId);

    /// <summary>
    /// 启动时装载**全部在册的桌**并各自恢复（幂等：重复调用只做一次）。
    /// </summary>
    /// <remarks>
    /// 恢复失败的桌**不阻断启动**：它自己带着降级位，说书人可在界面里显式重建；
    /// 一格坏桌不该让别的桌开不了（多桌的可用性要求）。
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        var setups = await _catalog.ListAsync(cancellationToken);
        if (setups.Count == 0)
        {
            // 空库是**正常的初始状态**（D-0027：宿主不再自建默认桌）：等第一桌被开出来。
            _logger.LogInformation("库中没有在册的桌：等待开桌（打开站点是空大厅）");
            return;
        }

        _logger.LogInformation(
            "装载在册的桌：数量={Count} 标识={Ids}",
            setups.Count,
            string.Join(",", setups.Select(setup => setup.GameId.Value)));

        foreach (var setup in setups)
        {
            // 单桌装载失败不影响其余桌：记下来，继续装载。
            // （恢复失败本身不会抛到这里——见 CreateAsync 的注释，它返回降级态实例。）
            try
            {
                await GetOrCreateAsync(setup.GameId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(
                    exception,
                    "装载桌失败（该桌不可用）：game={GameId}",
                    setup.GameId.Value);
            }
        }
    }

    /// <summary>构造一局并完成恢复。</summary>
    private async Task<GameInstance> CreateAsync(GameId gameId, CancellationToken cancellationToken)
    {
        var seatNames = new SeatNameDirectory();
        var session = new GameSession(
            gameId,
            _store,
            _catalog,
            _abilities,
            _standingEffects,
            _clock,
            _pacing,
            seatNames,
            _loggerFactory.CreateLogger<GameSession>());

        // 恢复失败**不抛出**：GameSession 已经把自己置为降级态并保留了序号连续性，
        // 说书人可在界面里显式重建。若在这里抛出，Lazy 会把异常缓存下来，
        // 这一桌此后永远拿不到实例——"降级"就变成了"装载失败"（实测踩过，见票据）。
        try
        {
            await session.RestoreAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogCritical(
                exception,
                "桌恢复失败，以降级态装载（等显式重建）：game={GameId}",
                gameId.Value);
        }

        await seatNames.ReloadAsync(gameId, _bindings, _accounts, cancellationToken);

        var replay = new ReplayQueryService(
            gameId,
            _store,
            seatNames,
            _loggerFactory.CreateLogger<ReplayQueryService>());

        _logger.LogInformation("桌已就绪：game={GameId}", gameId.Value);
        return new GameInstance(session, seatNames, replay);
    }
}
