namespace OpenClockTower.Server;

/// <summary>
/// 席位邀请码的**有效期**（D-0038）：说书人签发的邀请码活多久。
/// </summary>
/// <remarks>
/// <para>
/// 这是审计 G-A2-2 里"不过期"那一半的答案：邀请码从前与席位同寿（明文落库、永不作废），
/// 泄露出去就是一张永久通行证。现在它是一条**有寿命的凭据**，到期即作废、轮换即失效。
/// </para>
/// <para>
/// 默认 24 小时：够"说书人今天发、玩家明天来"的用法（跨夜叫人、临时换设备），
/// 又不至于让一枚走漏的码长期有效。**0 = 立即过期**（演练 / 验证"过期这条路真的会拒"用）。
/// </para>
/// <para>判定与动作在 <see cref="SeatInvitationService"/>；口径全文见 D-0038。</para>
/// </remarks>
public sealed class SeatInvitationOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "GameServer:SeatInvitation";

    /// <summary>邀请码有效期（小时），默认 24；0 = 立即过期。</summary>
    public int LifetimeHours { get; set; } = 24;

    /// <summary>有效期（时长形态；负数按 0 处理）。</summary>
    public TimeSpan Lifetime => TimeSpan.FromHours(Math.Max(0, LifetimeHours));
}
