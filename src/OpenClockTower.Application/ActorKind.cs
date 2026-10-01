namespace OpenClockTower.Application;

/// <summary>操作者的身份类别（身份闸的判定依据）。</summary>
/// <remarks>
/// 依据 D-0012：客户端声明的一切都不被信任——身份由服务端从连接与票据推导，
/// 这里这个值就是推导结果。
/// </remarks>
public enum ActorKind
{
    /// <summary>玩家：只能提交自己的响应。</summary>
    Player,

    /// <summary>说书人：作废 / 代填 / 强推 / 接管 / 裁定 / 座位变化 / 重建。</summary>
    Storyteller,

    /// <summary>系统：节拍器驱动的配额输入。</summary>
    System,

    /// <summary>宿主：开局、开阶段等运行时动作。</summary>
    Host,
}
