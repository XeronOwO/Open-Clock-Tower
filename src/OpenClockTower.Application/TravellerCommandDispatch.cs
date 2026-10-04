using Microsoft.Extensions.Logging;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 旅行者加入 / 离开的内核产出（票据 `traveller-and-exile` D1）：读账的状态相关校验 + 事件。
/// </summary>
/// <remarks>
/// 分派本身无副作用（只有日志）：加入产出「六维度账事件 + 加入事实 +（邪恶）私密揭示」，
/// 离开产出「离场事实」；席位票据的签发与持久化在 <see cref="GameSession"/> 编排。
/// 两个命令都允许在任意时刻发生，因此**必须原样透传步骤机状态**（不能把进行中的阶段抹掉）。
/// </remarks>
internal static class TravellerCommandDispatch
{
    private const string JoinReason = "traveller.joined";

    /// <summary>分派一条旅行者命令。</summary>
    internal static CommandDispatchResult Dispatch(
        GameCommand command,
        StepMachineState? machine,
        GameSetup setup,
        GameState state,
        GameId gameId,
        ILogger logger) =>
        command switch
        {
            JoinTravellerCommand join => Join(join, machine, setup, state, gameId, logger),
            RemoveTravellerCommand remove => Remove(remove, machine, state, gameId, logger),
            _ => CommandDispatchResult.Rejected(new CommandRejection
            {
                Code = "kernel.unsupported",
                Message = $"未支持的旅行者命令：{command.GetType().Name}",
                Gate = "kernel",
            }),
        };

    private static CommandDispatchResult Join(
        JoinTravellerCommand join,
        StepMachineState? machine,
        GameSetup setup,
        GameState state,
        GameId gameId,
        ILogger logger)
    {
        if (join.Seat is not { } seat)
        {
            // GameSession 应已把"未指定席位"解析成服务端追加的新席位（含票据）；
            // 到这里还是 null 说明接线漏了，显式拒绝而不是猜一个席位。
            return Reject(
                "legality.traveller_seat_unresolved",
                "旅行者加入的席位还没有解析（服务端分配席位的路径没有生效）");
        }

        if (state.HasDeparted(seat))
        {
            return Reject("legality.seat_departed", $"席位 {seat.Value} 已经离场：不能再作为加入落点");
        }

        if (state.Seat(seat)?.CharacterValue is not null)
        {
            return Reject("legality.seat_occupied", $"席位 {seat.Value} 已经有角色：加入只能落在尚未分配的席位");
        }

        if (state.Seats.FirstOrDefault(entry => entry.CharacterValue == join.Character) is { } holder)
        {
            return Reject(
                "legality.character_duplicated",
                $"角色 {join.Character.Value} 已经属于席位 {holder.Seat.Value}；角色唯一");
        }

        if (join.Alignment == Alignment.Evil && CheckReveal(join, setup, state) is { } revealFailure)
        {
            return CommandDispatchResult.Rejected(revealFailure);
        }

        var events = new List<GameEvent>
        {
            // 六维度初始条件（存活 / 清醒 / 健康）：与开局分配同一条口径（R-0015 / R-0016），
            // 阵营由说书人私下给出，不从角色类型推导（旅行者没有"类型对应阵营"）。
            new SeatStateChangedEvent
            {
                Seat = seat,
                Character = join.Character,
                Alignment = join.Alignment,
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = JoinReason,
            },
            new TravellerJoinedEvent
            {
                Seat = seat,
                Character = join.Character,
                Alignment = join.Alignment,
            },
        };

        if (join.Alignment == Alignment.Evil)
        {
            // 私密信息面：只有这名旅行者看得到（D-0012 §4.3）；内容由说书人选定的席位拼出，
            // 不是能力判定，因此 MayBeFalse = false。
            events.Add(new InformationResultIssuedEvent
            {
                Recipient = seat,
                Ability = TravellerJoinInfo.EvilRevealAbility,
                Content = TravellerJoinInfo.ComposeEvilReveal(join.RevealDemonSeats),
                MayBeFalse = false,
                Note = "邪恶旅行者加入：按说书人指定告知存活恶魔"
                    + "（百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式第 2 步）",
            });
        }

        logger.LogInformation(
            "旅行者已加入：game={GameId} seat={Seat} character={Character} alignment={Alignment} "
                + "公开宣告=角色+能力（阵营不公开） 告知恶魔={RevealCount}",
            gameId,
            seat.Value,
            join.Character.Value,
            join.Alignment,
            join.RevealDemonSeats.Count);

        return new CommandDispatchResult(machine, events, null);
    }

    /// <summary>邪恶旅行者的告知目标必须是**在局的存活恶魔**（百科《旅行者》· 旅行者运作方式第 2 步）。</summary>
    private static CommandRejection? CheckReveal(JoinTravellerCommand join, GameSetup setup, GameState state)
    {
        var inGame = InGameSeats.Derive(setup, state);
        foreach (var seat in join.RevealDemonSeats)
        {
            if (!inGame.Contains(seat))
            {
                return LegalityReject("legality.traveller_reveal_invalid", $"告知目标 {seat.Value} 号不在在局座次里");
            }

            var entry = state.Seat(seat);
            if (entry?.LifeValue != LifeState.Alive
                || entry.CharacterValue is not { } character
                || SectsAndVioletsRoster.TypeOf(character) != CharacterType.Demon)
            {
                return LegalityReject(
                    "legality.traveller_reveal_invalid",
                    $"告知目标 {seat.Value} 号不是存活的恶魔：只能告知存活恶魔（百科《旅行者》）");
            }
        }

        return null;
    }

    private static CommandDispatchResult Remove(
        RemoveTravellerCommand remove,
        StepMachineState? machine,
        GameState state,
        GameId gameId,
        ILogger logger)
    {
        if (state.HasDeparted(remove.Seat))
        {
            return Reject("legality.seat_departed", $"席位 {remove.Seat.Value} 已经离场");
        }

        var entry = state.Seat(remove.Seat);
        if (entry?.CharacterValue is not { } character)
        {
            return Reject(
                "legality.traveller_not_joined",
                $"席位 {remove.Seat.Value} 还没有加入任何旅行者：没有可移除的角色与生命标记");
        }

        if (SectsAndVioletsRoster.TypeOf(character) != CharacterType.Traveller)
        {
            return Reject(
                "legality.not_a_traveller",
                $"席位 {remove.Seat.Value} 的角色 {character.Value} 不是旅行者：离场流程只适用于旅行者（D-0022 范围）");
        }

        logger.LogInformation(
            "旅行者已离场：game={GameId} seat={Seat} character={Character} 说明={Note}",
            gameId,
            remove.Seat.Value,
            character.Value,
            remove.Note);

        return new CommandDispatchResult(machine, [new TravellerDepartedEvent { Seat = remove.Seat, Note = remove.Note }], null);
    }

    private static CommandDispatchResult Reject(string code, string message) =>
        CommandDispatchResult.Rejected(LegalityReject(code, message));

    private static CommandRejection LegalityReject(string code, string message) =>
        new() { Code = code, Message = message, Gate = "legality" };
}
