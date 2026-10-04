/**
 * 实时连接状态与 Hub 入口路径。说书人与玩家两端共用——
 * 它只是"连接在不在"和一个地址，不含任何一端专属的领域字段。
 */
export type GatewayState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

/** Hub 入口相对路径：与 Vite 代理 / 生产同源部署一致。 */
export const HUB_PATH = '/hub/game'

/** 账号 Hub 入口（D-0021）：注册 / 登录 / 改名 / 找回；与游戏 Hub 分开，凭据与游戏连接无关。 */
export const ACCOUNT_HUB_PATH = '/hub/account'
