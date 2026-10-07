using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 旅行者加入 / 离开 / 离场申请命令的闸（票据 `traveller-and-exile` D1 + 本批 D-0037）：
/// 身份与参数形状检查。
/// </summary>
/// <remarks>
/// 从 <see cref="CommandGatePipeline"/> 拆出（单文件 600 行门禁，与 <see cref="VoteSweepGate"/> 同款）：
/// 这里只回答"这条命令由谁发、参数形状对不对"；"这个席位能不能加入 / 离场 / 申请"要读状态账，
/// 在内核侧 <see cref="TravellerCommandDispatch"/> 判（与"配板闸只看名单、求解器看数据"同一分工）。
/// 阶段上没有额外限制：四者都可以在**任意时刻**发生（含首个阶段之前；百科《旅行者》·
/// 2026-10-04 抓取 · 旅行者运作方式）。
/// </remarks>
internal static class TravellerGate
{
    /// <summary>身份闸：加入 / 直接移出 / 裁定申请是说书人（或宿主）的动作；**申请离场是旅行者本人**的动作。</summary>
    internal static CommandRejection? IdentityRejection(GameCommand command, Actor actor) =>
        command switch
        {
            JoinTravellerCommand or RemoveTravellerCommand or ResolveTravellerDepartureCommand
                when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            JoinTravellerCommand or RemoveTravellerCommand or ResolveTravellerDepartureCommand
                => Reject("identity", "identity.storyteller_only", "只有说书人或宿主可以安排旅行者加入 / 离开 / 裁定离场申请"),

            // 申请由旅行者本人提出：席位从连接凭据推导，命令面无自称身份（D-0012）。
            RequestTravellerDepartureCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null,
            RequestTravellerDepartureCommand
                => Reject("identity", "identity.player_only", "只有已入座的玩家本人可以提出离场申请"),

            _ => Reject("identity", "identity.unknown_traveller_command", "不是旅行者命令（防御性兜底）"),
        };

    /// <summary>合法性闸：角色类型、阵营形状、目标席位与会话信息。</summary>
    internal static CommandRejection? LegalityRejection(GameCommand command, GameSetup? setup) =>
        command switch
        {
            JoinTravellerCommand join => CheckJoin(join, setup),
            RemoveTravellerCommand remove => SeatGate.CheckExists(remove.Seat, setup),
            ResolveTravellerDepartureCommand resolve => SeatGate.CheckExists(resolve.Seat, setup),
            // 申请面不带席位（它由凭据推导），因此没有名单可查；"是不是在座的旅行者"在内核侧判。
            RequestTravellerDepartureCommand => null,
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
