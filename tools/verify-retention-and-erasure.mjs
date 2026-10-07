#!/usr/bin/env node
/**
 * 数据删除路径与保留策略的**真机读数装置**（M5 / G-A6-5 · G-A1-6）：
 * 账号注销端到端 + 席位释放 + 登录名释放，以及"什么会被留下"。
 *
 * 它回答一个**本机测试库证明不了**的问题：把包真的装到服务器上之后，注销这条路径在真协议栈上
 * （真 nginx + 真 Kestrel + 真 SignalR + 真 SQLite）是不是真的把这个人抹掉了。
 * 决定性的三条读数只有真机能给：
 *   ① 注销之后**旧账号会话**与**已经进门的那条连接**是否当场失效；
 *   ② 他占的席位是否真的空出来了（别人能坐、大厅人数回到 0）；
 *   ③ 同一个登录名能不能**重新注册**（D-0036 口径 11 明说"会被释放"）。
 *
 * 覆盖的链路（一段一条判据）：
 *   probe     目标可达（地址 / 前缀写错时以退出码 2 收场）
 *   erasure   注销端到端：注册 → 开桌 → 入座 → 错口令被拒（账号不动）→ 对口令成功 →
 *             旧会话被拒 → 旧连接被踢 → 席位释放、他开的桌留着但没有主人 → 登录名可再注册
 *   cleanup   收尾：把装置建出来的东西列清楚（账号已自删；那张空桌交给保留期）
 *
 * **只有 `--verify-erasure` 才动目标站点**：这一段会在服务器上注册账号、开一张桌。
 * 账号在段内自己注销掉（不留残留）；那张桌**刻意留着**——它是"注销不毁掉别人的对局"的判据，
 * 而它是一张空桌（没有事件），到保留期（默认 24 小时）会被自动回收，
 * 也可以由运维用 `retire-tables` 提前收掉（见输出末行的提示）。
 *
 * 保留策略那一半（谁被回收、谁不被回收）**不在本装置里**：它要造"很久没动的桌"，
 * 而那只能改库或改配置——真机读数由部署者按 `docs/acceptance/batches.md` 的批次记录取
 * （`retire-tables` 的只报告档 / 副本库上的 `--apply` / 启动日志里的清扫行）。
 *
 * 用法（在仓库根运行）：
 *   node tools/verify-retention-and-erasure.mjs --base-url http://<主机>/<前缀>/ --list-sections
 *   node tools/verify-retention-and-erasure.mjs --base-url ... --verify-erasure
 *
 * 退出码：0 = 全部通过；1 = 有失败；2 = 环境问题（参数不合法 / 目标探活不通 / 缺 signalr 客户端）。
 */
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createChecker, createSectionRunner } from './lib/verify-sections.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')

const SECTIONS = [
  { id: 'probe', title: '探活：目标站点可达（地址 / 前缀写错时以退出码 2 收场）' },
  { id: 'erasure', title: '注销端到端：账号抹掉、会话与连接失效、席位释放、他开的桌留着（G-A1-6）' },
  { id: 'cleanup', title: '收尾：装置建出来的东西列清楚（账号已自删，那张空桌交给保留期）' },
]

let options
try {
  options = parseArguments(process.argv.slice(2))
} catch (error) {
  console.error(`参数错误：${error instanceof Error ? error.message : String(error)}`)
  process.exit(2)
}

if (options.listSections) {
  console.log('可用段落（按执行顺序；--only 与 --from 互斥）：')
  for (const section of SECTIONS) {
    console.log(`  ${section.id.padEnd(10)} ${section.title}`)
  }

  process.exit(0)
}

const runner = createSectionRunner(SECTIONS, { only: options.only, from: options.from })
const checker = createChecker({
  sections: SECTIONS,
  isJudged: runner.isJudged,
  currentSection: () => runner.currentId,
})
const check = checker.check

const base = options.baseUrl
const timeoutMs = options.timeoutSeconds * 1000

let signalR = null
try {
  // 依赖装在 web/node_modules：脚本住在 tools/，所以要显式按 web/ 解析（Node 的默认查找不会跨目录）。
  signalR = createRequire(path.join(webRoot, 'package.json'))('@microsoft/signalr')
} catch (error) {
  console.error(`缺少 @microsoft/signalr：${String(error)}`)
  console.error('先运行：cd web; npm install')
  process.exit(2)
}

/** 本次运行的标记：登录名与桌名都带它，服务器上一眼能认出是验收残留。 */
const stamp = Date.now().toString(36)
const password = `pw-${stamp}-oct`
const username = `erasure-${stamp}`
const tableName = `注销验收-${stamp}`
/** 装置建出来的东西（收尾提示用）。 */
const created = { gameId: null }

/** 目标站点的某个路径（base 已保证以 / 结尾，所以相对路径能正确拼接前缀）。 */
function target(pathname) {
  return new URL(pathname, base).toString()
}

