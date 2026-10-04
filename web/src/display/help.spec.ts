import { describe, expect, it } from 'vitest'
import { HELP_TOPICS, type HelpTopicId } from '@/display/help'

const TOPIC_IDS = Object.keys(HELP_TOPICS) as HelpTopicId[]

describe('界面说明文案登记表', () => {
  it('每条都有标题与非空说明，且长度在可读范围内', () => {
    for (const id of TOPIC_IDS) {
      const entry = HELP_TOPICS[id]
      expect(entry.title.length, `${id} 标题不能为空`).toBeGreaterThan(0)
      expect(entry.title.length, `${id} 标题过长`).toBeLessThanOrEqual(10)
      expect(entry.text.trim().length, `${id} 说明不能为空`).toBeGreaterThan(7)
      expect(entry.text.length, `${id} 说明过长`).toBeLessThanOrEqual(200)
    }
  })

  it('说明里不带 HTML 与控制字符（纯文本渲染）', () => {
    for (const id of TOPIC_IDS) {
      const text = HELP_TOPICS[id].text
      expect(/[<>]/.test(text), `${id} 不应包含尖括号`).toBe(false)
      expect(
        [...text].some((character) => character.codePointAt(0)! < 0x20),
        `${id} 不应包含控制字符`,
      ).toBe(false)
    }
  })

  it('规则 / 口径类条目指回来源（裁定号或决策号）', () => {
    expect(HELP_TOPICS.madness.text).toContain('R-0003')
    expect(HELP_TOPICS.blocked.text).toContain('R-0009')
    expect(HELP_TOPICS['player-name'].text).toContain('D-0021')
    expect(HELP_TOPICS.information.text).toContain('D-0002')
    expect(HELP_TOPICS['status-ledger'].text).toContain('D-0015')
  })

  it('关键术语用登记表口径（不引入新词）', () => {
    expect(HELP_TOPICS.phase.title).toBe('阶段')
    expect(HELP_TOPICS.seat.text).toContain('席位')
    expect(HELP_TOPICS.execution.text).toContain('处决')
    expect(HELP_TOPICS.execution.text).toContain('死亡')
    expect(HELP_TOPICS.control.text).toContain('说书人接管')
  })

  it('条目 id 与内容一一对应、没有重复文案', () => {
    const texts = TOPIC_IDS.map((id) => HELP_TOPICS[id].text)
    expect(new Set(texts).size).toBe(texts.length)
  })
})
