using OpenClockTower.Application;
using OpenClockTower.Contracts;

namespace OpenClockTower.Server;

/// <summary>
/// 大厅用例：列出在开的桌、由管理员创建新桌（D-0025）。
/// </summary>
/// <remarks>
/// <para>
/// 放在 Server 层而不是 Application：授权依据是本机配置的管理员名单（<see cref="AdminDirectory"/>），
/// 属于部署方的授权策略，不是领域规则。
/// </para>
/// <para>
/// 建桌只做三件事：生成标识、写会话目录（票据 + 桌元数据）、让注册表装载它。
/// 新桌的会话信息由 <see cref="GameSetupFactory"/> 生成——与默认桌**同一套**票据实现，
/// 不另造一条建局路径。
/// </para>
/// </remarks>
public sealed class LobbyService
{
    /// <summary>桌名长度上限（够用即可；过长只是显示负担）。</summary>
    private const int MaxNameLength = 24;

    /// <summary>席位数区间：与配板求解的可行范围一致。</summary>
    private const int MinSeatCount = 1;
    private const int MaxSeatCount = 20;

    /// <summary>桌标识长度（随机段）。</summary>
    private const int IdLength = 8;

    /// <summary>桌标识字母表：去掉容易看错的 0 / o / 1 / l / i，方便口头转述。</summary>
    private const string IdAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    private readonly IGameCatalog _catalog;
    private readonly ISeatBindingStore _bindings;
    private readonly GameRegistry _registry;
    private readonly AdminDirectory _admins;
    private readonly ILogger<LobbyService> _logger;

    /// <summary>构造大厅服务。</summary>
    public LobbyService(
        IGameCatalog catalog,
        ISeatBindingStore bindings,
        GameRegistry registry,
        AdminDirectory admins,
        ILogger<LobbyService> logger)
    {
        _catalog = catalog;
        _bindings = bindings;
        _registry = registry;
        _admins = admins;
        _logger = logger;
    }

    /// <summary>列出在开的桌（按标识升序）。</summary>
    /// <remarks>
    /// 人数从席位绑定表现算：它可能在玩家加入后变化，不缓存在内存里（"状态属于所有者"——
    /// 绑定表才是所有者，不做第二份事实）。
    /// </remarks>
    public async Task<IReadOnlyList<LobbyTableDto>> ListAsync(CancellationToken cancellationToken)
    {
        var setups = await _catalog.ListAsync(cancellationToken);

        var tables = new List<LobbyTableDto>(setups.Count);
        foreach (var setup in setups)
        {
            var bindings = await _bindings.ListByGameAsync(setup.GameId, cancellationToken);
            var started = await HasStartedAsync(setup.GameId, cancellationToken);

            tables.Add(new LobbyTableDto
            {
                GameId = setup.GameId.Value,
                Name = setup.Name,
                SeatCapacity = setup.Seats.Count,
                TakenSeatCount = bindings.Count,
                Started = started,
                Locked = setup.IsLocked,
            });
        }

        return tables;
    }

    /// <summary>创建一张新桌（只有管理员）。</summary>
    /// <param name="account">开桌者（由账号会话推导，客户端声明不可信）。</param>
    /// <param name="name">桌名（可为空 = 未命名）。</param>
    /// <param name="seatCount">席位数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<LobbyCreateResultDto> CreateAsync(
        Account? account,
        string? name,
        int seatCount,
        CancellationToken cancellationToken)
    {
        if (!_admins.IsAdmin(account))
        {
            // 中性文案 + 审计：不告诉调用者"谁是管理员"。
            _logger.LogWarning(
                "建桌被拒（权限）：account={AccountId} username={Username} 原因=不是管理员",
                account?.Id.Value,
                account?.Username ?? "(未登录)");
            return Fail("not_admin", "只有管理员可以开新桌");
        }

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length > MaxNameLength)
        {
            return Fail("invalid_name", $"桌名最多 {MaxNameLength} 个字符");
        }

        if (seatCount < MinSeatCount || seatCount > MaxSeatCount)
        {
            return Fail("invalid_seat_count", $"席位数必须在 {MinSeatCount}–{MaxSeatCount} 之间");
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var gameId = new GameId(NewGameId());
            if (await _catalog.FindAsync(gameId, cancellationToken) is not null)
            {
                // 撞号（概率极低）：换一个再来。
                continue;
            }

            var setup = GameSetupFactory.Create(gameId, seatCount) with { Name = trimmed };
            await _catalog.SaveAsync(setup, cancellationToken);
            await _registry.GetOrCreateAsync(gameId, cancellationToken);

            _logger.LogInformation(
                "已开桌：game={GameId} 桌名={Name} 席位={SeatCount} 开桌人={Username}",
                gameId.Value,
                trimmed.Length == 0 ? "(未命名)" : trimmed,
                seatCount,
                account!.Username);

            return new LobbyCreateResultDto
            {
                Ok = true,
                Code = "ok",
                Message = "已开桌",
                GameId = gameId.Value,
                StorytellerTicket = setup.StorytellerTicket,
                SeatCount = seatCount,
            };
        }

        _logger.LogError("建桌失败：连续 {Attempts} 次标识撞号", 5);
        return Fail("id_conflict", "开桌失败，请重试");
    }

    /// <summary>这一桌是否已开局（已产生过夜晚或白天）。</summary>
    /// <remarks>
    /// 从注册表里**已装载**的会话问；未装载的桌按"未开局"处理——大厅列表不应该为了显示一个标记
    /// 就把每一桌都恢复一遍（那会让"看一眼大厅"变成"把所有桌装进内存"）。
    /// 一桌一旦被打开过就会留在注册表里，所以这个判断对"正在被使用的桌"是准确的。
    /// </remarks>
    private async Task<bool> HasStartedAsync(GameId gameId, CancellationToken cancellationToken)
    {
        var game = await _registry.FindAsync(gameId, cancellationToken);
        return game is not null && game.Session.GetStorytellerView().Phase is not null;
    }

    private static LobbyCreateResultDto Fail(string code, string message) => new()
    {
        Ok = false,
        Code = code,
        Message = message,
    };

    /// <summary>生成一个便于口头转述的桌标识。</summary>
    private static string NewGameId()
    {
        var chars = new char[IdLength];
        for (var index = 0; index < IdLength; index++)
        {
            chars[index] = IdAlphabet[Random.Shared.Next(IdAlphabet.Length)];
        }

        return new string(chars);
    }
}
