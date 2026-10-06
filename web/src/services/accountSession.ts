/**
 * 账号会话（D-0021 / D-0027 / D-0029）：**一个浏览器一份，三个面共用**。
 *
 * 此前玩家面与说书人面各自 `new AccountGateway()`，于是同一个浏览器里点一下顶栏换面就要**再登一次**；
 * 而"是谁"在服务端本来就只有一个答案（账号会话）。这里把它收成模块级单例：
 *
 * - 账号连接只有一条（`AccountGateway` 内部一条 `/hub/account` 连接）；
 * - 已登录资料是响应式的，任何面读到的是同一份（换面不重新登录、也不重新连）；
 * - **凭据持久化到 `sessionStorage`**（M1 / D-0029）：刷新 / 同标签页导航 / 后退前进都还在，
 *   关标签页即清、不跨设备。连接级凭据仍然只在网关内存里（D-0012 不变），
 *   所以刷新之后是"用账号回到原处"，不是"带着一串旧凭据硬闯"。
 *
 * 启动顺序（`main.ts`）：**先挂载、再 `restore()`**——服务端不可达时既不白屏，
 * 也不会把用户误判成"已登出"（恢复失败不清持久化）。
 */
import { computed, ref, type ComputedRef } from 'vue'
import {
  AccountGateway,
  type AccountProfile,
  type LobbyCreateResult,
  type LobbyTable,
} from '@/services/accountGateway'
import { BrowserSessionStore, defaultStorage, type ActiveTable } from '@/services/browserSession'

/** 唯一的账号网关（一条连接）。 */
const gateway = new AccountGateway()

/** 浏览器会话（`sessionStorage`）：账号凭据 + 位置。存储不可用时自动降级为内存。 */
const store = new BrowserSessionStore(defaultStorage())

const profileValue = ref<AccountProfile | null>(null)
const busyValue = ref(false)
const noticeValue = ref('')
const recoveryCodeValue = ref('')
const restoringValue = ref(true)
const activeTableValue = ref<ActiveTable | null>(null)

/** 已登录资料；未登录为 null。 */
export const profile: ComputedRef<AccountProfile | null> = computed(() => profileValue.value)

/** 账号操作进行中（按钮禁用用）。 */
export const busy: ComputedRef<boolean> = computed(() => busyValue.value)

/** 最近一条账号操作的回执说明（可直接展示）。 */
export const notice: ComputedRef<string> = computed(() => noticeValue.value)

/** 一次性恢复码：注册 / 重置成功时出现一次，抄下即清（服务端只存哈希，丢了找不回）。 */
export const recoveryCode: ComputedRef<string> = computed(() => recoveryCodeValue.value)

/**
 * 正在向服务端确认持久化凭据（启动后的一小段）。
 *
 * 界面必须把它和"未登录"分开：否则每次刷新都会先闪一张登录卡，再跳回原来的面。
 */
export const restoring: ComputedRef<boolean> = computed(() => restoringValue.value)

/** 我上一次在哪一桌哪一席（M1）：首页据此给"回到我那一桌"，两端据此自动回原处。 */
export const activeTable: ComputedRef<ActiveTable | null> = computed(() => activeTableValue.value)

/** 浏览器会话是否真的在干活（false = 隐私模式之类的降级：刷新会掉登录）。 */
export const sessionPersisted: ComputedRef<boolean> = computed(() => store.persistent)

/**
 * 用持久化凭据恢复登录态（M1 / D-0029）：启动时调用一次。
 *
 * 三种结果都要有交代：
 * - 没有存过 → 静默留在登录卡（这不是失败）；
 * - 存过但服务端说无效（登出 / 改口令 / 过期）→ **清掉持久化那份**并说明，撤销性不退步；
 * - 服务端不可达 → 保留持久化（凭据可能仍然有效），只说明这次没恢复上。
 */
export async function restore(): Promise<void> {
  restoringValue.value = true
  try {
    const stored = store.read()
    if (stored === null) {
      return
    }

    const result = await gateway.resume(stored.accountSession)
    if (!result.ok) {
      store.clear()
      activeTableValue.value = null
      noticeValue.value = '上一次的登录已经失效，请重新登录'
      return
    }

    profileValue.value = gateway.profile
    activeTableValue.value = stored.activeTable
  } catch (error) {
    noticeValue.value = `恢复登录状态失败：${describe(error)}`
  } finally {
    restoringValue.value = false
  }
}

/** 记住"我在哪一桌哪一席"：刷新后按它回到原处。未登录时忽略（位置属于账号，不属于浏览器）。 */
export function rememberTable(table: ActiveTable): void {
  if (profileValue.value === null) {
    return
  }

  activeTableValue.value = table
  store.writeActiveTable(table)
}

/** 忘记位置（离开了这一桌 / 那个席位已经不在了）。凭据不动：位置与登录是两件事。 */
export function forgetTable(): void {
  activeTableValue.value = null
  store.clearActiveTable()
}