/** 调 Hub 方法并把 `HubException` 收成结果的一部分（一次没预料的拒绝不该让整份报告崩在半路）。 */
async function invokeSafe(connection, method, ...args) {
  try {
    return await connection.invoke(method, ...args)
  } catch (error) {
    return { hubError: error instanceof Error ? error.message : String(error) }
  }
}

/** 开一条账号连接（账号自助与大堂都走它）。 */
async function connectAccountAsync() {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(target('hub/account'))
    .configureLogging(signalR.LogLevel.Error)
    .build()
  await connection.start()
  return connection
}

/** 开一条**指定桌**的游戏连接。 */
async function connectGameAsync(gameId) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${target('hub/game')}?gameId=${encodeURIComponent(gameId)}`)
    .configureLogging(signalR.LogLevel.Error)
    .build()
  await connection.start()
  return connection
}

// ── 探活 ─────────────────────────────────────────────────────────────────────
if (runner.begin('probe')) {
  try {
    const response = await fetch(target('healthz'), { signal: AbortSignal.timeout(timeoutMs) })
    const body = await response.json().catch(() => null)
    check('目标站点健康检查 200', response.status === 200, `HTTP ${response.status}`)
    check('健康检查给出席位数量', typeof body?.seatCount === 'number', `seatCount=${body?.seatCount}`)
  } catch (error) {
    console.error(`目标不可达：${error instanceof Error ? error.message : String(error)}`)
    console.error(`（本次用的地址是 ${base}；地址或前缀写错时以退出码 2 收场。）`)
    process.exit(2)
  }
}

// ── 注销端到端 ───────────────────────────────────────────────────────────────
if (runner.begin('erasure')) {
  if (!options.verifyErasure) {
    console.log('  未开启 --verify-erasure：这一段会在目标站点注册账号与开桌，所以必须显式开启。')
  } else {
    const account = await connectAccountAsync()
    let seat = null
    try {
      // ① 注册即登录。
      const registered = await invokeSafe(account, 'Register', username, `要走的人-${stamp}`, password)
      check('注册成功（注销要从一个真账号出发）', registered?.ok === true, `code=${registered?.code}`)
      const session = registered?.accountSession ?? null
      check('注册回执带账号会话', typeof session === 'string' && session.length > 0)

      // ② 开一桌并坐进 1 号席：注销要同时清掉"归属"与"席位认领"两样。
      const created1 = await invokeSafe(account, 'CreateTable', session, tableName, 5)
      check('开桌成功（这一段需要一张真桌）', created1?.ok === true, `code=${created1?.code}`)
      created.gameId = created1?.gameId ?? null

      seat = await connectGameAsync(created.gameId)
      const joined = await invokeSafe(seat, 'JoinTable', session, 1, 0)
      check('凭账号入座成功（席位认领成立）', typeof joined?.credential === 'string' && joined.credential.length > 0, `hubError=${joined?.hubError ?? ''}`)
      const credential = joined?.credential ?? null

      const before = await invokeSafe(account, 'ListTables', session)
      const mine = Array.isArray(before) ? before.find((item) => item.gameId === created.gameId) : null
      check('大厅认得这一桌是我开的、且坐着 1 人', mine?.createdByMe === true && mine?.takenSeatCount === 1, `createdByMe=${mine?.createdByMe} taken=${mine?.takenSeatCount}`)
      const seatNumbers = mine?.occupiedSeatNumbers ?? []
      check('已占席位号里含 1 号', Array.isArray(seatNumbers) && seatNumbers.includes(1), `occupied=${JSON.stringify(seatNumbers)}`)

      // ③ 口令不对：**什么都不删**（账号与席位都还在）。
      const denied = await invokeSafe(account, 'DeleteAccount', session, '猜的口令')
      check('口令不对 → 注销被拒（invalid_credentials）', denied?.ok === false && denied?.code === 'invalid_credentials', `code=${denied?.code}`)
      const stillThere = await invokeSafe(account, 'Resume', session)
      check('被拒之后账号照旧可用（拒绝没有顺手把人注销掉）', stillThere?.ok === true, `code=${stillThere?.code}`)

      // ④ 口令正确：注销。
      const deleted = await invokeSafe(account, 'DeleteAccount', session, password)
      check('注销成功', deleted?.ok === true, `code=${deleted?.code} message=${deleted?.message}`)

      // ⑤ "下一次进门"被拒：旧账号会话当场失效。
      const resumed = await invokeSafe(account, 'Resume', session)
      check('旧账号会话当场失效（Resume 被拒）', resumed?.ok === false, `code=${resumed?.code}`)

      // ⑥ **已经进门**的那条连接也被踢（M2 / G-A2-1 的撤销面）。
      const afterKick = await invokeSafe(seat, 'Nominate', credential, 2, `erasure-${stamp}-after`)
      check(
        '已进门的席位连接被踢（命令被凭据闸拒）',
        typeof afterKick?.hubError === 'string' && afterKick.hubError.includes('连接凭据无效'),
        `hubError=${afterKick?.hubError ?? '(没被拒)'}`,
      )

      // ⑦ 席位释放、他开的桌**留着**（不毁掉别人的对局），只是不再属于任何人。
      const tables = await invokeSafe(account, 'ListTables', null)
      const kept = Array.isArray(tables) ? tables.find((item) => item.gameId === created.gameId) : null
      check('他开的桌留在库里（没有连带删掉）', kept !== undefined && kept !== null, `gameId=${created.gameId}`)
      check('那张桌的席位空出来了（别人能坐）', kept?.takenSeatCount === 0, `taken=${kept?.takenSeatCount}`)
      check('未登录视角下"我开的桌"是空的（归属已随账号一起没了）', Array.isArray(tables) && tables.every((item) => item.createdByMe === false))

      // ⑧ 登录名被释放：同一个名字能再注册（D-0036 口径 11 明说的边界）。
      const again = await invokeSafe(account, 'Register', username, `回来的人-${stamp}`, password)
      check('同一登录名可以重新注册（登录名被释放）', again?.ok === true, `code=${again?.code}`)

      // ⑨ 收尾：把第二个账号也注销掉，不给目标实例留任何账号。
      if (again?.ok === true) {
        const cleanup = await invokeSafe(account, 'DeleteAccount', again.accountSession, password)
        check('收尾：第二个账号也注销掉（不留账号残留）', cleanup?.ok === true, `code=${cleanup?.code}`)
      }
    } finally {
      if (seat !== null) {
        await seat.stop().catch(() => {})
      }

      await account.stop().catch(() => {})
    }
  }
}

// ── 收尾 ─────────────────────────────────────────────────────────────────────
if (runner.begin('cleanup')) {
  const gameId = created.gameId
  check(
    '本次没有留下任何账号（账号由装置自己注销）',
    options.verifyErasure,
    options.verifyErasure ? '' : '未跑注销段（--verify-erasure 未开启）',
  )

  if (gameId !== null) {
    console.log(`\n留给你的一件事：装置开的空桌 \`${gameId}\`（"${tableName}"）留在了目标实例上。`)
    console.log('它没有任何事件，属于"从未开局的桌"——到保留期（默认 24 小时）会被自动回收。')
    console.log('想立刻收掉就在那台机器上停服后跑：')
    console.log(`  <APP_DIR>/OpenClockTower.Server retire-tables --apply --empty-hours 0 --db <APP_DIR>/data/oct.db`)
  }
}

