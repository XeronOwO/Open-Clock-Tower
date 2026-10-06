/**
 * 浏览器会话（M1 / D-0029）：刷新不掉登录的**承载层**。
 *
 * 只放两样东西：
 *   1. **账号会话凭据**——唯一必须持久化的秘密。账号是身份证（D-0025 / D-0027），
 *      把它关在内存里等于让身份证每次刷新作废；
 *   2. **位置**（我在哪一面 / 哪一桌 / 哪一席）——非秘密，用于刷新后回到原处。
 *
 * **连接级凭据（席位 / 说书人）不进这里**：它们本来就每次 Join 重新签发（D-0012），
 * 持久化没有收益，只会多一份可能陈旧的凭据。
 *
 * 存储用 `sessionStorage`：刷新 / 同标签页导航 / 后退前进都在，**关标签页即清**，不跨设备。
 * 存储不可用（隐私模式 / 被策略禁用）时降级为内存——登录本身不受影响，只是刷新要重新登录；
 * 降级是**显式**的（`persistent`），不假装存上了。
 *
 * 读取一律走防御性解析：这里的内容可以被用户手工改、也可以是上一个版本的形状，
 * 坏数据只当"没有"，绝不把半截凭据当有效凭据送出去。
 */
import { asCredential, asSeatNumber } from '@/display/format'

/** 存储键带版本号：形状变了就换键，旧数据自然作废（不做就地迁移）。 */
export const STORAGE_KEY = 'oct.session.v1'

/** 哪一面：玩家端还是主持台。 */
export type Surface = 'player' | 'storyteller'

/** "我刚才在哪"：非秘密。`seat` 只在玩家面有意义（主持台没有席位）。 */
export interface ActiveTable {
  surface: Surface
  gameId: string
  seat: number | null
}

/** 存下来的会话：凭据一件 + 位置一条。 */
export interface StoredSession {
  accountSession: string
  activeTable: ActiveTable | null
}

/**
 * 存储后端的最小面（`sessionStorage` 的形状）。
 *
 * 抽成接口是为了让"存储不可用"这件事**可以被测试**：真实浏览器里隐私模式才出现的失败路径，
 * 在装置与单测里必须能构造出来（否则降级逻辑永远没被跑过）。
 */
export interface StorageLike {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
  removeItem(key: string): void
}

/** 默认后端：没有 `window`（非浏览器环境）就是 null = 纯内存。 */
export function defaultStorage(): StorageLike | null {
  if (typeof window === 'undefined') {
    return null
  }

  try {
    return window.sessionStorage
  } catch {
    // 某些浏览器在禁用存储时连读属性都抛。
    return null
  }
}

/** 存储是否真的能写：构造时探一次，之后每次读写失败也会翻。 */
function probe(storage: StorageLike): boolean {
  try {
    const key = `${STORAGE_KEY}.probe`
    storage.setItem(key, '1')
    storage.removeItem(key)
    return true
  } catch {
    return false
  }
}

/** 浏览器会话的读写器。一个实例对应一个后端；模块级单例在 `services/accountSession.ts`。 */
export class BrowserSessionStore {
  private memory: string | null = null
  private available: boolean

  constructor(private readonly storage: StorageLike | null) {
    this.available = storage !== null && probe(storage)
  }

  /** 存储后端是否真的在干活（false = 已降级为内存，刷新会掉登录）。 */
  get persistent(): boolean {
    return this.available
  }

  /** 读一条会话；没有 / 坏了 / 凭据不合格都返回 null（"没有"和"坏了"对调用方是同一件事）。 */
  read(): StoredSession | null {
    const raw = this.readRaw()
    return raw === null ? null : parseStoredSession(raw)
  }

  write(value: StoredSession): void {
    this.writeRaw(JSON.stringify(value))
  }

  /** 清掉整条记录（登出 / 凭据被撤销时）。 */
  clear(): void {
    this.writeRaw(null)
  }

  readActiveTable(): ActiveTable | null {
    return this.read()?.activeTable ?? null
  }

  /** 只更新位置、保留凭据：两者同一条记录，分开写会丢掉另一半。 */
  writeActiveTable(table: ActiveTable): void {
    const current = this.read()
    if (current === null) {
      // 没登录就没有"我的位置"：位置属于账号，不属于这台浏览器。
      return
    }

    this.write({ accountSession: current.accountSession, activeTable: table })
  }

  clearActiveTable(): void {
    const current = this.read()
    if (current === null) {
      return
    }

    this.write({ accountSession: current.accountSession, activeTable: null })
  }

  private readRaw(): string | null {
    if (this.storage === null || !this.available) {
      return this.memory
    }

    try {
      return this.storage.getItem(STORAGE_KEY)
    } catch {
      // 读失败 = 这个后端不能信：退回内存里的影子副本，不把异常抛给界面。
      this.available = false
      return this.memory
    }
  }

  private writeRaw(value: string | null): void {
    // 内存里始终留一份影子：存储坏掉之后界面仍能正常工作（只是刷新会掉登录）。
    this.memory = value
    if (this.storage === null || !this.available) {
      return
    }

    try {
      if (value === null) {
        this.storage.removeItem(STORAGE_KEY)
      } else {
        this.storage.setItem(STORAGE_KEY, value)
      }
    } catch {
      this.available = false
    }
  }
}

/** 坏数据只当"没有"：形状不对、凭据不合格、位置不完整，一律整条作废。 */
export function parseStoredSession(raw: string): StoredSession | null {
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return null
  }

  if (parsed === null || typeof parsed !== 'object') {
    return null
  }

  const value = parsed as Record<string, unknown>
  const accountSession = asCredential(value['accountSession'])
  if (accountSession === null) {
    return null
  }

  return { accountSession, activeTable: parseActiveTable(value['activeTable']) }
}

/** 位置解析：面名与桌标识必须合法；玩家面还必须有席位号，否则整条位置丢弃。 */
export function parseActiveTable(raw: unknown): ActiveTable | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const value = raw as Record<string, unknown>
  const surface = value['surface']
  const gameId = value['gameId']
  if (surface !== 'player' && surface !== 'storyteller') {
    return null
  }

  if (typeof gameId !== 'string' || gameId.length === 0 || gameId.length > 128) {
    return null
  }

  if (surface === 'storyteller') {
    return { surface, gameId, seat: null }
  }

  const seat = asSeatNumber(value['seat'], 1000)
  return seat === null ? null : { surface, gameId, seat }
}
