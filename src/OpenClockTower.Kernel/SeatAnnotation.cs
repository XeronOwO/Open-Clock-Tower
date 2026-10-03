namespace OpenClockTower.Kernel;

/// <summary>
/// 一条说书人注记：挂在某个席位上的自由文本提示标记（D-0019）。
/// </summary>
/// <remarks>
/// 它不是游戏状态事实：不参与任何规则判定，不进 <see cref="GameState"/>，也不进玩家投影。
/// 文本已在写入侧归一化（折叠空白、长度上限），折叠层不再改写内容。
/// </remarks>
/// <param name="Id">签发标识；改 / 删按它定位。</param>
/// <param name="Seat">挂在哪一席。</param>
/// <param name="Text">自由文本（已归一化）。</param>
public sealed record SeatAnnotation(SeatAnnotationId Id, SeatId Seat, string Text);
