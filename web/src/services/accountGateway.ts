/**
 * 账号端连接（D-0021）：注册 / 登录 / 登出 / 改玩家名 / 恢复码重置。
 *
 * 秘密纪律（D-0012 / D-0021 / D-0029）：账号会话凭据只存在**内存**与 `sessionStorage`
 * （`services/browserSession.ts`）两处——不进 DOM、不进日志；连接级凭据仍只在内存里。
 * 刷新页面由 `Resume` 向服务端确认后恢复，关标签页即清。
 * 连接目标是与游戏 Hub 分开的 `/hub/account`；凭据与游戏连接无关，只用于认领席位与账号自助。
 */
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import type { AccountDto } from '@/contracts/game'
import { ACCOUNT_HUB_PATH, type GatewayState } from '@/services/connectionState'

/** 已登录账号的展示资料；会话凭据是秘密，只随事件在内存里流转，不渲染。 */
export interface AccountProfile {
  id: number
  username: string
  displayName: string
  accountSession: string
  /** 这个账号现在能不能开桌（D-0026）。只用于决定给不给"开桌"入口，不是权限。 */
  canCreateTable: boolean
}

/** 大厅里的一桌（D-0025）：挑桌用的公开信息，不含票据与席位归属。 */
export interface LobbyTable {
  gameId: string
  name: string
  seatCapacity: number
  takenSeatCount: number
  started: boolean
  /** 是不是**邀请制桌**（D-0037）：邀请制桌不接受自助入座，必须凭邀请码；大厅里仍然列出来。 */
  inviteOnly: boolean
  /** 已被占用的席位号：界面据此把座位按钮置灰，玩家不必"点一下试试"。 */
  occupiedSeatNumbers: number[]
  /** 这张桌是不是**我**开的（D-0027）：服务端按会话算好，前端不自己拼事实。 */
  createdByMe: boolean
  /** 这张桌上**已经属于我**的席位号（D-0027）：刷新 / 换设备后"回到我的座位"靠它。 */
  mySeatNumbers: number[]
}

/**
 * 建桌结果（D-0027）：回执里**没有凭据**——开桌即成为这一桌的说书人，`gameId` 就够进主持台了。
 */
export interface LobbyCreateResult {
  ok: boolean
  code: string
  message: string
  gameId: string
  seatCount: number
}

/** 未知载荷 → 大厅列表；识别不了的条目直接丢弃（宁可少显示，也不显示坏数据）。 */
export function normalizeLobbyTables(raw: unknown): LobbyTable[] {
  if (!Array.isArray(raw)) {
    return []
  }

  const tables: LobbyTable[] = []
  for (const item of raw) {
    if (item === null || typeof item !== 'object') {
      continue
    }

    const value = item as Record<string, unknown>
    const gameId = value['gameId']
    if (typeof gameId !== 'string' || gameId.length === 0) {
      continue
    }

    const number = (key: string): number => (typeof value[key] === 'number' ? (value[key] as number) : 0)
    const flag = (key: string): boolean => value[key] === true
    const seatNumbers = (key: string): number[] => {
      const raw = value[key]
      return Array.isArray(raw)
        ? raw.filter((seat): seat is number => typeof seat === 'number' && Number.isInteger(seat))
        : []
    }

    tables.push({
      gameId,
      name: typeof value['name'] === 'string' ? value['name'] : '',
      seatCapacity: number('seatCapacity'),
      takenSeatCount: number('takenSeatCount'),
      started: flag('started'),
      inviteOnly: flag('inviteOnly'),
      occupiedSeatNumbers: seatNumbers('occupiedSeatNumbers'),
      createdByMe: flag('createdByMe'),
      mySeatNumbers: seatNumbers('mySeatNumbers'),
    })
  }

  return tables
}

/** 连接工厂：测试注入假连接以验证接线与顺序（不改变任何线上行为）。 */
export type AccountConnectionFactory = () => HubConnection

function createAccountConnection(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(ACCOUNT_HUB_PATH)
    .withAutomaticReconnect([0, 1000, 3000, 5000])
    .configureLogging(LogLevel.Warning)
    .build()
}

/** 未知载荷 → 账号结果；缺 ok / code 时返回 null（表达不了就不采纳，宁可报错）。 */
export function normalizeAccount(raw: unknown): AccountDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const value = raw as Record<string, unknown>
  const ok = value['ok']
  const code = value['code']
  if (typeof ok !== 'boolean' || typeof code !== 'string' || code.length === 0) {
    return null
  }

  const id = value['id']
  return {
    ok,
    code,
    message: typeof value['message'] === 'string' ? value['message'] : '',
    id: typeof id === 'number' && Number.isInteger(id) ? id : 0,
    username: typeof value['username'] === 'string' ? value['username'] : '',
    displayName: typeof value['displayName'] === 'string' ? value['displayName'] : '',
    accountSession:
      typeof value['accountSession'] === 'string' && value['accountSession'].length > 0
        ? value['accountSession']
        : null,
    recoveryCode:
      typeof value['recoveryCode'] === 'string' && value['recoveryCode'].length > 0
        ? value['recoveryCode']
        : null,
    canCreateTable: value['canCreateTable'] === true,
  }
}

/** 账号网关：一个实例一条账号连接；未登录时 `profile` 为 null。 */
export class AccountGateway {
  private readonly connection: HubConnection
  private profileValue: AccountProfile | null = null

  constructor(
    private readonly onState: (state: GatewayState) => void = () => {},
    createConnection: AccountConnectionFactory = createAccountConnection,
  ) {
    this.connection = createConnection()
    this.connection.onreconnecting(() => this.onState('reconnecting'))
    this.connection.onreconnected(() => this.onState('connected'))
    this.connection.onclose(() => this.onState('disconnected'))
  }

