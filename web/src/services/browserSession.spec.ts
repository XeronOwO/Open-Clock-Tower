import { describe, expect, it } from 'vitest'
import {
  BrowserSessionStore,
  parseActiveTable,
  parseStoredSession,
  STORAGE_KEY,
  type StorageLike,
} from '@/services/browserSession'

/**
 * 浏览器会话（M1 / D-0029）：只有两种东西会被持久化——账号会话凭据与"我在哪一桌哪一席"。
 *
 * 这里要钉住的四件事：
 *   1. 凭据与位置**同存一条**：只改其中一个不许把另一个写丢；
 *   2. 坏数据只当"没有"（用户能手改 `sessionStorage`，旧版本残留也会出现）；
 *   3. 存储不可用时降级为内存，并且**如实标注**（`persistent === false`），不假装记住了；
 *   4. 连接级凭据（席位 / 说书人）**没有**进这里的入口——形状上就不存在这个字段。
 */

/** 合法凭据的形状与服务端一致：43 个 base64url 字符（`asCredential` 要求 ≥16）。 */
const CREDENTIAL = 'C'.repeat(43)

/** 假存储：既能当正常后端，也能在读写上出错（隐私模式 / 被策略禁用的形状）。 */
class FakeStorage implements StorageLike {
  readonly values = new Map<string, string>()
  failOnRead = false
  failOnWrite = false

  getItem(key: string): string | null {
    if (this.failOnRead) {
      throw new Error('读取被拒绝')
    }

    return this.values.get(key) ?? null
  }

  setItem(key: string, value: string): void {
    if (this.failOnWrite) {
      throw new Error('写入被拒绝')
    }

    this.values.set(key, value)
  }

  removeItem(key: string): void {
    if (this.failOnWrite) {
      throw new Error('删除被拒绝')
    }

    this.values.delete(key)
  }

  /** 直接塞原始字符串：模拟用户手改过的存储内容。 */
  raw(value: string): void {
    this.values.set(STORAGE_KEY, value)
  }
}

function storeWith(fake: FakeStorage): BrowserSessionStore {
  return new BrowserSessionStore(fake)
}

describe('浏览器会话存储', () => {
  it('写进去能原样读回来（凭据 + 位置）', () => {
    const fake = new FakeStorage()
    const store = storeWith(fake)

    store.write({ accountSession: CREDENTIAL, activeTable: { surface: 'player', gameId: 't1', seat: 3 } })

    expect(store.persistent).toBe(true)
    expect(store.read()).toEqual({
      accountSession: CREDENTIAL,
      activeTable: { surface: 'player', gameId: 't1', seat: 3 },
    })
  })

  it('没写过 / 清掉之后都读成"没有"', () => {
    const store = storeWith(new FakeStorage())
    expect(store.read()).toBeNull()

    store.write({ accountSession: CREDENTIAL, activeTable: null })
    store.clear()
    expect(store.read()).toBeNull()
  })

  it('只改位置不会把凭据写丢，反之亦然', () => {
    const store = storeWith(new FakeStorage())
    store.write({ accountSession: CREDENTIAL, activeTable: null })

    store.writeActiveTable({ surface: 'storyteller', gameId: 't2', seat: null })
    expect(store.read()).toEqual({
      accountSession: CREDENTIAL,
      activeTable: { surface: 'storyteller', gameId: 't2', seat: null },
    })

    store.clearActiveTable()
    expect(store.read()).toEqual({ accountSession: CREDENTIAL, activeTable: null })
  })

  it('没登录时写位置是空操作：位置属于账号，不属于这台浏览器', () => {
    const fake = new FakeStorage()
    const store = storeWith(fake)

    store.writeActiveTable({ surface: 'player', gameId: 't1', seat: 1 })

    expect(store.read()).toBeNull()
    expect(fake.values.size).toBe(0)
  })

  it('存储写入失败 → 降级为内存（同一会话内仍可读），并如实标注 persistent=false', () => {
    const fake = new FakeStorage()
    const store = storeWith(fake)
    fake.failOnWrite = true

    store.write({ accountSession: CREDENTIAL, activeTable: null })

    expect(store.persistent).toBe(false)
    expect(store.read()?.accountSession).toBe(CREDENTIAL)
  })

  it('存储读取失败 → 同样降级，绝不把异常抛给界面', () => {
    const fake = new FakeStorage()
    const store = storeWith(fake)
    store.write({ accountSession: CREDENTIAL, activeTable: null })
    fake.failOnRead = true

    expect(store.read()?.accountSession).toBe(CREDENTIAL)
    expect(store.persistent).toBe(false)
  })

  it('构造时就写不进去（隐私模式的形状）→ 直接标记不可持久化', () => {
    const fake = new FakeStorage()
    fake.failOnWrite = true

    const store = storeWith(fake)

    expect(store.persistent).toBe(false)
    expect(store.read()).toBeNull()
  })

  it('没有后端（非浏览器环境）→ 纯内存，不报错', () => {
    const store = new BrowserSessionStore(null)

    expect(store.persistent).toBe(false)
    store.write({ accountSession: CREDENTIAL, activeTable: { surface: 'player', gameId: 't1', seat: 2 } })
    expect(store.read()?.activeTable?.seat).toBe(2)
  })
})

