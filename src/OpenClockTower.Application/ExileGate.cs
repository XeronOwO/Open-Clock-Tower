using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 流放命令的闸（票据 traveller-and-exile · D2）：身份与参数形状的检查。
/// </summary>
/// <remarks>
/// 从 <see cref="CommandGatePipeline"/> 拆出（与 <see cref="VoteSweepGate"/> 同款）：这里只回答
/// "这条命令由谁发、参数形状对不对"；"现在能不能提 / 能不能收"由内核按白天账判（与内核同尺）。
/// 流放不是提名——两条命令族的闸各持一份，不混进同一个 switch（R-0044 第 1 条）。
/// </remarks>
internal static class ExileGate
{
    /// <summary>身份闸：提议 / 举手是玩家动作（含死者），开始 / 继续 / 计票归说书人，逐席到点只认系统。</summary>
    internal static CommandRejection? IdentityRejection(GameCommand command, Actor actor) =>
        command switch
        {
            ProposeExileCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null,
            ProposeExileCommand => Reject("identity", "identity.player_only", "只有持席位的玩家可以发起流放提议"),

            CastExileVoteCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null,
            CastExileVoteCommand => Reject("identity", "identity.player_only", "只有持席位的玩家可以在流放表决里举手"),

            StartExileSweepCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            StartExileSweepCommand => Reject(
                "identity",
                "identity.storyteller_only",
                "只有说书人或宿主可以开始流放收票"),

            ResumeExileSweepCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            ResumeExileSweepCommand => Reject(
                "identity",
                "identity.storyteller_only",
                "只有说书人或宿主可以继续流放收票"),

            CountExileVotesCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            CountExileVotesCommand => Reject(
                "identity",
                "identity.storyteller_only",
                "只有说书人或宿主可以给流放计票"),

            CollectExileSeatVoteCommand when actor.Kind == ActorKind.System => null,
            CollectExileSeatVoteCommand => Reject(
                "identity",
                "identity.system_only",
                "流放收票到点输入只能由系统节拍器发出"),

            _ => Reject("identity", "identity.unknown_exile_command", "不是流放命令（防御性兜底）"),
        };

    /// <summary>合法性闸：序号形状、节奏参数范围与席位存在性（与内核同尺）。</summary>
    internal static CommandRejection? LegalityRejection(GameCommand command, GameSetup? setup) =>
        command switch
        {
            ProposeExileCommand propose => SeatGate.CheckExists(propose.Target, setup),
            CastExileVoteCommand castVote => CheckExileIndex(castVote.ExileIndex),
            StartExileSweepCommand startSweep when !VoteSweepLimits.IsCountdownValid(startSweep.CountdownMilliseconds)
                => Reject(
                    "legality",
                    "legality.sweep_countdown_invalid",
                    $"倒计时必须在 {VoteSweepLimits.MinCountdownMilliseconds}–{VoteSweepLimits.MaxCountdownMilliseconds} 毫秒之间"),
            StartExileSweepCommand startSweep when !VoteSweepLimits.IsIntervalValid(startSweep.IntervalMilliseconds)
                => Reject(
                    "legality",
                    "legality.sweep_interval_invalid",
                    $"逐席间隔必须在 {VoteSweepLimits.MinIntervalMilliseconds}–{VoteSweepLimits.MaxIntervalMilliseconds} 毫秒之间"),
            StartExileSweepCommand startSweep => CheckExileIndex(startSweep.ExileIndex),
            ResumeExileSweepCommand resumeSweep => CheckExileIndex(resumeSweep.ExileIndex),
            CountExileVotesCommand countVotes => CheckExileIndex(countVotes.ExileIndex),
            CollectExileSeatVoteCommand collectSeat => CheckExileIndex(collectSeat.ExileIndex)
                ?? SeatGate.CheckExists(collectSeat.Seat, setup),
            _ => Reject("legality", "legality.unknown_exile_command", "不是流放命令（防御性兜底）"),
        };

    private static CommandRejection? CheckExileIndex(int index) =>
        index < 1
            ? Reject("legality", "legality.exile_index_invalid", $"流放序号必须从 1 开始：{index}")
            : null;

    private static CommandRejection Reject(string gate, string code, string message) =>
        GateRejections.Reject(code, message, gate);
}
