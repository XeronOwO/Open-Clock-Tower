namespace OpenClockTower.Kernel;

/// <summary>规则层对「这次首次处决后有没有可用屠夫」的判定结论（R-0050）。</summary>
public enum ExtraNominationOutcome
{
    /// <summary>有可用屠夫：打开窗口并把窗口授予该席位（R-0050 第 1 条）。</summary>
    Available,

    /// <summary>没有可用屠夫：照常关闭白天（没有来源覆盖 / 屠夫不在局 / 已死亡 / 能力不生效）。</summary>
    Unavailable,

    /// <summary>判定不了：观测不齐或多个屠夫席位，显式拒绝、不猜（R-0050 第 1 条）。</summary>
    Indeterminate,
}
