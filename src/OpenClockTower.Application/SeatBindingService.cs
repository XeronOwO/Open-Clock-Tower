using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 席位认领（D-0021）：一席一账号、一账号一席；绑定属会话信息，不进事件流。
/// </summary>
/// <remarks>
/// 认领只在「登录 + 本局票据」首次加入时发生一次；重复认领幂等（同一账号同一席返回原绑定），
/// 并发竞态落到存储的唯一索引上，再由这里重新读一次收敛成"幂等"或"明确拒绝"，不静默通过。
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
                ? Accept(existing)
                : Reject("seat_taken", $"席位 {seat.Value} 已经由其他账号认领");
        }

        var mine = await _bindings.FindByAccountAsync(gameId, accountId, cancellationToken);
        if (mine is not null)
        {
            return Reject("account_already_seated", $"这个账号在本局已经认领了席位 {mine.Seat.Value}");
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
            return Accept(binding);
        }

        // 并发竞态：唯一索引挡下后重新读一次，按结果收敛——幂等成功，或明确拒绝。
        var raced = await _bindings.FindBySeatAsync(gameId, seat, cancellationToken);
        return raced is not null && raced.AccountId == accountId
            ? Accept(raced)
            : Reject(
                raced is null ? "account_already_seated" : "seat_taken",
                "席位认领冲突：这个席位或这个账号刚刚被占用，请重试");
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

    private static SeatBindingOutcome Accept(SeatBinding binding) => new()
    {
        Accepted = true,
        Code = "ok",
        Binding = binding,
    };

    private static SeatBindingOutcome Reject(string code, string message) => new()
    {
        Accepted = false,
        Code = code,
        Message = message,
    };
}
