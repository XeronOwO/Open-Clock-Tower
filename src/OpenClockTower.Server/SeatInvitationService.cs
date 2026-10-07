using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 席位邀请码的**签发与核验**（D-0038）：一枚有寿命、可轮换、只存哈希的席位凭据。
/// </summary>
/// <remarks>
/// <para>
/// 这是审计 G-A2-2 的落点。此前"邀请码"就是席位票据本身：明文写进 <c>Games.SeatsJson</c>、
/// 与席位同寿、不可作废，比较还是字符串直比。现在它走与账号会话同一套零件
/// （<see cref="SecretToken"/>：256 位密码学随机、只存 SHA-256、固定时间比较、日志只写短指纹），
/// 再加一条**到期时刻**与一次**覆盖即轮换**。
/// </para>
/// <para>
/// **明文只回给签发者一次**：核验、重投、重新查询都拿不回它（库里只有哈希）。因此
/// "凭据落进备份 = 席位永久失守"这条路被断掉了。
/// </para>
/// <para>
/// **不消费**（不做"用过即焚"）：席位认领本身就是一次性的——绑定落库之后这个席位归那个账号，
/// 谁是第一个来的谁就是本人。再叠一层"码用过就烧"只会多一个失败模式（玩家第一次点失败、
/// 码却烧掉了），却不减少任何攻击面（先到先得这件事消费与否都一样）。
/// </para>
/// </remarks>
public sealed class SeatInvitationService
{
    private readonly ISeatInvitationStore _store;
    private readonly IClock _clock;
    private readonly SeatInvitationOptions _options;
    private readonly ILogger<SeatInvitationService> _logger;

    /// <summary>构造签发 / 核验服务。</summary>
    public SeatInvitationService(
        ISeatInvitationStore store,
        IClock clock,
        SeatInvitationOptions options,
        ILogger<SeatInvitationService> logger)
    {
        _store = store;
        _clock = clock;
        _options = options;
        _logger = logger;

        if (options.LifetimeHours <= 0)
        {
            // 不拦启动（演练要用 0），但必须说出来：配成 0 之后签出来的码**当场过期**，
            // 表现是"玩家怎么说邀请码无效"，而日志里一句都不会有。
            _logger.LogWarning(
                "席位邀请码的有效期配成了 {Hours} 小时：签出来的邀请码会立刻过期，玩法上等于关掉了邀请",
                options.LifetimeHours);
        }
    }

    /// <summary>
    /// 为某席位签发（或轮换）邀请码：返回**明文一次**，库里只留哈希。
    /// </summary>
    /// <param name="gameId">哪一桌。</param>
    /// <param name="seat">哪一个席位。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 重复签发就是轮换：旧的那一枚当场失效——"码发错人了"因此有一个干净的收场，
    /// 而不是只能眼睁睁看着它一直有效。
    /// </remarks>
    public async Task<IssuedSeatInvitation> IssueAsync(
        GameId gameId,
        SeatId seat,
        CancellationToken cancellationToken)
    {
        // 前缀只是给人看的（"这是 3 号席的码"）：它不参与核验，核验比的是整串的哈希。
        // 席位号本来就在大厅里公开，写进码里不泄露任何东西。
        var code = $"seat-{seat.Value}-{SecretToken.CreateNew()}";
        var expiresAt = _clock.UtcNow + _options.Lifetime;

        await _store.ReplaceAsync(
            gameId,
            new SeatInvitation
            {
                Seat = seat,
                Hash = SecretToken.HashOf(code),
                ExpiresAt = expiresAt,
            },
            cancellationToken);

        _logger.LogInformation(
            "已签发席位邀请码：game={GameId} seat={Seat} 到期={ExpiresAt:o} 指纹={Fingerprint}"
            + "（明文只在下发那一刻存在；重复签发即轮换，旧码当场失效）",
            gameId.Value,
            seat.Value,
            expiresAt,
            SecretToken.FingerprintOf(code));

        return new IssuedSeatInvitation { Seat = seat, Code = code, ExpiresAt = expiresAt };
    }

    /// <summary>核验一枚邀请码：命中且未过期才给出席位。</summary>
    /// <param name="gameId">哪一桌（邀请码只在签给它的那一桌上有效）。</param>
    /// <param name="code">玩家出示的邀请码明文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<SeatInvitationRedemption> RedeemAsync(
        GameId gameId,
        string? code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code))
        {
            return SeatInvitationRedemption.Reject(SeatInvitationRedemption.UnknownReason);
        }

        var hash = SecretToken.HashOf(code);
        var now = _clock.UtcNow;
        var invitations = await _store.ListByGameAsync(gameId, cancellationToken);

        SeatInvitation? matched = null;
        foreach (var invitation in invitations)
        {
            // **逐条比完、不提前退出**：命中在第几条不该从耗时上读出来。
            if (matched is null && CryptographicOperations.FixedTimeEquals(invitation.Hash, hash))
            {
                matched = invitation;
            }
        }

        if (matched is null)
        {
            _logger.LogWarning(
                "邀请码核验失败：game={GameId} 原因=本局没有这一枚 指纹={Fingerprint} 本局邀请数={Count}",
                gameId.Value,
                SecretToken.FingerprintOf(code),
                invitations.Count);
            return SeatInvitationRedemption.Reject(SeatInvitationRedemption.UnknownReason);
        }

        if (matched.ExpiresAt <= now)
        {
            _logger.LogWarning(
                "邀请码核验失败：game={GameId} seat={Seat} 原因=已过期 到期={ExpiresAt:o} 现在={Now:o} 指纹={Fingerprint}",
                gameId.Value,
                matched.Seat.Value,
                matched.ExpiresAt,
                now,
                SecretToken.FingerprintOf(code));
            return SeatInvitationRedemption.Reject(SeatInvitationRedemption.ExpiredReason);
        }

        return SeatInvitationRedemption.Accept(matched.Seat);
    }
}
