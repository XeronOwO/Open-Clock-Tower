#!/usr/bin/env node
/**
 * 滥用与风控的真机读数装置（M4：G-A5-2 · G-A5-5 · G-A5-8 · G-A5-10 的一半）。
 *
 * 它回答一个**本机测试库证明不了**的问题：把包真的装到服务器上之后，注册额度、开桌配额、
 * 自由文本上限与写文本频率**在真协议栈上**是不是真的生效——真 nginx + 真 Kestrel + 真 SignalR。
 * 「日志里有没有带上登录名 / 操作者 / 真实来源、注入能不能伪造第二行」要靠 SSH 读 journalctl，
 * 装置做不到，那几条由部署者按 `docs/acceptance/batches.md` 的批次记录手工取证。
 *
 * 覆盖的链路（一段一条判据）：
 *   probe        目标可达（地址 / 前缀写错时以退出码 2 收场）
 *   registration 注册在额度处停住（`too_many_attempts`），且**被拒这一跳明显快于**成功那一跳
 *                （证明拒绝发生在慢哈希之前——否则限速只是"晚一点被刷爆"）
 *   tables       开桌超单账号配额被拒（`table_quota_account`），且拒绝发生在写库之前
 *   text         自由文本上限：超长被拒、换行放行、控制字符被拒、幂等键有界、注记空转被拒
 *   frequency    写文本的频率额度：第 N+1 次被拒，文案带"秒后再试"
 *   closed       关掉自助注册后：注册被拒（`registration_closed`），既有账号照常登录
 *
 * **每一段都要显式开启**（`--verify-*`），因为它们**都会在目标服务器上建数据**：
 * 注册额度段要建满额度个账号、开桌配额段要建满额度张桌、文本段要建 1 个账号 + 1 张桌、
 * 频率段同理、开关段要一次注册尝试。默认跑一遍只做探活——不会悄悄在你的部署上留东西。
 * 段与段之间**不共用判定范围**：`--only X` 只跑 X 及其前置（未开启的前置段直接跳过，不建数据），
 * 所以"一次运行验一项"是安全的；要连验几项，就在每项之间重启宿主（重启同时清掉内存里的计数桶）。
 *
 * 用法（在仓库根运行）：
 *   node tools/verify-abuse-guard.mjs --base-url http://<主机>/<前缀>/ --list-sections
 *   node tools/verify-abuse-guard.mjs --base-url ... --only text --verify-text-limits
 *   node tools/verify-abuse-guard.mjs --base-url ... --only registration --verify-register-quota --register-quota 2
 *   node tools/verify-abuse-guard.mjs --base-url ... --only tables --verify-table-quota --table-quota 1
 *   node tools/verify-abuse-guard.mjs --base-url ... --only frequency --verify-write-text-quota --write-text-quota 3
 *   node tools/verify-abuse-guard.mjs --base-url ... --only closed --expect-registration-closed --login-probe <登录名> --login-password <口令>
 *
 * **额度段要跟部署配置对上**：`--register-quota` / `--table-quota` / `--write-text-quota` 填的是
 * **部署机上那一份配置的当前值**（要验"小额度下真会拦"，就先在部署机上临时把它调小再跑）。
 *
 * **日志那一半要 SSH 复核**（装置只走 HTTP/SignalR）：审计日志里有没有登录名 / 操作者 / 真实来源、
 * 注入的换行有没有被转义成单行——装置会把"该看哪一行"打出来，读数由部署者 `journalctl` 取。
 *
 * 数据残留：**会建账号与桌**（这是注册 / 开桌配额的判据本身）。收尾会把清理 SQL 原样打印出来，
 * 由部署者执行——装置不替你连 SSH。
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
  { id: 'registration', title: '注册额度：连续注册在额度处停住，且拒绝发生在慢哈希之前（G-A5-2）' },
  { id: 'tables', title: '开桌配额：单账号在册桌数到顶后拒绝（G-A5-5）' },
  { id: 'text', title: '自由文本：超长 / 控制字符 / 幂等键被拒，换行放行，注记空转被拒（G-A5-8）' },
  { id: 'frequency', title: '写文本频率：额度用尽后被拒并给出等待时间（G-A5-8 频率半）' },
  { id: 'closed', title: '自助注册开关：关掉后开不出新账号，登录照常（G-A5-2）' },
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
    console.log(`  ${section.id.padEnd(13)} ${section.title}`)
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

/** 本次运行的标记：账号与桌名都带它，服务器上一眼能认出是验收残留。 */
const stamp = Date.now().toString(36)
const password = `pw-${stamp}-oct`
/** 装置自己产生的数据：收尾用它打印清理 SQL。 */
const created = { usernames: [], gameIds: [] }

