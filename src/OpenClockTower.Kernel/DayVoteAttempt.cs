namespace OpenClockTower.Kernel;

/// <summary>
/// 白天一次投票动作的**原始事实**：谁在第几项提名上投了赞成 / 撤回，以及**动作发生时**他的角色。
/// </summary>
/// <remarks>
/// <para>
/// 角色是**时刻快照**，不是"现在查账"：依据百科《卖花女孩》· 2026-10-01 抓取 · 角色简介 4——
/// 「如果在原恶魔投票之后，卖花女孩得知该信息之前，恶魔玩家发生了改变，卖花女孩的能力还是会
/// 检测到**原恶魔**是否投票」；《城镇公告员》的「爪牙提名」同理。换角（麻脸巫婆 / 舞蛇人 /
/// 理发师 / 说书人手工上报，R-0032）之后再去查当前角色就会答错。
/// </para>
/// <para>
/// 记的是**举手动作**而不是"计票名单"：在线投票窗口允许反复改票 / 撤回（R-0017），
/// 而线下「举手」是一次可见动作；两者的对应口径见 R-0037。角色维度未观测时快照为 null
/// （不猜：不把"不知道"写成"不是恶魔"），动作本身照记——投票合法性不依赖角色。
/// </para>
/// </remarks>
public sealed record DayVoteAttempt
{
    /// <summary>投的是当天第几次提名（与 <see cref="NominationRecord.Index"/> 同一把键）。</summary>
    public required int NominationIndex { get; init; }

    /// <summary>投票 / 改票的席位。</summary>
    public required SeatId Voter { get; init; }

    /// <summary>动作发生时的角色快照；该维度未观测时为 null。</summary>
    public CharacterId? VoterCharacter { get; init; }

    /// <summary>true = 投赞成（举手）；false = 撤回。</summary>
    public required bool Voted { get; init; }
}
