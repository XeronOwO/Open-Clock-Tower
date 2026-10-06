namespace OpenClockTower.Kernel;

/// <summary>
/// 艺术家提问文本的口径（R-0040）：长度上限**只在这里声明一处**。
/// </summary>
/// <remarks>
/// 与 <see cref="SeatAnnotationText"/> 同款：客户端送来的文本是不可信输入（D-0012），
/// 上限由内核与命令闸共用同一把尺子——两处各写一个 200，改一处就会静默漂移。
/// </remarks>
public static class ArtistQuestionText
{
    /// <summary>提问文本的字符上限：防止把整段对话塞进事件流（超限显式拒绝）。</summary>
    public const int MaxLength = 200;
}