checker.report()
const failed = checker.results.filter((result) => result.outcome === 'fail').length
process.exit(failed === 0 ? 0 : 1)

/**
 * 解析命令行：只认 `--名字 值` 与开关两种形态，认不出来的当场报错（不静默忽略）。
 */
function parseArguments(argv) {
  const parsed = {
    baseUrl: process.env.OCT_BASE_URL ?? 'http://127.0.0.1:5080/',
    timeoutSeconds: 20,
    only: [],
    from: undefined,
    listSections: false,
    verifyErasure: false,
  }

  for (let index = 0; index < argv.length; index += 1) {
    const name = argv[index]
    const value = argv[index + 1]
    switch (name) {
      case '--base-url': {
        if (value === undefined) {
          throw new Error('--base-url 需要一个地址')
        }

        const url = new URL(value).toString()
        if (!url.endsWith('/')) {
          throw new Error(`--base-url 必须以 / 结尾（前缀与相对路径拼接全靠它）：${value}`)
        }

        parsed.baseUrl = url
        index += 1
        break
      }
      case '--timeout':
        parsed.timeoutSeconds = positiveInteger(value, '--timeout')
        index += 1
        break
      case '--verify-erasure':
        parsed.verifyErasure = true
        break
      case '--only':
        if (value === undefined) {
          throw new Error('--only 需要段名')
        }

        parsed.only.push(value)
        index += 1
        break
      case '--from':
        parsed.from = value
        index += 1
        break
      case '--list-sections':
        parsed.listSections = true
        break
      case '--help':
      case '-h':
        console.log(
          '用法：node tools/verify-retention-and-erasure.mjs [--base-url http://主机/前缀/] [--timeout 20]'
            + ' [--verify-erasure] [--only 段名] [--from 段名] [--list-sections]',
        )
        process.exit(0)
        break
      default:
        throw new Error(`参数不认识：${name}`)
    }
  }

  if (parsed.only.length > 0 && parsed.from !== undefined) {
    throw new Error('--only 与 --from 互斥')
  }

  return parsed
}

function positiveInteger(value, name) {
  const parsed = Number.parseInt(value ?? '', 10)
  if (!Number.isInteger(parsed) || parsed <= 0) {
    throw new Error(`${name} 需要正整数：${value}`)
  }

  return parsed
}