/** 目标站点的某个路径（base 已保证以 / 结尾，所以相对路径能正确拼接前缀）。 */
function target(pathname) {
  return new URL(pathname, base).toString()
}

async function fetchWithTimeout(url, init = {}) {
  return await fetch(url, { ...init, signal: AbortSignal.timeout(timeoutMs), redirect: 'manual' })
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

/** 开一条**指定桌**的游戏连接并加入主持台，返回连接与连接级凭据（加入被拒时凭据为 null）。 */
async function connectStorytellerAsync(gameId, accountSession) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${target('hub/game')}?gameId=${encodeURIComponent(gameId)}`)
    .configureLogging(signalR.LogLevel.Error)
    .build()
  await connection.start()
  const joined = await invokeSafe(connection, 'JoinStorytellerWithAccount', accountSession)
  return { connection, credential: joined?.credential ?? null, hubError: joined?.hubError }
}

/** 注册一个带本次标记的探针账号；返回 {username, result}（被限速时 result.code = too_many_attempts）。 */
async function registerProbeAsync(account, suffix) {
  const username = `abuse-${stamp}-${suffix}`
  const result = await invokeSafe(account, 'Register', username, `验收${suffix}`, password)
  if (result.ok) {
    created.usernames.push(username)
  }

  return { username, result }
}

/** 上报座位状态（开局前就能做：这条链路上最便宜的自由文本入口）。 */
function reportAsync(connection, credential, reason, key) {
  // 这一条**故意不做兜底**：调用方自己 catch（频率段就是要看它被拒的那一跳）。
  return connection.invoke('ReportSeatState', credential, 1, 'Alive', null, null, null, null, reason, null, key)
}

/**
 * 调 Hub 方法并把 `HubException` 收成结果的一部分。
 *
 * 服务端的拒绝有两条形态：**命令被拒**（返回 `kind=Rejected` + 拒绝码）与**入口被拒**
 * （抛 `HubException`：参数层、准入层、加入入口）。装置要把两者都当成"读数"而不是崩溃——
 * 一次没预料的限速不该让整份报告崩在半路（那样后面的段一条读数都拿不到）。
 */
async function invokeSafe(connection, method, ...args) {
  try {
    return await connection.invoke(method, ...args)
  } catch (error) {
    return { hubError: error instanceof Error ? error.message : String(error) }
  }
}

/** 结果的一句摘要：优先给 `HubException` 的原文（那是服务端说的人话）。 */
function describe(result) {
  if (result?.hubError) {
    return `HubException: ${result.hubError}`
  }

  if (result?.rejectionCode) {
    return `code=${result.rejectionCode}`
  }

  if (result?.code) {
    return `code=${result.code} ${result.message ?? ''}`
  }

  return `kind=${result?.kind ?? '（无）'}`
}

/**
 * 额度类段落的开关：没显式开启就打一句说明并跳过（返回 false）。
 *
 * 为什么默认不跑：这三段的判据本身就要**把额度用尽**——按产品默认值跑一遍会留下 10 个账号、
 * 12 张桌，还会把同一来源的注册额度吃干，让后面的段落互相挤占。要验就在部署机上临时把配置调小。
 */
function requireOptIn(enabled, title, flag) {
  if (enabled) {
    return true
  }

  console.log(`  [SKIP] ${title}：本段要显式开启（${flag}）。`)
  console.log('         它是"把额度用尽"的判据，按默认值跑会留下大量残留；')
  console.log('         做法：部署机上临时调小对应配置 → 重启 → 带该开关重跑（见批次记录）。')
  return false
}

/** 起一张探针桌并占住主持台；返回 {gameId, connection, credential, session}。 */
async function openProbeTableAsync(account, suffix) {
  const probe = await registerProbeAsync(account, suffix)
  if (!probe.result.ok) {
    return { ...probe, gameId: null, connection: null, credential: null }
  }

  const createdTable = await invokeSafe(account, 'CreateTable', probe.result.accountSession, `验收桌${stamp}-${suffix}`, 5)
  if (!createdTable.ok) {
    return { ...probe, gameId: null, connection: null, credential: null, createFailure: createdTable }
  }

  created.gameIds.push(createdTable.gameId)
  const joined = await connectStorytellerAsync(createdTable.gameId, probe.result.accountSession)
  return { ...probe, gameId: createdTable.gameId, connection: joined.connection, credential: joined.credential, joinError: joined.hubError }
}

/** 段 1：探活。不通就直接以退出码 2 收场（"断言失败"与"没连上"要分得清）。 */if (runner.begin('probe')) {
  let health = null
  try {
    health = await fetchWithTimeout(target('healthz'))
  } catch (error) {
    console.error(`目标不可达：${target('healthz')} → ${error instanceof Error ? error.message : String(error)}`)
    process.exit(2)
  }

  if (health.status !== 200) {
    console.error(`目标返回 ${health.status}：${target('healthz')}（地址 / 前缀写错？）`)
    process.exit(2)
  }

  const payload = await health.json().catch(() => null)
  check('宿主探活通过（/healthz 回 ok）', payload?.status === 'ok', `status=${health.status} body=${JSON.stringify(payload)}`)

  // 页面在生产形态下由宿主自己发（wwwroot 随发布带上）；开发形态（只跑宿主、前端在 Vite）没有这一层，
  // 那不是"环境不对"——所以只有拿得到页面时才判它，拿不到就说明一句，不假绿也不误红。
  const home = await fetchWithTimeout(target('./'))
  if (home.status === 200) {
    check('首页可达且是 HTML', (await home.text()).includes('<html'), `status=${home.status}`)
  } else {
    console.log(`  [SKIP] 首页返回 ${home.status}：本宿主没带前端产物（开发形态），这一条不在本次判定里`)
  }
}

/** 段 2：注册额度（G-A5-2）。**要显式开启**：它会把来源的注册额度用尽，并与其余段落互相挤占。 */
if (runner.begin('registration') && requireOptIn(options.verifyRegisterQuota, '注册额度', '--verify-register-quota')) {
  const account = await connectAccountAsync()
  try {
    const quota = options.registerQuota
    const durations = []
    let blocked = null

    for (let attempt = 1; attempt <= quota + 1; attempt += 1) {
      const startedAt = Date.now()
      const probe = await registerProbeAsync(account, `reg${attempt}`)
      durations.push(Date.now() - startedAt)

      if (attempt <= quota) {
        check(`第 ${attempt} 次注册成功（额度内）`, probe.result.ok === true, `code=${probe.result.code}`)
      } else {
        blocked = probe.result
      }
    }

    check(
      `第 ${quota + 1} 次注册被拒（too_many_attempts）`,
      blocked?.code === 'too_many_attempts',
      `code=${blocked?.code ?? '（没有拒绝）'} message=${blocked?.message ?? ''}`,
    )
    check(
      '限速文案给出等待时间（说人话）',
      typeof blocked?.message === 'string' && blocked.message.includes('秒后再试'),
      blocked?.message ?? '',
    )

    // 关键读数：被拒这一跳的量级要**远小于**成功那一跳（成功要跑两次 PBKDF2 ≈ 50–110 ms）。
    const accepted = durations.slice(0, quota)
    const slowest = Math.max(...accepted)
    const rejected = durations[quota]
    check(
      '被拒发生在慢哈希之前（拒绝耗时明显小于成功耗时）',
      rejected * 3 < slowest,
      `成功最慢=${slowest}ms 被拒=${rejected}ms（成功一跳含两次 PBKDF2）`,
    )
  } finally {
    await account.stop().catch(() => {})
  }
}

/** 段 3：开桌配额（G-A5-5）。**要显式开启**（否则默认额度会留下 12 张桌的残留）。 */
if (runner.begin('tables') && requireOptIn(options.verifyTableQuota, '开桌配额', '--verify-table-quota')) {
  const account = await connectAccountAsync()
  try {
    const probe = await registerProbeAsync(account, 'tbl')
    if (!probe.result.ok) {
      check('探针账号注册成功（后续判据的前提）', false, `code=${probe.result.code}`)
    } else {
      const quota = options.tableQuota
      let blocked = null
      for (let attempt = 1; attempt <= quota + 1; attempt += 1) {
        const createdTable = await invokeSafe(
          account,
          'CreateTable',
          probe.result.accountSession,
          `验收桌${stamp}-tbl${attempt}`,
          5,
        )

        if (attempt <= quota) {
          check(`第 ${attempt} 张开桌成功（配额内）`, createdTable.ok === true, describe(createdTable))
          if (createdTable.ok) {
            created.gameIds.push(createdTable.gameId)
          }
        } else {
          blocked = createdTable
        }
      }

      check(
        `第 ${quota + 1} 张被拒（table_quota_account）`,
        blocked?.code === 'table_quota_account',
        blocked?.code ?? describe(blocked),
      )
      check(
        '拒绝文案说清怎么办（不提协议码）',
        typeof blocked?.message === 'string' && blocked.message.includes('桌'),
        blocked?.message ?? '',
      )
    }
  } finally {
    await account.stop().catch(() => {})
  }
}

/** 段 4：自由文本上限与注记空转（G-A5-8）。**要显式开启**：它要建 1 个账号 + 1 张桌。 */
if (runner.begin('text') && requireOptIn(options.verifyTextLimits, '自由文本上限', '--verify-text-limits')) {
  const account = await connectAccountAsync()
  let table = null
  try {
    table = await openProbeTableAsync(account, 'text')
    if (table.credential === null) {
      check('探针桌开出来并占住主持台（后续判据的前提）', false, `code=${table.createFailure?.code ?? table.result.code}`)
    } else {
      const accepted = await invokeSafe(table.connection, 'ReportSeatState', table.credential, 1, 'Alive', null, null, null, null, '开局前核对生死', null, `text-ok-${stamp}`)
      check('合法文本照常通行（反方向先立住）', accepted.kind === 'Accepted', describe(accepted))

      const multiline = await invokeSafe(table.connection, 'ReportSeatState', table.credential, 1, 'Alive', null, null, null, null, '第一行\r\n第二行\t结束', null, `text-ml-${stamp}`)
      check('换行 / 制表算可折叠空白（放行）', multiline.kind === 'Accepted', describe(multiline))

      const tooLong = await invokeSafe(table.connection, 'ReportSeatState', table.credential, 1, 'Alive', null, null, null, null, '甲'.repeat(201), null, `text-long-${stamp}`)
      check(
        '超长说明被拒（legality.text_too_long）',
        tooLong.rejectionCode === 'legality.text_too_long',
        describe(tooLong),
      )

      const control = await invokeSafe(table.connection, 'ReportSeatState', table.credential, 1, 'Alive', null, null, null, null, '甲\u0000乙', null, `text-ctl-${stamp}`)
      check(
        '控制字符被拒（legality.text_control_chars）',
        control.rejectionCode === 'legality.text_control_chars',
        describe(control),
      )

      const keyTooLong = await invokeSafe(table.connection, 'ReportSeatState', table.credential, 1, 'Alive', null, null, null, null, '正常原因', null, 'k'.repeat(65))
      check(
        '超长幂等键被拒（它进数据库主键，必须有界）',
        keyTooLong.rejectionCode === 'legality.idempotency_key_too_long',
        describe(keyTooLong),
      )

      const added = await invokeSafe(
        table.connection,
        'AddSeatAnnotation',
        table.credential,
        1,
        '18 不共边',
        `note-add-${stamp}`,
      )
      check('注记新增成功（后续空转判据的前提）', added.kind === 'Accepted', describe(added))

      const unchanged = await invokeSafe(
        table.connection,
        'UpdateSeatAnnotation',
        table.credential,
        1,
        '18 不共边',
        `note-same-${stamp}`,
      )
      check(
        '同文本的注记更新被拒（否则循环 Update 能写出无上限事件行）',
        unchanged.rejectionCode === 'legality.annotation_unchanged',
        describe(unchanged),
      )

      // 日志注入的**半条判据**：装置能证明"客户端原文会被带进参数层拒绝"，但"它在日志里是单行"
      // 只有 journalctl 看得到。装置把该看什么打出来，读数由部署者取（批次记录里贴了实测）。
      const injected = await invokeSafe(
        table.connection,
        'ReportSeatState',
        table.credential,
        1,
        '活着\n[Warning] 伪造日志行',
        null,
        null,
        null,
        null,
        '正常原因',
        null,
        `inject-${stamp}`,
      )

      check(
        '非法维度被拒（拒绝原因里嵌着客户端原文）',
        typeof injected.hubError === 'string' && injected.hubError.includes('未知的'),
        describe(injected),
      )
      console.log('  [手工] 在目标机上 `journalctl -u clocktower | grep 命令被拒绝（参数）` 复核：')
      console.log('         ① 原文里的换行是转义形态（反斜杠 + n）而不是真的换行；② 该行含 客户端 / 连接 上下文。')
      console.log(`         本次注入串：活着${String.fromCharCode(92)}n[Warning] 伪造日志行（连接来自本次装置运行）`)
    }
  } finally {
    await table?.connection?.stop().catch(() => {})
    await account.stop().catch(() => {})
  }
}

/** 段 5：写文本频率（G-A5-8 频率半）。**要显式开启**（默认额度 120 次，跑满等于刷一局的数据量）。 */
if (runner.begin('frequency') && requireOptIn(options.verifyWriteTextQuota, '写文本频率', '--verify-write-text-quota')) {
  const account = await connectAccountAsync()
  let table = null
  try {
    table = await openProbeTableAsync(account, 'freq')
    if (table.credential === null) {
      check('探针桌开出来并占住主持台（后续判据的前提）', false, `code=${table.createFailure?.code ?? table.result.code}`)
    } else {
      const quota = options.writeTextQuota
      for (let attempt = 1; attempt <= quota; attempt += 1) {
        const result = await invokeSafe(table.connection, 'ReportSeatState', table.credential, 1, 'Alive', null, null, null, null, `第 ${attempt} 条`, null, `freq-${attempt}-${stamp}`)
        check(`第 ${attempt} 次写文本在额度内`, result.kind === 'Accepted', describe(result))
      }

      const blocked = await invokeSafe(table.connection, 'ReportSeatState', table.credential, 1, 'Alive', null, null, null, null, '超额那条', null, `freq-over-${stamp}`)

      check(
        `第 ${quota + 1} 次被限速（Hub 拒绝）`,
        typeof blocked.hubError === 'string' && blocked.hubError.includes('秒后再试'),
        describe(blocked),
      )
      check(
        '限速文案带上限（说人话，玩家知道额度是多少）',
        typeof blocked.hubError === 'string' && blocked.hubError.includes('最多'),
        blocked.hubError ?? '',
      )
    }
  } finally {
    await table?.connection?.stop().catch(() => {})
    await account.stop().catch(() => {})
  }
}

/** 段 6：自助注册开关（G-A5-2）。 */
if (runner.begin('closed')) {
  if (!options.expectRegistrationClosed) {
    console.log('  [SKIP] 本段要部署方把 GameServer__AllowSelfRegistration 设为 false 后重跑：')
    console.log('         加 --expect-registration-closed（可配 --login-probe / --login-password 判"老账号照常登录"）')
  } else {    const account = await connectAccountAsync()
    try {
      const rejected = await account.invoke('Register', `abuse-${stamp}-closed`, '不该开出来的账号', password)
      check(
        '注册被拒（registration_closed）',
        rejected.code === 'registration_closed',
        `code=${rejected.code} message=${rejected.message ?? ''}`,
      )
      check(
        '拒绝文案是人话（不把协议码丢给用户）',
        typeof rejected.message === 'string' && rejected.message.includes('不开放自助注册'),
        rejected.message ?? '',
      )

      if (options.loginProbe === null) {
        console.log('  [SKIP] 未给 --login-probe：跳过"既有账号照常登录"这一条')
      } else {
        const login = await account.invoke('Login', options.loginProbe, options.loginPassword ?? password)
        check(
          '既有账号照常登录（注册被关不等于登录被关）',
          login.ok === true,
          `ok=${login.ok} code=${login.code}`,
        )
      }
    } finally {
      await account.stop().catch(() => {})
    }
  }
}

runner.reportTimings()
checker.report()
reportLeftovers()
process.exit(checker.results.some((result) => result.outcome === 'fail') ? 1 : 0)

/** 收尾：把本次留下的数据与清理 SQL 打印出来（装置不连 SSH，清理由部署者执行）。 */
function reportLeftovers() {
  console.log('\n=== 本次在目标服务器上留下的数据（请按需清理）===')
  console.log(`  测试账号：${created.usernames.length} 个（Users 表）`)
  for (const username of created.usernames) {
    console.log(`    ${username}（口令 ${password}）`)
  }

  console.log(`  测试桌　：${created.gameIds.length} 张（Games 表：${created.gameIds.join(', ') || '（没有开成）'}）`)
  if (created.usernames.length === 0) {
    console.log('  没有留下账号：无需清理。')
    return
  }

  const owners = `SELECT Id FROM Users WHERE Username LIKE 'abuse-${stamp}-%'`
  const games = `SELECT GameId FROM Games WHERE CreatedByAccountId IN (${owners})`
  console.log('  在目标机上执行（把 <DB> 换成库文件路径）：')
  console.log('    systemctl stop clocktower            # 必做：开着的桌活在宿主内存里，不停服务就删会被写回来')
  console.log(
    `    sqlite3 <DB> "DELETE FROM SeatBindings WHERE GameId IN (${games});` +
      ` DELETE FROM Events WHERE GameId IN (${games});` +
      ` DELETE FROM Snapshots WHERE GameId IN (${games});` +
      ` DELETE FROM Receipts WHERE GameId IN (${games});` +
      ` DELETE FROM Games WHERE CreatedByAccountId IN (${owners});` +
      ` DELETE FROM Users WHERE Username LIKE 'abuse-${stamp}-%';"`,
  )
  console.log('    systemctl start clocktower')
}

