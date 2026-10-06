import { HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { describe, expect, it } from 'vitest'
import { AccountGateway, normalizeAccount } from '@/services/accountGateway'

/**
 * 账号网关（D-0021）：注册 / 登录 / 登出 / 改玩家名 / 恢复码重置的接线，
 * 以及"账号会话只存内存、失败不改状态"的秘密纪律。
 */

/** 假连接：只实现账号网关用到的那几个成员（`as unknown as HubConnection` 注入）。 */
class FakeConnection {
  state = HubConnectionState.Disconnected
  response: unknown = null
  readonly invocations: Array<{ method: string; args: unknown[] }> = []

  onreconnecting(_handler: (error?: Error) => void): void {}

  onreconnected(_handler: (connectionId?: string) => void): void {}

  onclose(_handler: (error?: Error) => void): void {}

  async start(): Promise<void> {
    this.state = HubConnectionState.Connected
  }

  async stop(): Promise<void> {
    this.state = HubConnectionState.Disconnected
  }

  invoke(method: string, ...args: unknown[]): Promise<unknown> {
    this.invocations.push({ method, args })
    return Promise.resolve(this.response)
  }
}

function account(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    ok: true,
    code: 'ok',
    message: '',
    id: 7,
    username: 'alice',
    displayName: '爱丽丝',
    accountSession: 'session-1',
    recoveryCode: null,
    ...overrides,
  }
}

function gatewayWith(fake: FakeConnection): AccountGateway {
  return new AccountGateway(() => {}, () => fake as unknown as HubConnection)
}

describe('账号回执归一化（不可信输入）', () => {
  it('缺 ok / code 或不是对象 → null（不采纳）', () => {
    expect(normalizeAccount(null)).toBeNull()
    expect(normalizeAccount('nope')).toBeNull()
    expect(normalizeAccount({})).toBeNull()
    expect(normalizeAccount({ ok: 'yes', code: 'ok' })).toBeNull()
  })

  it('缺字段按空值降级，会话 / 恢复码只在非空字符串时保留', () => {
    const result = normalizeAccount({ ok: false, code: 'invalid_credentials' })
    expect(result).toEqual({
      ok: false,
      code: 'invalid_credentials',
      message: '',
      id: 0,
      username: '',
      displayName: '',
      accountSession: null,
      recoveryCode: null,
      // 服务端没说能不能开桌 → 一律当"不能"（不猜能力；D-0026 的判定在服务端）。
      canCreateTable: false,
    })
  })
})

describe('账号网关接线与秘密纪律', () => {
  it('注册成功即登录：留下内存资料，并把一次性恢复码原样交回调用方', async () => {
    const fake = new FakeConnection()
    fake.response = account({ recoveryCode: 'recovery-1' })
    const gateway = gatewayWith(fake)

    const result = await gateway.register('alice', '爱丽丝', 'password-123')

    expect(fake.invocations[0]).toEqual({
      method: 'Register',
      args: ['alice', '爱丽丝', 'password-123'],
    })
    expect(result.recoveryCode).toBe('recovery-1')
    expect(gateway.profile).toEqual({
      id: 7,
      username: 'alice',
      displayName: '爱丽丝',
      accountSession: 'session-1',
      canCreateTable: false,
    })
  })

  it('开桌能力随回执采纳：服务端说能开就记成能开，没说就记成不能', async () => {
    const fake = new FakeConnection()
    fake.response = account({ canCreateTable: true })
    const gateway = gatewayWith(fake)

    await gateway.login('alice', 'password-123')
    expect(gateway.profile?.canCreateTable).toBe(true)

    // 同一份回执把字段去掉（老服务端 / 部署方关掉了自助开桌）→ 不能开。
    fake.response = account()
    await gateway.login('alice', 'password-123')
    expect(gateway.profile?.canCreateTable).toBe(false)
  })

  it('登录失败不改状态：不留下任何资料', async () => {
    const fake = new FakeConnection()
    fake.response = account({ ok: false, code: 'invalid_credentials', message: '登录名或口令不正确' })
    const gateway = gatewayWith(fake)

    const result = await gateway.login('alice', 'wrong')

    expect(result.ok).toBe(false)
    expect(gateway.profile).toBeNull()
  })

  it('改玩家名：带内存里的账号会话调用，成功后同步本地显示名', async () => {
    const fake = new FakeConnection()
    fake.response = account()
    const gateway = gatewayWith(fake)
    await gateway.login('alice', 'password-123')

    fake.response = account({ displayName: '新名字', accountSession: null })
    const result = await gateway.changeDisplayName('新名字')

    expect(result.ok).toBe(true)
    expect(fake.invocations.at(-1)).toEqual({
      method: 'ChangeDisplayName',
      args: ['session-1', '新名字'],
    })
    expect(gateway.profile?.displayName).toBe('新名字')
  })

  it('登出：先请服务端撤销，再清本地；服务端不可达也照样清', async () => {
    const fake = new FakeConnection()
    fake.response = account()
    const gateway = gatewayWith(fake)
    await gateway.login('alice', 'password-123')

    fake.response = { ok: true, code: 'ok', message: '已登出' }
    await gateway.logout()

    expect(fake.invocations.at(-1)).toEqual({ method: 'Logout', args: ['session-1'] })
    expect(gateway.profile).toBeNull()
  })

  it('恢复码重置：成功后采纳新会话（旧会话在服务端已被撤销）', async () => {
    const fake = new FakeConnection()
    fake.response = account({ accountSession: 'session-new', recoveryCode: 'recovery-new' })
    const gateway = gatewayWith(fake)

    const result = await gateway.resetPassword('alice', 'recovery-old', 'password-456')

    expect(result.ok).toBe(true)
    expect(gateway.profile?.accountSession).toBe('session-new')
    expect(result.recoveryCode).toBe('recovery-new')
  })

  it('stop 清内存资料并断开连接', async () => {
    const fake = new FakeConnection()
    fake.response = account()
    const gateway = gatewayWith(fake)
    await gateway.login('alice', 'password-123')

    await gateway.stop()

    expect(gateway.profile).toBeNull()
    expect(fake.state).toBe(HubConnectionState.Disconnected)
  })
})