  /** 已登录资料；未登录为 null。 */
  get profile(): AccountProfile | null {
    return this.profileValue
  }

  /** 注册（成功即登录）；失败结果是正常回执，不抛异常。 */
  async register(username: string, displayName: string, password: string): Promise<AccountDto> {
    const result = await this.invoke('Register', username, displayName, password)
    this.adopt(result)
    return result
  }

  /** 登录；失败结果同样走回执（不区分"登录名不存在 / 口令不对"）。 */
  async login(username: string, password: string): Promise<AccountDto> {
    const result = await this.invoke('Login', username, password)
    this.adopt(result)
    return result
  }

  /**
   * 用持久化的账号会话恢复登录态（M1 / D-0029）：刷新页面之后的第一件事。
   *
   * 回执**不带凭据**（恢复不是签发，服务端也不会重发），所以这里沿用本地那一份——
   * 它刚刚被服务端确认有效。无效时 `profileValue` 保持 null，由调用方清掉持久化并回登录卡。
   */
  async resume(accountSession: string): Promise<AccountDto> {
    const result = await this.invoke('Resume', accountSession)
    if (result.ok) {
      this.profileValue = {
        id: result.id,
        username: result.username,
        displayName: result.displayName,
        accountSession,
        canCreateTable: result.canCreateTable === true,
      }
    }

    return result
  }

  /** 登出：先请服务端撤销，再清本地；服务端不可达也清本地（不让旧会话留在界面上）。 */
  async logout(): Promise<void> {
    const session = this.profileValue?.accountSession
    try {
      if (session !== undefined) {
        await this.invoke('Logout', session)
      }
    } finally {
      this.profileValue = null
    }
  }

  /** 改玩家名：改名后服务端会同步给本局已绑定席位；这里同步更新本地资料。 */
  async changeDisplayName(displayName: string): Promise<AccountDto> {
    const session = this.requireSession()
    const result = await this.invoke('ChangeDisplayName', session, displayName)
    if (result.ok && this.profileValue !== null) {
      this.profileValue = { ...this.profileValue, displayName: result.displayName }
    }

    return result
  }

  /** 恢复码重置口令：成功后服务端撤销旧会话并签发新会话（已登录状态直接续上）。 */
  async resetPassword(
    username: string,
    recoveryCode: string,
    newPassword: string,
  ): Promise<AccountDto> {
    const result = await this.invoke('ResetPassword', username, recoveryCode, newPassword)
    this.adopt(result)
    return result
  }

  /**
   * 注销账号（M5 / G-A1-6）：**不可逆**，所以除会话外还要当事人重新给一次口令。
   *
   * 成功后本地立即清干净（资料）：服务端已经把这个账号与它的全部会话撤掉了，
   * 留着那份凭据只会在下一次刷新时拿一串废票去试（D-0029 的"撤销性不退步"）。
   */
  async deleteAccount(password: string): Promise<AccountDto> {
    const session = this.requireSession()
    const result = await this.invoke('DeleteAccount', session, password)
    if (result.ok) {
      this.profileValue = null
    }

    return result
  }

  /** 列出在开的桌（大厅）：未登录也能看；带会话时附带"这张桌是不是我开的"（D-0027）。 */
  async listTables(): Promise<LobbyTable[]> {
    return normalizeLobbyTables(
      await this.invokeRaw('ListTables', this.profileValue?.accountSession ?? null),
    )
  }

  /**
   * 开一张新桌（D-0026：登录即可；D-0027：开完这一桌就是你的，回执里没有凭据）。
   */
  async createTable(name: string, seatCount: number): Promise<LobbyCreateResult> {
    const session = this.requireSession()
    const raw = await this.invokeRaw('CreateTable', session, name, seatCount)
    const value = (raw ?? {}) as Record<string, unknown>
    return {
      ok: value['ok'] === true,
      code: typeof value['code'] === 'string' ? value['code'] : 'unknown',
      message: typeof value['message'] === 'string' ? value['message'] : '',
      gameId: typeof value['gameId'] === 'string' ? value['gameId'] : '',
      seatCount: typeof value['seatCount'] === 'number' ? value['seatCount'] : 0,
    }
  }

  /** 断开并清掉内存里的账号会话。 */
  async stop(): Promise<void> {
    this.profileValue = null
    if (this.connection.state !== HubConnectionState.Disconnected) {
      await this.connection.stop()
    }
  }

  /** 成功且带回会话时更新本地资料；失败结果不改变已登录状态。 */
  private adopt(result: AccountDto): void {
    if (!result.ok || result.accountSession === null) {
      return
    }

    this.profileValue = {
      id: result.id,
      username: result.username,
      displayName: result.displayName,
      accountSession: result.accountSession,
      canCreateTable: result.canCreateTable === true,
    }
  }

  private requireSession(): string {
    if (this.profileValue === null) {
      throw new Error('尚未登录：没有账号会话')
    }

    return this.profileValue.accountSession
  }

  /** 原始调用（大厅那两个方法返回的不是账号回执，不能走 `normalizeAccount`）。 */
  private async invokeRaw(method: string, ...args: unknown[]): Promise<unknown> {
    if (this.connection.state === HubConnectionState.Disconnected) {
      this.onState('connecting')
      await this.connection.start()
    }

    this.onState('connected')
    return await this.connection.invoke<unknown>(method, ...args)
  }

  private async invoke(method: string, ...args: unknown[]): Promise<AccountDto> {
    if (this.connection.state === HubConnectionState.Disconnected) {
      this.onState('connecting')
      await this.connection.start()
    }

    this.onState('connected')
    const result = normalizeAccount(await this.connection.invoke<unknown>(method, ...args))
    if (result === null) {
      throw new Error('账号回执不可识别')
    }

    return result
  }
}
