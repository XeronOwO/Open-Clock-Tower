namespace OpenClockTower.Application;

/// <summary>需要推送给客户端的通知类别。</summary>
public enum GameNotificationKind
{
    /// <summary>向被请求的玩家定向推送操作请求。</summary>
    OperationRequestIssued,

    /// <summary>向被请求的玩家推送"你的请求被作废了及原因"。</summary>
    OperationRequestVoided,

    /// <summary>说书人视图有变化（卡点列表 / 控制模式 / 阻塞等）。</summary>
    StorytellerViewChanged,

    /// <summary>房间已按事件日志重建完成。</summary>
    RoomRebuilt,

    /// <summary>推给某个玩家的信息类结果（只有内容，「可能为假」不下发）。</summary>
    InformationResultIssued,

    /// <summary>向被请求的玩家推送"你的请求已被响应"（玩家本人作答或说书人代填）。</summary>
    OperationRequestAnswered,

    /// <summary>向全部已绑定席位的玩家广播阶段开始（公开信息：昼夜）。</summary>
    PhaseStarted,
}
