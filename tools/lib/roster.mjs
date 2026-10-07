/**
 * 花名册的唯一读法（装置侧）：从 `web/src/display/labels.ts` 的 `ROSTER` 里读。
 *
 * 为什么读它：服务端权威数据在 `src/OpenClockTower.Rules/SectsAndVioletsRoster.cs`，
 * 但装置跑在 Node 里读不了 C#；`labels.ts` 是**规范门禁逐条对账过**的镜像
 * （`tests/OpenClockTower.NormativeGates.Tests/RosterMirrorGateTests.cs`：slug / 中文名 / 类型 /
 * 设置调整，连**顺序**都对账，漂移即红），因此它是装置侧可用的唯一来源。
 *
 * 装置里**不要再手抄一份花名册**：抄下来的那份不会跟着花名册更新，于是断言要么写死条数
 * （花名册一长就假红——`verify-pit-hag` 曾把「整张角色列表」写死成 25，旅行者进册后读数变 30），
 * 要么只比对自己抄的那几个（假绿）。要哪一族的角色，就从这里派生。
 */

import { readFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..')

/** `labels.ts` 里旅行者的类型名（术语表 §9 的中文类型名，与门禁的 `TypeLabels` 同一套）。 */
const TRAVELLER_TYPE = '旅行者'

/** 条目形状与 `RosterMirrorGateTests.WebEntry()` 同形：`{ slug: '…', name: '…', type: '…' … }`。 */
const ENTRY_PATTERN = /\{\s*slug:\s*'([a-z0-9-]+)',\s*name:\s*'([^']*)',\s*type:\s*'([^']*)'/g

let cached = null

/** 整份花名册（30 条；`{ slug, name, type }`，顺序 = 术语表 §9 顺序）。 */
export function readRoster() {
  if (cached) {
    return cached
  }

  const labelsPath = path.join(repositoryRoot, 'web', 'src', 'display', 'labels.ts')
  const source = readFileSync(labelsPath, 'utf8')
  const start = source.indexOf('export const ROSTER')
  const end = source.indexOf('\n]', start)
  if (start < 0 || end < 0) {
    throw new Error(`读不出花名册：${labelsPath} 的 ROSTER 段落形状变了（解析器要跟着改）`)
  }

  const entries = [...source.slice(start, end).matchAll(ENTRY_PATTERN)].map((match) => ({
    slug: match[1],
    name: match[2],
    type: match[3],
  }))

  // 解析出 0 条时**显式失败**，不返回空表：空表会让"一个旅行者都没有"之类的阴性断言
  // 变成永真（假绿），也会让集合断言报出一句看不懂的话。
  if (entries.length === 0) {
    throw new Error(`花名册解析出 0 条：${labelsPath} 的 ROSTER 条目形状变了（解析器要跟着改）`)
  }

  cached = entries
  return cached
}

/** **角色列表**上的角色：镇民 / 外来者 / 爪牙 / 恶魔（不含旅行者；R-0060）。 */
export function characterList() {
  return readRoster().filter((entry) => entry.type !== TRAVELLER_TYPE)
}

/** 旅行者（在旅行者列表上，不在角色列表上）。 */
export function travellers() {
  return readRoster().filter((entry) => entry.type === TRAVELLER_TYPE)
}

/** 中文名；不在册的 slug 原样回显（与前端「未知取值不猜」同一姿态）。 */
export function nameOf(slug) {
  return readRoster().find((entry) => entry.slug === slug)?.name ?? slug
}
