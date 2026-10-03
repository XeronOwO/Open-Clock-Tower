using System.Globalization;

namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人注记的标识：一局内稳定、单调签发，**永不复用**（含已删除的注记）。
/// </summary>
/// <remarks>
/// 依据 D-0019：注记进事件流，改 / 删都按这个标识定位；复用标识会让持有旧 id 的客户端
/// 误改到一条新注记，所以签发序号只增不减（见 <see cref="SeatAnnotationLedger.LastIssuedId"/>）。
/// </remarks>
/// <param name="Value">签发序号（从 1 开始）。</param>
public readonly record struct SeatAnnotationId(int Value)
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
