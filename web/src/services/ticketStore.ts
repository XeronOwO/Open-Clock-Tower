/**
 * 会话票据的本地存放。
 *
 * 票据就是身份（服务端由票据推导席位 / 说书人身份，客户端不能自称），因此这里只做
 * 「记住上次输入」这一件事，不做任何信任判断：拿到票据 ≠ 拿到视图，
 * 视图永远由服务端按当前状态重新投影。
 */
export class TicketStore {
  private readonly storage: Storage | null

  constructor(storage: Storage | null = safeLocalStorage()) {
    this.storage = storage
  }

  read(): string {
    try {
      return this.storage?.getItem(TicketStore.key) ?? ''
    } catch {
      return ''
    }
  }

  write(ticket: string): void {
    try {
      if (ticket.length === 0) {
        this.storage?.removeItem(TicketStore.key)
        return
      }

      this.storage?.setItem(TicketStore.key, ticket)
    } catch {
      // 隐私模式 / 存储配额：记不住票据不影响本次会话可用，不打扰用户。
    }
  }

  clear(): void {
    try {
      this.storage?.removeItem(TicketStore.key)
    } catch {
      // 同上：清理失败无副作用。
    }
  }

  private static readonly key = 'openclocktower.ticket'
}

function safeLocalStorage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}
