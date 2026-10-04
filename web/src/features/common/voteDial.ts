/**
 * 钟盘几何（纯呈现，R-0017 目标形态）：席位 → 角度 / 坐标。
 *
 * 只按"席位在座次里的顺序"均分一圈，从 12 点方向起顺时针——这是布局约定，不是规则判断；
 * 未知席位返回 null（不画针、不猜），与 web/AGENTS §4 的防御性呈现一致。
 */
export const DIAL_CENTER = 100

/** 席位 → 角度（0° = 12 点方向，顺时针）；席位不在座次里时为 null。 */
export function dialAngle(seatNumbers: readonly number[], seat: number): number | null {
  const index = seatNumbers.indexOf(seat)
  if (index < 0 || seatNumbers.length === 0) {
    return null
  }

  return (index / seatNumbers.length) * 360
}

/** 席位 → 圆上坐标；席位不在座次里时为 null。 */
export function dialPoint(
  seatNumbers: readonly number[],
  seat: number,
  radius: number,
): { x: number; y: number } | null {
  const angle = dialAngle(seatNumbers, seat)
  if (angle === null) {
    return null
  }

  const radians = ((angle - 90) * Math.PI) / 180
  return { x: DIAL_CENTER + radius * Math.cos(radians), y: DIAL_CENTER + radius * Math.sin(radians) }
}
