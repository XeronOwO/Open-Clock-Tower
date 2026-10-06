/**
 * 账号会话（D-0021 / D-0027）：**一个浏览器一份，三个面共用**。
 *
 * 此前玩家面与说书人面各自 `new AccountGateway()`，于是同一个浏览器里点一下顶栏换面就要**再登一次**；
 * 而"是谁"在服务端本来就只有一个答案（账号会话）。这里把它收成模块级单例：
 *
 * - 账号连接只有一条（`AccountGateway` 内部一条 `/hub/account` 连接）；
 * - 已登录资料是响应式的，任何面读到的是同一份（换面不重新登录、也不重新连）；
 * - 会话凭据仍只活在网关的内存里（D-0012 / D-0021：不落盘、不进 DOM、不进日志）——
 *   刷新页面即失效，需要重新登录。这正是 D-0027 之后"换设备回来"能成立的原因：
 *   桌记在**账号**名下，不再依赖本机存着的一串凭据。
 */
import { computed, ref, type ComputedRef } from 'vue'
import {
  AccountGateway,
  type AccountProfile,
  type LobbyCreateResult,
  type LobbyTable,
} from '@/services/accountGateway'

/** 唯一的账号网关（一条连接）。 */
const gateway = new AccountGateway()

const profileValue = ref<AccountProfile | null>(null)
const busyValue = ref(false)
const noticeValue = ref('')
const recoveryCodeValue = ref('')

/** 已登录资料；未登录为 null。 */
export const profile: ComputedRef<AccountProfile | null> = computed(() => profileValue.value)

/** 账号操作进行中（按钮禁用用）。 */
export const busy: ComputedRef<boolean> = computed(() => busyValue.value)

/** 最近一条账号操作的回执说明（可直接展示）。 */
export const notice: ComputedRef<string> = computed(() => noticeValue.value)

/** 一次性恢复码：注册 / 重置成功时出现一次，抄下即清（服务端只存哈希，丢了找不回）。 */
export const recoveryCode: ComputedRef<string> = computed(() => recoveryCodeValue.value)

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

function failureText(code: string, message: string): string {
  return message.length > 0 ? `账号操作未成功（${code}）：${message}` : `账号操作未成功（${code}）`
}

function describe(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
