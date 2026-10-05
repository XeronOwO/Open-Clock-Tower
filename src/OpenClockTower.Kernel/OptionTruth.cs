namespace OpenClockTower.Kernel;

/// <summary>
/// 一个候选选项的**真值**：说书人把它当"一条信息"给出去时，这句话此刻实际是对是错。
/// </summary>
/// <remarks>
/// <para>
/// 信息类裁定点（博学者 R-0057）此前只有自由文本，平台不判定真假（D-0002）；本类型不推翻那个姿态
/// ——真值由**候选事实库自己按状态账求值**（规则层，可复现、可审计），平台仍然不替说书人挑内容，
/// 只是把他要挑的东西连同真值摆在他面前，并在提交时核对组合。
/// </para>
/// <para>
/// "判不了"不进候选，因此这里只有两种取值（D-0015：不猜）。
/// </para>
/// </remarks>
public enum OptionTruth
{
    /// <summary>这句话此刻为真。</summary>
    True,

    /// <summary>这句话此刻为假。</summary>
    False,
}
