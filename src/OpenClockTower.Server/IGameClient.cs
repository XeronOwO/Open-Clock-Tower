using OpenClockTower.Contracts;

namespace OpenClockTower.Server;

/// <summary>推送给客户端的强类型方法（SignalR 强类型 Hub 的客户端契约）。</summary>
public interface IGameClient
{
    /// <summary>服务端主动推来一条操作请求（定向单播）。</summary>
    Task ReceiveOperationRequest(OperationRequestDto request);

    /// <summary>服务端告知"你的请求被作废了及原因"。</summary>
    Task ReceiveOperationRequestVoided(OperationRequestVoidedDto voided);

    /// <summary>服务端告知"你的请求已被响应"（玩家本人作答或说书人代填）。</summary>
    Task ReceiveOperationRequestAnswered(OperationRequestAnsweredDto answered);

    /// <summary>服务端广播阶段开始（公开信息：昼夜；逐连接定向发送给每个已绑定席位）。</summary>
    Task ReceivePhaseStarted(PhaseStartedDto phase);

    /// <summary>说书人视图发生变化（卡点 / 控制模式 / 阻塞等）。</summary>
    Task ReceiveStorytellerViewChanged(StorytellerViewDto view);

    /// <summary>服务端推给某个玩家的信息类结果（定向单播；只有内容）。</summary>
    Task ReceiveInformationResult(InformationResultDto information);

    /// <summary>服务端广播白天状态（公开信息；按席位投影，含"我现在能不能动"）。</summary>
    Task ReceiveDayChanged(PlayerDayDto day);

    /// <summary>服务端广播"本局结束"（胜方 + 条件 + 说明；公开信息，R-0024）。</summary>
    Task ReceiveGameEnded(GameOutcomeDto outcome);

    /// <summary>服务端广播呆瓜的公开选择（公开事实，R-0027）。</summary>
    Task ReceiveKlutzChoiceMade(KlutzChoiceDto choice);

    /// <summary>
    /// 服务端推送"你的视图变了"：本人整视图（快照口径；艺术家提问状态 / 阶段边界等）。
    /// </summary>
    /// <param name="sequence">这份视图被表达时的序号（与快照同源，客户端按它合并）。</param>
    /// <param name="view">该席位的完整投影（不含任何他人字段）。</param>
    Task ReceivePlayerViewChanged(long sequence, PlayerViewDto view);
}
