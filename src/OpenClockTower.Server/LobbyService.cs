using OpenClockTower.Application;
using OpenClockTower.Contracts;

namespace OpenClockTower.Server;

/// <summary>
/// 大厅用例：列出在开的桌、由登录账号创建新桌（D-0025 / D-0026 / D-0027）。
/// </summary>
/// <remarks>
/// <para>
/// 放在 Server 层而不是 Application：授权依据是本机配置（<see cref="TableCreationPolicy"/>），
/// 属于部署方的授权策略，不是领域规则。
/// </para>
/// <para>
/// 开桌只做三件事：生成标识、写会话目录（席位票据 + 归属 + 桌元数据）、让注册表装载它。
/// 新桌的会话信息由 <see cref="GameSetupFactory"/> 生成——席位票据只有这一套实现，不另造一条建局路径。
/// </para>
/// <para>
/// **开桌 ≠ 获得权限**（D-0026）：任何登录账号都能开。**但这一桌从此归开桌账号**（D-0027）：
/// `CreatedByAccountId` 是进主持台的唯一依据，平台不提供转交——说书人仍是"玩这一局的角色"，
/// 只是这个角色不再是一串可以转手的凭据。
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
    private readonly TableCreationPolicy _tableCreation;
    private readonly ILogger<LobbyService> _logger;

    /// <summary>构造大厅服务。</summary>
    public LobbyService(
        IGameCatalog catalog,
        ISeatBindingStore bindings,
        GameRegistry registry,
        TableCreationPolicy tableCreation,
        ILogger<LobbyService> logger)
    {
        _catalog = catalog;
        _bindings = bindings;
        _registry = registry;
        _tableCreation = tableCreation;
        _logger = logger;
    }

    /// <summary>列出在开的桌（按标识升序）。</summary>
    /// <param name="viewer">看这份列表的账号（未登录为 null）；用来算 <see cref="LobbyTableDto.CreatedByMe"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 人数从席位绑定表现算：它可能在玩家加入后变化，不缓存在内存里（"状态属于所有者"——
    /// 绑定表才是所有者，不做第二份事实）。归属同理，直接读会话目录的 `CreatedByAccountId`，
    /// 服务端算好"这张桌是不是你开的"，不让前端自己拼事实（D-0027）。
    /// </remarks>
    public async Task<IReadOnlyList<LobbyTableDto>> ListAsync(
        AccountId? viewer,
        CancellationToken cancellationToken)
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
                CreatedByMe = viewer is { } account && setup.CreatedByAccountId == account,
                // 我自己已经认领的席位：界面据此让"回到我的座位"在开局 / 锁桌之后仍然点得动。
                MySeatNumbers = viewer is { } me
                    ? [.. bindings.Where(binding => binding.AccountId == me)
                        .Select(binding => binding.Seat.Value)
                        .OrderBy(value => value)]
                    : [],
                // 已占席位号：前端据此把按钮置灰，玩家不必"点一下试试"才知道被占。
                OccupiedSeatNumbers = [.. bindings.Select(binding => binding.Seat.Value).OrderBy(value => value)],
            });
        }

        return tables;
    }

    /// <summary>
    /// 创建一张新桌（D-0026：登录即可；D-0027：开桌即成为这一桌的说书人）。
    /// </summary>
    /// <param name="account">开桌者（由账号会话推导，客户端声明不可信）。</param>
    /// <param name="name">桌名（可为空 = 未命名）。</param>
    /// <param name="seatCount">席位数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 拒绝分两种，文案不混：**未登录**是会话问题，"**本服不开放自助开桌**"是部署开关收口
    /// （<see cref="TableCreationPolicy.AllowsPlayerTables"/>）。两者都不透露运维名单里有谁。
    /// </remarks>
    public async Task<LobbyCreateResultDto> CreateAsync(
        Account? account,
        string? name,
        int seatCount,
        CancellationToken cancellationToken)
    {
        if (!_tableCreation.CanCreate(account))
        {
            // `CanCreate` 为假只有两种可能：没登录，或部署方关掉了自助开桌（关掉时 `AllowsPlayerTables` 必为 false）。
            var reason = account is null ? "未登录" : "本服已关闭玩家自助开桌";
            _logger.LogWarning(
                "开桌被拒（授权）：account={AccountId} username={Username} 原因={Reason}",
                account?.Id.Value,
                account?.Username ?? "(未登录)",
                reason);

            return account is null
                ? Fail("invalid_session", "账号会话无效或已过期，请重新登录")
                : Fail("not_allowed", "本服当前不开放自助开桌，请联系运维开桌");
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

            var setup = GameSetupFactory.Create(gameId, seatCount, account!.Id) with { Name = trimmed };
            await _catalog.SaveAsync(setup, cancellationToken);
            await _registry.GetOrCreateAsync(gameId, cancellationToken);

            _logger.LogInformation(
                "已开桌：game={GameId} 桌名={Name} 席位={SeatCount} 开桌人={Username}（含账号 {AccountId}，他因此成为这一桌的说书人）",
                gameId.Value,
                trimmed.Length == 0 ? "(未命名)" : trimmed,
                seatCount,
                account.Username,
                account.Id.Value);

            return new LobbyCreateResultDto
            {
                Ok = true,
                Code = "ok",
                Message = "已开桌",
                GameId = gameId.Value,
                SeatCount = seatCount,
            };
        }

        _logger.LogError("开桌失败：连续 {Attempts} 次标识撞号", 5);
        return Fail("id_conflict", "开桌失败，请重试");
    }

    /// <summary>
    /// 改桌名 / 锁桌（说书人）。锁桌后**不再接受新的入座**，已在座的玩家不受影响。
    /// </summary>
    /// <param name="gameId">哪一桌。</param>
    /// <param name="name">新桌名；null = 不改名。</param>
    /// <param name="isLocked">新的锁定状态；null = 不改。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 元数据进会话目录（不进事件流）：它不影响任何规则判定，也不该出现在复盘里（D-0015）。
    /// </remarks>
    public async Task UpdateLobbyAsync(
        GameId gameId,
        string? name,
        bool? isLocked,
        CancellationToken cancellationToken)
    {
        var setup = await _catalog.FindAsync(gameId, cancellationToken)
            ?? throw new InvalidOperationException($"这一桌不存在：{gameId.Value}");

        var trimmed = name?.Trim();
        if (trimmed is not null && trimmed.Length > MaxNameLength)
        {
            throw new InvalidOperationException($"桌名最多 {MaxNameLength} 个字符");
        }

        var nextName = trimmed ?? setup.Name;
        var nextLocked = isLocked ?? setup.IsLocked;
        if (nextName == setup.Name && nextLocked == setup.IsLocked)
        {
            return;
        }

        await _catalog.UpdateLobbyAsync(gameId, nextName, nextLocked, cancellationToken);
        _logger.LogInformation(
            "桌元数据已更新：game={GameId} 桌名={Name} 锁定={Locked}",
            gameId.Value,
            nextName.Length == 0 ? "(未命名)" : nextName,
            nextLocked);
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
