using Microsoft.Extensions.Logging;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 配板建议的求解与组装：只读、不落账（口径见 R-0041 / R-0042 / R-0046），由 <see cref="GameSession"/> 在锁内调用。
/// </summary>
/// <remarks>
/// <para>
/// 拆在这里而不是塞进 <see cref="GameSession"/>：会话类只做状态门与锁，求解 + wire 形状翻译
/// 归这个窄接口；同时避免会话文件顶着架构行数上限（AGENTS.md「架构硬约束」）。
/// </para>
/// <para>
/// **旅行者是叠加角色**（R-0046）：输入是**非旅行者人数**，分布表按它取行；旅行者不占镇民 / 外来者 /
/// 爪牙 / 恶魔名额、不参与求解。非旅行者上限 15——总人数超过 15 时，超出部分必须是旅行者，否则显式失败。
/// 旅行者按 D1 以「追加席位」进入，故建议把非旅行者角色绑在**低号席**（高号席留给旅行者）；
/// 建议仍只是建议，说书人提交时可改绑（D-0017）。
/// </para>
/// </remarks>
internal static class SetupProposalQuery
{
    /// <summary>非旅行者人数上限（R-0046 第 2 条：总人数超过 15 的部分必须是旅行者）。</summary>
    private const int MaxNonTravellers = 15;

    /// <summary>
    /// 非旅行者人数 + 显式种子 → 席位映射 / 净分布 / 显式失败；<paramref name="setup"/> 为空 = 还没有席位名单。
    /// </summary>
    /// <param name="nonTravellerCount">配板覆盖的非旅行者人数；null = 本局全部席位都是非旅行者（缺省语义）。</param>
    public static SetupProposalResult Build(
        GameId gameId,
        GameSetup? setup,
        bool phaseStarted,
        string? seed,
        int? nonTravellerCount,
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

        var seatCount = setup.Seats.Count;
        var nonTravellers = nonTravellerCount ?? seatCount;
        // 调用面契约：非旅行者人数必须落在本局席位数以内（0 = 全员旅行者，交给分布表按 5–15 显式失败）。
        if (nonTravellers < 0 || nonTravellers > seatCount)
        {
            logger.LogInformation(
                "配板建议未生成：game={GameId} 非旅行者={NonTravellers} 席位数={SeatCount} 码=setup.non_traveller_count_invalid",
                gameId,
                nonTravellers,
                seatCount);
            return SetupProposalResult.Failed(
                "setup.non_traveller_count_invalid",
                $"非旅行者人数 {nonTravellers} 不在 0–{seatCount} 之间（本局席位总数 {seatCount}）。",
                nonTravellers,
                Math.Clamp(seatCount - nonTravellers, 0, seatCount));
        }

        if (nonTravellers > MaxNonTravellers)
        {
            logger.LogInformation(
                "配板建议未生成：game={GameId} 非旅行者={NonTravellers} 上限={MaxNonTravellers} 码=setup.player_count_unsupported",
                gameId,
                nonTravellers,
                MaxNonTravellers);
            return SetupProposalResult.Failed(
                "setup.player_count_unsupported",
                $"非旅行者 {nonTravellers} 人超过 15 人上限：总人数超过 15 时，超出部分必须是旅行者（R-0046）。",
                nonTravellers,
                seatCount - nonTravellers);
        }

        var resolvedSeed = string.IsNullOrWhiteSpace(seed) ? Guid.NewGuid().ToString("N") : seed;
        var composed = SetupComposer.Compose(nonTravellers, resolvedSeed);
        if (!composed.Ok)
        {
            var failure = composed.Error!;
            logger.LogInformation(
                "配板建议未生成：game={GameId} 非旅行者={NonTravellers} 码={Code} 原因={Message}",
                gameId,
                nonTravellers,
                failure.Code,
                failure.Message);
            return SetupProposalResult.Failed(
                FailureCodeOf(failure.Code),
                failure.Message,
                nonTravellers,
                seatCount - nonTravellers);
        }

        var proposal = composed.Proposal!;
        // 非旅行者按 D1 落在低号席（旅行者以「追加席位」进入 = 高号席）；建议只覆盖非旅行者部分。
        var seats = setup.Seats
            .Select(item => item.Seat)
            .OrderBy(seat => seat.Value)
            .Take(nonTravellers)
            .ToList();
        var assignments = seats
            .Select((seat, index) => new SeatCharacterAssignment
            {
                Seat = seat,
                Character = proposal.Bag[index],
            })
            .ToList();

        var travellerCount = seatCount - nonTravellers;
        var notes = new List<string>(proposal.Notes);
        if (travellerCount > 0)
        {
            notes.Add(
                $"本局旅行者 {travellerCount} 名：不参与配板、不占任何类型名额（R-0046）；默认落在高号席"
                + $"（第 {nonTravellers + 1}–{seatCount} 席），提交分配前可改绑。");
        }

        logger.LogInformation(
            "配板建议已生成：game={GameId} 非旅行者={NonTravellers} 旅行者={Travellers} 种子={Seed} 分布={Distribution}",
            gameId,
            nonTravellers,
            travellerCount,
            resolvedSeed,
            Describe(proposal.Counts));

        return new SetupProposalResult
        {
            Seed = resolvedSeed,
            NonTravellerCount = nonTravellers,
            TravellerCount = travellerCount,
            Assignments = assignments,
            Distribution =
            [
                new SetupTypeCount(nameof(CharacterType.Townsfolk), proposal.Counts.Townsfolk),
                new SetupTypeCount(nameof(CharacterType.Outsider), proposal.Counts.Outsiders),
                new SetupTypeCount(nameof(CharacterType.Minion), proposal.Counts.Minions),
                new SetupTypeCount(nameof(CharacterType.Demon), proposal.Counts.Demons),
            ],
            Notes = notes,
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
