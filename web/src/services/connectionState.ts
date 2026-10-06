/**
 * 实时连接状态与 Hub 入口路径。说书人与玩家两端共用——
 * 它只是"连接在不在"和一个地址，不含任何一端专属的领域字段。
 */
import { withBase } from '@/services/basePath'
import { RUNTIME_BASE } from '@/services/runtimeBase'

/** 网关连接状态（说书人与玩家两端共用）。 */
export type GatewayState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

/** Hub 入口路径（跟随构建期部署前缀；根路径部署时仍是 `/hub/game`）。 */
export const HUB_PATH = withBase('/hub/game', RUNTIME_BASE)

/** 账号 Hub 入口（D-0021）：注册 / 登录 / 改名 / 找回；与游戏 Hub 分开，凭据与游戏连接无关。 */
export const ACCOUNT_HUB_PATH = withBase('/hub/account', RUNTIME_BASE)