describe('浏览器会话解析（存储内容是不可信输入）', () => {
  it('坏 JSON / 不是对象 / 缺凭据 / 凭据不合格 → 整条当"没有"', () => {
    expect(parseStoredSession('{')).toBeNull()
    expect(parseStoredSession('"字符串"')).toBeNull()
    expect(parseStoredSession('{}')).toBeNull()
    expect(parseStoredSession(JSON.stringify({ accountSession: '' }))).toBeNull()
    expect(parseStoredSession(JSON.stringify({ accountSession: 'short' }))).toBeNull()
    expect(parseStoredSession(JSON.stringify({ accountSession: 42 }))).toBeNull()
  })

  it('凭据合格但位置坏了 → 只丢位置，登录态仍然可用', () => {
    const broken = [
      { surface: 'player', gameId: 't1' },
      { surface: 'player', gameId: 't1', seat: 0 },
      { surface: 'player', gameId: '', seat: 1 },
      { surface: 'lobby', gameId: 't1', seat: 1 },
      { surface: 'player', gameId: 't1', seat: '1' },
      'not-an-object',
    ]

    for (const activeTable of broken) {
      const parsed = parseStoredSession(JSON.stringify({ accountSession: CREDENTIAL, activeTable }))
      expect(parsed).toEqual({ accountSession: CREDENTIAL, activeTable: null })
    }
  })

  it('位置解析：主持台没有席位，玩家面必须有席位', () => {
    expect(parseActiveTable({ surface: 'storyteller', gameId: 't1', seat: 9 })).toEqual({
      surface: 'storyteller',
      gameId: 't1',
      seat: null,
    })
    expect(parseActiveTable({ surface: 'player', gameId: 't1', seat: 9 })).toEqual({
      surface: 'player',
      gameId: 't1',
      seat: 9,
    })
    expect(parseActiveTable({ surface: 'player', gameId: 't1', seat: null })).toBeNull()
  })

  it('持久化里不存在"连接级凭据"这一项：席位 / 说书人凭据只能活在网关内存里', () => {
    const store = storeWith(new FakeStorage())
    store.write({ accountSession: CREDENTIAL, activeTable: { surface: 'player', gameId: 't1', seat: 1 } })

    // 多塞的字段不会被读出来（形状由类型与解析共同钉死）。
    const parsed = parseStoredSession(
      JSON.stringify({
        accountSession: CREDENTIAL,
        activeTable: null,
        credential: 'seat-credential',
      }),
    )

    expect(parsed).toEqual({ accountSession: CREDENTIAL, activeTable: null })
    expect(Object.keys(parsed!)).toEqual(['accountSession', 'activeTable'])
  })
})
