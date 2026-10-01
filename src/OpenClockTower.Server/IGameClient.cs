using OpenClockTower.Contracts;

namespace OpenClockTower.Server;

/// <summary>推送给客户端的强类型方法（SignalR 强类型 Hub 的客户端契约）。</summary>
public interface IGameClient
{
    /// <summary>服务端主动推来一条操作请求（定向单播）。</summary>
    Task ReceiveOperationRequest(OperationRequestDto request);

    /// <summary>服务端告知"你的请求被作废了及原因"。</summary>
    Task ReceiveOperationRequestVoided(OperationRequestVoidedDto voided);

    /// <summary>说书人视图发生变化（卡点 / 控制模式 / 阻塞等）。</summary>
    Task ReceiveStorytellerViewChanged(StorytellerViewDto view);

    /// <summary>服务端推给某个玩家的信息类结果（定向单播；只有内容）。</summary>
    Task ReceiveInformationResult(InformationResultDto information);
}
