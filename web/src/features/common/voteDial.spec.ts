import { describe, expect, it } from 'vitest'
import { dialAngle, dialPoint } from '@/features/common/voteDial'

describe('钟盘几何（纯呈现）', () => {
  it('席位按座次从 12 点起顺时针均分', () => {
    const seatNumbers = [1, 2, 3, 4]
    expect(dialAngle(seatNumbers, 1)).toBe(0)
    expect(dialAngle(seatNumbers, 2)).toBe(90)
    expect(dialAngle(seatNumbers, 3)).toBe(180)
    expect(dialAngle(seatNumbers, 4)).toBe(270)
  })

  it('5 席按 72° 均分', () => {
    const seatNumbers = [1, 2, 3, 4, 5]
    expect(dialAngle(seatNumbers, 3)).toBeCloseTo(144)
    expect(dialAngle(seatNumbers, 5)).toBeCloseTo(288)
  })

  it('未知席位 / 空座次给出 null：不画针、不猜', () => {
    expect(dialAngle([1, 2], 9)).toBeNull()
    expect(dialAngle([], 1)).toBeNull()
    expect(dialPoint([1, 2], 9, 10)).toBeNull()
  })

  it('1 号席在正上方（12 点方向）', () => {
    const point = dialPoint([1, 2, 3, 4], 1, 80)
    expect(point).not.toBeNull()
    expect(point!.x).toBeCloseTo(100)
    expect(point!.y).toBeCloseTo(20)
  })
})
