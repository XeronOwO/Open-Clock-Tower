/**
 * 角色图地址的单元回归（D-0007 / R-0006）。
 *
 * 关键约束：
 * - 首版 30 人花名册（25 非旅行者 + 5 旅行者）**每人都有图**（花名册新增角色时这里会红，逼着补映射）；
 * - 地址唯一、形状符合 MediaWiki 的 `/images/<1位>/<2位>/<文件>.png` 布局；
 * - 未知 / 空 slug 返回 null——不猜、不请求（与 labels.ts 同姿态）。
 */
import { describe, expect, it } from 'vitest'
import { characterArtUrlOf } from '@/display/character-art'
import { ROSTER } from '@/display/labels'

const WIKI_IMAGE_PATTERN =
  /^https:\/\/clocktower-wiki\.gstonegames\.com\/images\/[0-9a-f]\/[0-9a-f]{2}\/[A-Za-z]+\.png$/

describe('角色图地址（运行期热链百科）', () => {
  it('30 个角色全部有图，地址唯一且形状正确', () => {
    const urls = ROSTER.map((profile) => characterArtUrlOf(profile.slug))
    expect(urls.every((url) => url !== null)).toBe(true)
    expect(new Set(urls).size).toBe(ROSTER.length)
    for (const url of urls) {
      expect(url).toMatch(WIKI_IMAGE_PATTERN)
    }
  })

  it('未知 / 空 slug 不猜：返回 null', () => {
    expect(characterArtUrlOf('not-a-character')).toBeNull()
    expect(characterArtUrlOf(null)).toBeNull()
    expect(characterArtUrlOf(undefined)).toBeNull()
    expect(characterArtUrlOf('')).toBeNull()
  })
})
