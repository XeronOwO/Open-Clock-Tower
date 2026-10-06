using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using OpenClockTower.Application;
using OpenClockTower.Contracts;

namespace OpenClockTower.Server;

/// <summary>
/// 说书人加入流程（**按开桌账号**定位身份 → 签发连接凭据 → 返回整视图）：从 <see cref="GameHub"/> 拆出。
/// </summary>
/// <remarks>
/// <para>
/// 与玩家加入（<see cref="SeatJoinCoordinator"/>）并列：玩家的加入要重投未响应请求，因此留在 Hub 里
/// 贴着 <c>Clients.Caller</c> 写；说书人的加入只回一份 DTO，没有推送到调用者的动作，
/// 可以整段搬出 Hub——拆分的直接原因是单文件 600 行门禁（Hub 的命令面本身不能再瘦）。
/// </para>
/// <para>
/// **身份依据是归属，不是凭据**（D-0027）：这一桌是谁开的，就只有那个账号进得来主持台。
/// 此前是"谁拿着说书人票据谁就进得来"，那串凭据已随 D-0027 整个删除；
/// 于是换设备 / 清缓存不再是失联，登录同一账号即可回来。
/// </para>
/// <para>
/// 一次调用构造一次：连接 id、取消令牌与**落在哪一桌**都属于本次调用，不做成单例
/// （D-0012：凭据与连接一一对应，串了连接等于串了身份；D-0024：串了桌等于串了局）。
/// </para>
/// </remarks>
internal sealed class HubJoinFlow
{
    private readonly IGameCatalog _catalog;
    private readonly GameInstance _game;
    private readonly ConnectionRegistry _registry;
    private readonly ILogger _logger;
    private readonly string _connectionId;
    private readonly CancellationToken _abort;

    internal HubJoinFlow(
        IGameCatalog catalog,
        GameInstance game,
        ConnectionRegistry registry,
        ILogger logger,
        string connectionId,
        CancellationToken abort)
    {
        _catalog = catalog;
        _game = game;
        _registry = registry;
        _logger = logger;
        _connectionId = connectionId;
        _abort = abort;
    }

    /// <summary>
    /// 说书人加入：**只认这一桌的开桌账号**，签发连接凭据（同局同一时刻只保留一条有效说书人连接）。
    /// </summary>
    /// <param name="accountId">调用者出示的账号会话解析出的账号。</param>
    internal async Task<StorytellerJoinDto> JoinStorytellerAsync(AccountId accountId)
    {
        var setup = await LoadSetupAsync();
        if (setup.CreatedByAccountId is not { } owner || owner != accountId)
        {
            // 拒绝理由分成两种，日志里说清楚是哪一种（对外只给中性文案，不透露这桌是谁开的）。
            _logger.LogWarning(
                "说书人加入被拒（不是开桌账号）：connection={ConnectionId} game={GameId} 账号={AccountId} 开桌账号={Owner}",
                _connectionId,
                _game.GameId.Value,
                accountId.Value,
                setup.CreatedByAccountId?.Value);
            throw new HubException("这一桌不是你开的");
        }

        var credential = _registry.IssueForStoryteller(_game.GameId, _connectionId);
        _logger.LogInformation(
            "已签发说书人连接凭据：connection={ConnectionId} game={GameId} 账号={AccountId} 指纹={Fingerprint}（旧说书人连接已作废）",
            _connectionId,
            _game.GameId.Value,
            accountId.Value,
            ConnectionCredential.FingerprintOf(credential.Value));

        var view = ProjectionMapper.ToDto(_game.Session.GetStorytellerView());
        _logger.LogInformation(
            "说书人已加入：connection={ConnectionId} game={GameId} 序号={Sequence} 挂起={Held}",
            _connectionId,
            _game.GameId.Value,
            view.Sequence,
            view.Pending is not null);

        return new StorytellerJoinDto
        {
            Credential = credential.Value,
            View = view,
        };
    }

    /// <summary>本局会话信息；还没有会话时显式拒绝（归属就存在它里面）。</summary>
    private async Task<GameSetup> LoadSetupAsync()
    {
        var setup = await _catalog.FindAsync(_game.GameId, _abort);
        if (setup is null)
        {
            _logger.LogWarning(
                "命令被拒绝（会话）：connection={ConnectionId} 原因=本局还没有会话信息",
                _connectionId);
            throw new HubException("本局还没有会话信息");
        }

        return setup;
    }
}