/** 注册（成功即登录）。返回是否成功。 */
export async function register(username: string, displayName: string, password: string): Promise<boolean> {
  busyValue.value = true
  recoveryCodeValue.value = ''
  try {
    const result = await gateway.register(username, displayName, password)
    if (!result.ok) {
      noticeValue.value = failureText(result.code, result.message)
      return false
    }

    profileValue.value = gateway.profile
    recoveryCodeValue.value = result.recoveryCode ?? ''
    noticeValue.value = ''
    persist()
    return true
  } catch (error) {
    noticeValue.value = `注册失败：${describe(error)}`
    return false
  } finally {
    busyValue.value = false
  }
}

/** 登录。返回是否成功。 */
export async function login(username: string, password: string): Promise<boolean> {
  busyValue.value = true
  recoveryCodeValue.value = ''
  try {
    const result = await gateway.login(username, password)
    if (!result.ok) {
      noticeValue.value = failureText(result.code, result.message)
      return false
    }

    profileValue.value = gateway.profile
    noticeValue.value = ''
    persist()
    return true
  } catch (error) {
    noticeValue.value = `登录失败：${describe(error)}`
    return false
  } finally {
    busyValue.value = false
  }
}

/** 登出：服务端撤销后清本地（服务端不可达也清，不把旧会话留在界面上）。 */
export async function logout(): Promise<void> {
  busyValue.value = true
  try {
    await gateway.logout()
  } catch (error) {
    noticeValue.value = `登出时服务端不可达，已在本机退出：${describe(error)}`
  } finally {
    profileValue.value = null
    recoveryCodeValue.value = ''
    // 位置跟着凭据一起清：换个人登录不该回到上一个人的桌。
    store.clear()
    activeTableValue.value = null
    busyValue.value = false
  }
}

/** 改玩家名（同桌所有人都会看到新名字）。返回是否成功。 */
export async function rename(displayName: string): Promise<boolean> {
  busyValue.value = true
  try {
    const result = await gateway.changeDisplayName(displayName)
    if (!result.ok) {
      noticeValue.value = failureText(result.code, result.message)
      return false
    }

    profileValue.value = gateway.profile
    noticeValue.value = '玩家名已更新'
    return true
  } catch (error) {
    noticeValue.value = `改名失败：${describe(error)}`
    return false
  } finally {
    busyValue.value = false
  }
}

/** 用一次性恢复码重置口令（成功即重新登录）。返回是否成功。 */
export async function resetPassword(
  username: string,
  code: string,
  newPassword: string,
): Promise<boolean> {
  busyValue.value = true
  try {
    const result = await gateway.resetPassword(username, code, newPassword)
    if (!result.ok) {
      noticeValue.value = failureText(result.code, result.message)
      return false
    }

    profileValue.value = gateway.profile
    recoveryCodeValue.value = result.recoveryCode ?? ''
    noticeValue.value = ''
    // 旧会话已被服务端作废（RevokeAllForAccount）：持久化那份必须换成新的，
    // 否则下一次刷新会拿着作废凭据去试——那正是"撤销性"要挡住的事。
    persist()
    return true
  } catch (error) {
    noticeValue.value = `重置口令失败：${describe(error)}`
    return false
  } finally {
    busyValue.value = false
  }
}

/** 大厅桌列表（公开信息；带"是不是我开的"）。 */
export async function listTables(): Promise<LobbyTable[]> {
  return await gateway.listTables()
}

/** 开一桌（登录即可；开完这一桌就是你的）。 */
export async function createTable(name: string, seatCount: number): Promise<LobbyCreateResult> {
  busyValue.value = true
  try {
    return await gateway.createTable(name, seatCount)
  } finally {
    busyValue.value = false
  }
}

/** 清掉当前提示（换表单 / 换页时用，避免旧提示串台）。 */
export function clearNotice(): void {
  noticeValue.value = ''
}

/** 抄下恢复码：确认后从界面上清掉（它只该出现这一次）。 */
export function dismissRecoveryCode(): void {
  recoveryCodeValue.value = ''
}

/**
 * 把当前凭据写进浏览器会话。
 *
 * 存储写不进去（隐私模式）时**如实说**：能登录、但刷新要重新登录——
 * 宁可让人看见这句，也不假装"记住了"然后在刷新后把人扔回登录卡。
 */
function persist(): void {
  const current = profileValue.value
  if (current === null) {
    return
  }

  store.write({ accountSession: current.accountSession, activeTable: activeTableValue.value })
  if (!store.persistent) {
    noticeValue.value = '这台浏览器不允许保存登录状态：刷新页面后需要重新登录'
  }
}

function failureText(code: string, message: string): string {
  return message.length > 0 ? `账号操作未成功（${code}）：${message}` : `账号操作未成功（${code}）`
}

function describe(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