function parseArguments(argv) {
  const parsed = {
    baseUrl: new URL('http://127.0.0.1:5080/'),
    timeoutSeconds: 30,
    only: [],
    from: undefined,
    listSections: false,
    registerQuota: 10,
    tableQuota: 12,
    writeTextQuota: 120,
    verifyTextLimits: false,
    verifyRegisterQuota: false,
    verifyTableQuota: false,
    verifyWriteTextQuota: false,
    expectRegistrationClosed: false,
    loginProbe: null,
    loginPassword: null,
  }

  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index]
    const value = argv[index + 1]
    switch (flag) {
      case '--base-url': {
        if (value === undefined) {
          throw new Error('--base-url 需要值')
        }

        const url = new URL(value)
        if (!url.pathname.endsWith('/')) {
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
      case '--register-quota':
        parsed.registerQuota = positiveInteger(value, '--register-quota')
        index += 1
        break
      case '--table-quota':
        parsed.tableQuota = positiveInteger(value, '--table-quota')
        index += 1
        break
      case '--write-text-quota':
        parsed.writeTextQuota = positiveInteger(value, '--write-text-quota')
        index += 1
        break
      case '--verify-register-quota':
        parsed.verifyRegisterQuota = true
        break
      case '--verify-text-limits':
        parsed.verifyTextLimits = true
        break
      case '--verify-table-quota':
        parsed.verifyTableQuota = true
        break
      case '--verify-write-text-quota':
        parsed.verifyWriteTextQuota = true
        break
      case '--expect-registration-closed':
        parsed.expectRegistrationClosed = true
        break
      case '--login-probe':
        parsed.loginProbe = value ?? null
        index += 1
        break
      case '--login-password':
        parsed.loginPassword = value ?? null
        index += 1
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
          '用法：node tools/verify-abuse-guard.mjs [--base-url http://主机/前缀/] [--timeout 30]'
            + ' [--verify-text-limits] [--verify-register-quota --register-quota 2]'
            + ' [--verify-table-quota --table-quota 1] [--verify-write-text-quota --write-text-quota 3]'
            + ' [--expect-registration-closed] [--login-probe 登录名] [--login-password 口令]'
            + ' [--only 段名] [--from 段名] [--list-sections]',
        )
        process.exit(0)
        break
      default:
        throw new Error(`未知参数：${flag}`)
    }
  }

  if (parsed.only.length > 0 && parsed.from !== undefined) {
    throw new Error('--only 与 --from 互斥')
  }

  return parsed
}

function positiveInteger(value, flag) {
  const parsed = Number.parseInt(value ?? '', 10)
  if (!Number.isInteger(parsed) || parsed < 1) {
    throw new Error(`${flag} 必须是正整数：${value}`)
  }

  return parsed
}
