/**
 * 运行期的部署基址：把 vite 的 `import.meta.env.BASE_URL` 交给唯一那份归一化规则
 * （`basePath.ts`；构建配置用的是同一个函数）。
 *
 * 单独成模块的原因：`basePath.ts` 必须保持纯函数（构建配置在 Node 类型环境下也要能导入它），
 * 所以"读环境"这一步留在前端运行时这一侧。
 */
import { resolveDeployBase } from '@/services/basePath'

/** 已归一化的部署前缀（恒以 `/` 开头、恒以 `/` 结尾；默认 `/`）。 */
export const RUNTIME_BASE: string = resolveDeployBase(import.meta.env.BASE_URL)
