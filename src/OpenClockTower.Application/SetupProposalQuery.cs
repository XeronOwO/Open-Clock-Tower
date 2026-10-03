using Microsoft.Extensions.Logging;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 配板建议的求解与组装：只读、不落账（口径见 R-0041 / R-0042），由 <see cref="GameSession"/> 在锁内调用。
/// </summary>
/// <remarks>
/// 拆在这里而不是塞进 <see cref="GameSession"/>：会话类只做状态门与锁，求解 + wire 形状翻译
/// 归这个窄接口；同时避免会话文件顶着架构行数上限（AGENTS.md「架构硬约束」）。
/// </remarks>
internal static class SetupProposalQuery
{
    /// <summary>人数 + 显式种子 → 席位映射 / 净分布 / 显式失败；<paramref name="setup"/> 为空 = 还没有席位名单。</summary>
    public static SetupProposalResult Build(
        GameId gameId,
        GameSetup? setup,
        bool phaseStarted,
        string? seed,
        ILogger logger)
    {
        if (setup is null || setup.Seats.Count == 0)
        {
            return SetupProposalResult.Failed("setup.no_session", "本局还没有会话信息（席位名单），无法配板。");
        }

        if (phaseStarted)
        {
            return SetupProposalResult.Failed("setup.phase_started", "配板只在首个阶段开始前可用（开局设置）。");
        }

        var resolvedSeed = string.IsNullOrWhiteSpace(seed) ? Guid.NewGuid().ToString("N") : seed;
        var composed = SetupComposer.Compose(setup.Seats.Count, resolvedSeed);
        if (!composed.Ok)
        {
            var failure = composed.Error!;
            logger.LogInformation(
                "配板建议未生成：game={GameId} 人数={SeatCount} 码={Code} 原因={Message}",
                gameId,
                setup.Seats.Count,
                failure.Code,
                failure.Message);
            return SetupProposalResult.Failed(FailureCodeOf(failure.Code), failure.Message);
        }

        var proposal = composed.Proposal!;
        var seats = setup.Seats.Select(item => item.Seat).OrderBy(seat => seat.Value).ToList();
        var assignments = seats
            .Select((seat, index) => new SeatCharacterAssignment
            {
                Seat = seat,
                Character = proposal.Bag[index],
            })
            .ToList();

        logger.LogInformation(
            "配板建议已生成：game={GameId} 人数={SeatCount} 种子={Seed} 分布={Distribution}",
            gameId,
            seats.Count,
            resolvedSeed,
            Describe(proposal.Counts));

        return new SetupProposalResult
        {
            Seed = resolvedSeed,
            Assignments = assignments,
            Distribution =
            [
                new SetupTypeCount(nameof(CharacterType.Townsfolk), proposal.Counts.Townsfolk),
                new SetupTypeCount(nameof(CharacterType.Outsider), proposal.Counts.Outsiders),
                new SetupTypeCount(nameof(CharacterType.Minion), proposal.Counts.Minions),
                new SetupTypeCount(nameof(CharacterType.Demon), proposal.Counts.Demons),
            ],
            Notes = proposal.Notes,
        };
    }

    private static string FailureCodeOf(SetupComposeResult.FailureCode code) => code switch
    {
        SetupComposeResult.FailureCode.PlayerCountUnsupported => "setup.player_count_unsupported",
        SetupComposeResult.FailureCode.PoolExhausted => "setup.pool_exhausted",
        SetupComposeResult.FailureCode.DistributionConflict => "setup.distribution_conflict",
        _ => "setup.unknown",
    };

    private static string Describe(SetupCounts counts) =>
        $"镇民 {counts.Townsfolk} / 外来者 {counts.Outsiders} / 爪牙 {counts.Minions} / 恶魔 {counts.Demons}";
}
