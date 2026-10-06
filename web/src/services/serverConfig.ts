/**
 * 从服务端读取本局的运行期配置（今天只有席位数量）。
 *
 * 为什么要有这个出口：席位数量是**服务端配置**（`GameServer:SeatCount`），而说书人面板的席位名单
 * 是按它渲染的。此前前端把它写死在构建期（默认 5），于是服务端配 6 席时前端只画 5 席，
 * 说书人无法给第 6 席分配角色、那一名玩家永远进不了局——部署形态下这是硬阻塞。
 * 现在只留一个来源：服务端的 `/healthz`。
 *
 * 边界：服务端数据是不可信输入（架构 §4.4），这里只做"是不是一个可用的席位数量"的判定，
 * 取不到就返回 null，由调用方降级并显式告知，不在这一层猜一个值。
 */
import { withBase } from '@/services/basePath'
import { RUNTIME_BASE } from '@/services/runtimeBase'

/** 健康检查响应里本模块用到的字段。 */
interface HealthPayload {
  seatCount?: unknown
}

/**
 * 服务端读数取不到时的兜底席位。**只作显示兜底，不是配置口径**——
 * 真正的来源是服务端 `GameServer:SeatCount`，两边分叉会让最后一席分不到角色。
 */
export const DEFAULT_SEAT_COUNT = 5

/** 合法席位数量区间：与配板求解的可行范围一致，超出即视为坏数据。 */
const MIN_SEAT_COUNT = 1
const MAX_SEAT_COUNT = 20

/**
 * 读取服务端配置的席位数量；取不到或值不可用时返回 null（调用方降级，不在这里猜）。
 * @param fetchImpl 注入的 fetch（便于测试与替换）。
 */
export async function readSeatCount(fetchImpl: typeof fetch = fetch): Promise<number | null> {
  try {
    // 跟随构建期部署前缀：子路径部署（如 /clocktower）下同样命中同一个服务端。
    const response = await fetchImpl(withBase('/healthz', RUNTIME_BASE))
    if (!response.ok) {
      return null
    }

    const payload = (await response.json()) as HealthPayload
    const raw = payload.seatCount
    return typeof raw === 'number' && Number.isInteger(raw) && raw >= MIN_SEAT_COUNT && raw <= MAX_SEAT_COUNT
      ? raw
      : null
  } catch {
    // 取不到不是故障：面板照常可用，只是回到兜底席位，调用方会给出诊断。
    return null
  }
}
