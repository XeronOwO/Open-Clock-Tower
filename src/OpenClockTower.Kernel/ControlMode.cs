namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤机的控制模式：自动化默认执行，但**不具强制力**（D-0014）。
/// </summary>
public enum ControlMode
{
    /// <summary>自动：节拍器按配额推进；说书人仍可随时强推 / 作废 / 代填 / 接管。</summary>
    Automatic,

    /// <summary>说书人接管：自动推进暂停，由说书人手动逐步驱动；可交还自动化。</summary>
    StorytellerTakeover,
}
