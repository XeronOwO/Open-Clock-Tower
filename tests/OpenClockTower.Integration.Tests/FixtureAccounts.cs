using Microsoft.Extensions.DependencyInjection;
using OpenClockTower.Application;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 夹具账号的签发台：注册 / 登录 / 续签会话，以及"每席一个账号"的缓存。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="TestServerHost"/> 拆出（单文件 600 行门禁）：宿主本身要管连接、库路径、命令与
/// 恢复场景，账号夹具是一条独立的关注点。
/// </para>
/// <para>
/// **入座必须登录**（D-0037）之后，夹具不能再拿"没有账号的游客"当默认形态：每一个要坐下的席位
/// 都得有真账号，而且**一局一账号一席**（服务端有唯一索引）——所以这里按席位缓存账号，
/// 重连类用例复用同一个（"同一个人刷新回来"本来就是真实用法）。
/// 夹具只借真实的账号服务造身份，不绕过任何产品判定。
/// </para>
/// </remarks>
internal sealed class FixtureAccounts
{
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _seatGate = new(1, 1);
    private readonly Dictionary<SeatId, FixtureAccount> _seatAccounts = [];

    /// <summary>构造签发台。</summary>
    /// <param name="services">宿主服务容器（取账号服务与会话登记表）。</param>
    internal FixtureAccounts(IServiceProvider services) => _services = services;

    /// <summary>注册一个夹具账号（走真实的账号服务，注册即登录）；失败显式抛出。</summary>
    internal async Task<FixtureAccount> RegisterAsync(string username, string displayName, string password)
    {
        var accounts = _services.GetRequiredService<AccountService>();
        var sessions = _services.GetRequiredService<AccountSessionRegistry>();

        var outcome = await accounts.RegisterAsync(username, displayName, password, CancellationToken.None);
        if (!outcome.Accepted || outcome.Account is null)
        {
            throw new InvalidOperationException($"夹具账号注册失败：{outcome.Code} {outcome.Message}");
        }

        return new FixtureAccount(
            outcome.Account.Id,
            outcome.Account.Username,
            outcome.Account.DisplayName,
            password,
            sessions.Issue(outcome.Account.Id).Value);
    }

    /// <summary>
    /// 这一席的**夹具账号**（就地注册并缓存）。
    /// </summary>
    /// <param name="seat">席位号。</param>
    internal async Task<FixtureAccount> ForSeatAsync(SeatId seat)
    {
        if (_seatAccounts.TryGetValue(seat, out var existing))
        {
            return existing;
        }

        // 同一个席位可能被**并发**加入（"四条连接抢同一席"的用例）：不加这道闸，
        // 四个调用会同时注册同一个登录名，撞上账号表的唯一索引——那是夹具的失败，不是产品的。
        await _seatGate.WaitAsync();
        try
        {
            if (_seatAccounts.TryGetValue(seat, out existing))
            {
                return existing;
            }

            var account = await RegisterOrSignInAsync(
                $"fixture-seat-{seat.Value}",
                $"夹具玩家{seat.Value}",
                FixtureSeatPassword);
            _seatAccounts[seat] = account;
            return account;
        }
        finally
        {
            _seatGate.Release();
        }
    }

    /// <summary>
    /// 给一个**已在册**的账号重新签发会话（重启场景：同一个库上的第二个宿主）。
    /// </summary>
    /// <param name="accountId">账号标识。</param>
    internal async Task<FixtureAccount> ReissueSessionAsync(AccountId accountId)
    {
        var accounts = _services.GetRequiredService<IAccountStore>();
        var sessions = _services.GetRequiredService<AccountSessionRegistry>();

        var account = await accounts.FindByIdAsync(accountId, CancellationToken.None)
            ?? throw new InvalidOperationException($"这个账号不存在：{accountId.Value}");

        return new FixtureAccount(
            account.Id,
            account.Username,
            account.DisplayName,
            FixtureOwnerPassword,
            sessions.Issue(account.Id).Value);
    }

    /// <summary>
    /// 注册夹具账号；**重启场景**（同一个库上的第二个宿主）里它已经在册，改为按同一口令登录。
    /// </summary>
    /// <remarks>
    /// "同一个账号在第二台设备上回来"本来就是真实用法，夹具不该因为登录名撞车而挂掉。
    /// </remarks>
    private async Task<FixtureAccount> RegisterOrSignInAsync(string username, string displayName, string password)
    {
        try
        {
            return await RegisterAsync(username, displayName, password);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("username_taken", StringComparison.Ordinal))
        {
            var accounts = _services.GetRequiredService<AccountService>();
            var sessions = _services.GetRequiredService<AccountSessionRegistry>();

            var outcome = await accounts.AuthenticateAsync(username, password, CancellationToken.None);
            if (!outcome.Accepted || outcome.Account is null)
            {
                throw new InvalidOperationException($"夹具账号已在册但登录失败：{outcome.Code} {outcome.Message}");
            }

            return new FixtureAccount(
                outcome.Account.Id,
                outcome.Account.Username,
                outcome.Account.DisplayName,
                password,
                sessions.Issue(outcome.Account.Id).Value);
        }
    }

    /// <summary>夹具房主账号的口令（只活在夹具里；重启场景重新签发会话时沿用它）。</summary>
    internal const string FixtureOwnerPassword = "fixture-owner-pw";

    /// <summary>夹具玩家账号的口令（只活在夹具里；入座必须登录，D-0037）。</summary>
    private const string FixtureSeatPassword = "fixture-seat-pw";
}
