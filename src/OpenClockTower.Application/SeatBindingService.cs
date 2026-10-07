using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 席位认领（D-0021）：一席一账号、一账号一席；绑定属会话信息，不进事件流。
/// </summary>
/// <remarks>
/// 认领只在「登录 + 本局票据」首次加入时发生一次；重复认领幂等（同一账号同一席返回原绑定），
/// 并发竞态落到存储的唯一索引上，再由这里重新读一次收敛成"幂等"或"明确拒绝"，不静默通过。
/// <para>
/// **两次读之间会被别人的写入穿过**（"读倾斜"）：并发下席位那次读可能发生在对方写入之前（看不到），
/// 账号那次读发生在之后（看得到）。这时读到的"我已经占的席位"就是**本次正要认领的这一席**——
/// 与开头那条同账号同席的快路径是同一件事，必须给同一个结论（幂等接受），不能当成冲突拒绝。
/// 现场是集成套件在集合并行下的随机红：四条同账号同席的并发加入里偶有一条被拒。
/// </para>
/// </remarks>
public sealed class SeatBindingService
{
    private readonly ISeatBindingStore _bindings;
    private readonly IClock _clock;

    /// <summary>构造认领服务。</summary>
    public SeatBindingService(ISeatBindingStore bindings, IClock clock)
    {
        _bindings = bindings;
        _clock = clock;
    }

    /// <summary>认领一个席位；同一账号同一席重复调用返回原绑定（幂等）。</summary>
    public async Task<SeatBindingOutcome> ClaimAsync(
        GameId gameId,
        SeatId seat,
        AccountId accountId,
        CancellationToken cancellationToken)
    {
        var existing = await _bindings.FindBySeatAsync(gameId, seat, cancellationToken);
        if (existing is not null)
        {
            return existing.AccountId == accountId
                ? Accept(existing, created: false)
                : SeatTaken(seat);
        }

        var mine = await _bindings.FindByAccountAsync(gameId, accountId, cancellationToken);
        if (mine is not null)
        {
            // 这一席正是它自己那一席 = 两次读之间夹进了**自己**刚才那次写入（并发下的常态），
            // 走幂等那条路；只有"占的是别的一席"才可能是真的越界。
            if (mine.Seat == seat)
            {
                return Accept(mine, created: false);
            }

            // 而"占的是别的一席"本身也可能是一份**过期读**：那一行刚被解除（说书人移人 / 账号注销）。
            // 按它再读一次席位表，只有那条绑定**现在仍然在**才拒绝；否则继续往下走，由唯一索引收敛。
            var still = await _bindings.FindBySeatAsync(gameId, mine.Seat, cancellationToken);
            if (still is not null && still.AccountId == accountId)
            {
                return Reject(
                    "account_already_seated",
                    $"这个账号在本局已经认领了席位 {still.Seat.Value}，不能再认领席位 {seat.Value}");
            }
        }

        var binding = new SeatBinding
        {
            GameId = gameId,
            Seat = seat,
            AccountId = accountId,
            BoundAt = _clock.UtcNow,
        };

        if (await _bindings.TryBindAsync(binding, cancellationToken))
        {
            return Accept(binding, created: true);
        }

        // 并发竞态：唯一索引挡下后重新读一次，按结果收敛——幂等成功，或明确拒绝。
        var raced = await _bindings.FindBySeatAsync(gameId, seat, cancellationToken);
        if (raced is not null)
        {
            return raced.AccountId == accountId ? Accept(raced, created: false) : SeatTaken(seat);
        }

        // 席位读为空而写入仍被挡下：挡的是"一账号一席"那条唯一索引，把它占的那一席读出来说清楚。
        var elsewhere = await _bindings.FindByAccountAsync(gameId, accountId, cancellationToken);
        return elsewhere is not null
            ? Reject(
                "account_already_seated",
                $"这个账号在本局已经认领了席位 {elsewhere.Seat.Value}，不能再认领席位 {seat.Value}")
            : Reject("conflict", $"席位 {seat.Value} 的认领与另一次写入撞在一起且已各自退场，请重试");
    }

    /// <summary>按账号解出本局席位（认领之后的"只凭账号重连"路径）；没有返回 null。</summary>
    public async Task<SeatBinding?> ResolveSeatAsync(
        GameId gameId,
        AccountId accountId,
        CancellationToken cancellationToken) =>
        await _bindings.FindByAccountAsync(gameId, accountId, cancellationToken);

    /// <summary>说书人 / 宿主解除席位绑定（误认领兜底）；没有绑定返回 false。</summary>
    public async Task<bool> ReleaseAsync(GameId gameId, SeatId seat, CancellationToken cancellationToken) =>
        await _bindings.TryReleaseAsync(gameId, seat, cancellationToken);

    private static SeatBindingOutcome Accept(SeatBinding binding, bool created) => new()
    {
        Accepted = true,
        Code = "ok",
        Binding = binding,
        Created = created,
    };

    private static SeatBindingOutcome SeatTaken(SeatId seat) =>
        Reject("seat_taken", $"席位 {seat.Value} 已经由其他账号认领");

    private static SeatBindingOutcome Reject(string code, string message) => new()
    {
        Accepted = false,
        Code = code,
        Message = message,
        Created = false,
    };
}
