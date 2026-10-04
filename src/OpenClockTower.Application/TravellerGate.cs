using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 旅行者加入 / 离开命令的闸（票据 `traveller-and-exile` D1）：身份与参数形状检查。
/// </summary>
/// <remarks>
/// 从 <see cref="CommandGatePipeline"/> 拆出（单文件 600 行门禁，与 <see cref="VoteSweepGate"/> 同款）：
/// 这里只回答"这条命令由谁发、参数形状对不对"；"这个席位能不能加入 / 离场"要读状态账，
/// 在内核侧 <see cref="TravellerCommandDispatch"/> 判（与"配板闸只看名单、求解器看数据"同一分工）。
/// 阶段上没有额外限制：加入 / 离场可以在**任意时刻**发生（含首个阶段之前；百科《旅行者》·
/// 2026-10-04 抓取 · 旅行者运作方式）。
/// </remarks>
internal static class TravellerGate
{
    /// <summary>身份闸：加入 / 离开都是说书人（或宿主）的动作。</summary>
    internal static CommandRejection? IdentityRejection(GameCommand command, Actor actor) =>
        command switch
        {
            JoinTravellerCommand or RemoveTravellerCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller
                => null,
            JoinTravellerCommand or RemoveTravellerCommand
                => Reject("identity", "identity.storyteller_only", "只有说书人或宿主可以安排旅行者加入 / 离开"),
            _ => Reject("identity", "identity.unknown_traveller_command", "不是旅行者命令（防御性兜底）"),
        };

    /// <summary>合法性闸：角色类型、阵营形状、目标席位与会话信息。</summary>
    internal static CommandRejection? LegalityRejection(GameCommand command, GameSetup? setup) =>
        command switch
        {
            JoinTravellerCommand join => CheckJoin(join, setup),
            RemoveTravellerCommand remove => SeatGate.CheckExists(remove.Seat, setup),
            _ => Reject("legality", "legality.unknown_traveller_command", "不是旅行者命令（防御性兜底）"),
        };

    private static CommandRejection? CheckJoin(JoinTravellerCommand join, GameSetup? setup)
    {
        if (setup is null)
        {
            return Reject("legality", "legality.setup_missing", "本局还没有会话信息（席位名单）");
        }

        if (!Enum.IsDefined(join.Alignment))
        {
            return Reject("legality", "legality.alignment_invalid", $"未知的阵营：{join.Alignment}");
        }

        if (SectsAndVioletsRoster.TypeOf(join.Character) != CharacterType.Traveller)
        {
            return Reject(
                "legality",
                "legality.traveller_character_invalid",
                $"角色 {join.Character.Value} 不是旅行者：加入旅行者只能使用花名册里的旅行者角色（R-0046）");
        }

        if (join.Seat is { } seat && SeatGate.CheckExists(seat, setup) is { } seatMissing)
        {
            return seatMissing;
        }

        if (join.RevealDemonSeats.Distinct().Count() != join.RevealDemonSeats.Count)
        {
            return Reject("legality", "legality.traveller_reveal_duplicated", "要告知的恶魔席位不能重复");
        }

        return join.Alignment switch
        {
            Alignment.Evil when join.RevealDemonSeats.Count == 0
                => Reject(
                    "legality",
                    "legality.traveller_reveal_required",
                    "邪恶旅行者加入必须由说书人指定要告知的存活恶魔（一名或全部；百科《旅行者》）"),
            Alignment.Good when join.RevealDemonSeats.Count > 0
                => Reject(
                    "legality",
                    "legality.traveller_reveal_not_allowed",
                    "善良旅行者不会得知恶魔：告知列表必须为空"),
            _ => null,
        };
    }

    private static CommandRejection Reject(string gate, string code, string message) =>
        GateRejections.Reject(code, message, gate);
}
