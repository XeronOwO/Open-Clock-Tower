import { describe, expect, it } from 'vitest'
import { parseStorytellerTicket } from '@/services/storytellerGateway'

/**
 * 说书人票据的解析（多桌，D-0024 / D-0025）。
 *
 * 多桌之后"一桌一份票据"，而票据本身长得一样（都是 `storyteller-…`）：
 * 只说一串票据，面板不知道该连哪一桌。所以开桌时给出的是**自描述**写法 `桌标识:票据`。
 * 这里锁住它的边界——尤其是"不能把看起来像桌标识的东西误当成桌"。
 */
describe('说书人票据解析', () => {
  it('自描述写法：桌标识 + 票据', () => {
    expect(parseStorytellerTicket('9cjj6e74:storyteller-abc123')).toEqual({
      gameId: '9cjj6e74',
      ticket: 'storyteller-abc123',
    })
  })

  it('老写法（只有票据）仍然可用，落在默认桌', () => {
    expect(parseStorytellerTicket('storyteller-abc123')).toEqual({ ticket: 'storyteller-abc123' })
  })

  it('两侧空白被裁掉', () => {
    expect(parseStorytellerTicket('  9cjj6e74 : storyteller-abc123  ')).toEqual({
      gameId: '9cjj6e74',
      ticket: 'storyteller-abc123',
    })
  })

  it('冒号在开头 / 缺一半，都退回"整串当票据"，不猜', () => {
    expect(parseStorytellerTicket(':storyteller-abc')).toEqual({ ticket: ':storyteller-abc' })
    expect(parseStorytellerTicket('9cjj6e74:')).toEqual({ ticket: '9cjj6e74:' })
  })

  it('票据里本来就有冒号时，只按第一个冒号切（右边的原样保留）', () => {
    expect(parseStorytellerTicket('table:story:teller')).toEqual({
      gameId: 'table',
      ticket: 'story:teller',
    })
  })

  it('空串不炸', () => {
    expect(parseStorytellerTicket('   ')).toEqual({ ticket: '' })
  })
})

/**
 * 桌标识必须**真的**传到连接工厂。
 *
 * 实测踩到过一个很隐蔽的写法：构造函数第三个参数是 gameId，而默认工厂写成
 * `() => createStorytellerConnection()`（无参箭头）——它把 gameId 吞掉，连接永远落在默认桌，
 * 表现却是"新桌的票据被判无效"（因为票据属于另一桌）。服务端直连同一串票据是成功的，
 * 所以这种 bug 极难从表象定位，必须由用例钉住。
 */
describe('桌标识传递', () => {
  it('默认工厂直接引用带参函数（不能被无参箭头吞掉）', async () => {
    const { StorytellerGateway } = await import('@/services/storytellerGateway')
    const seen: Array<string | undefined> = []
    const fake = {
      state: 'Disconnected',
      on: () => {},
      onreconnecting: () => {},
      onreconnected: () => {},
      onclose: () => {},
    }

    const gateway = new StorytellerGateway(
      { onView: () => {}, onState: () => {}, onDiagnostic: () => {} },
      (gameId?: string) => {
        seen.push(gameId)
        return fake as never
      },
      'table-x',
    )

    expect(gateway).toBeDefined()
    expect(seen).toEqual(['table-x'])
  })
})
