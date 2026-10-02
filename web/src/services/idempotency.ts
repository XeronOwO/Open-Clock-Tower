/**
 * 幂等键生成。
 *
 * 服务端的幂等闸要求每条命令带幂等键，重复投递返回首次结果。
 * 生成点在客户端：同一条命令重试必须**复用**同一个键（由调用方持有），
 * 因此这里只负责「生成一个新键」，不负责记忆。
 */
export function newIdempotencyKey(prefix: string): string {
  const suffix =
    typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
      ? crypto.randomUUID()
      : fallbackToken()

  return `${prefix}:${suffix}`
}

function fallbackToken(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    const bytes = crypto.getRandomValues(new Uint8Array(16))
    return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
  }

  // 最后兜底：仅用于本机小圈子自用的开发进程，不承担安全语义。
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}
