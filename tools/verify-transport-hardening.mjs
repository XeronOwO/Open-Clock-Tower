#!/usr/bin/env node
/**
 * 传输面加固的真机读数装置（M3：G-A3-2 · G-A3-3 · G-A3-4 · G-A5-7，以及 G-A1-1 的入口限速）。
 *
 * 它回答一个**本机测试库证明不了**的问题：把包真的装到服务器上之后，安全响应头、缓存口径、
 * 请求体上限与账号限速**在真协议栈上**是不是真的生效——真 nginx（或直连宿主）+ 真 Kestrel +
 * 真 SignalR 握手。本机的集成用例走 TestServer（内存连接），拿不到"哪一层拒的""响应头是不是
 * 反代发的那一份"这类读数。
 *
 * 覆盖的链路（一段一条判据）：
 *   probe    目标可达（地址 / 前缀写错时以退出码 2 收场）
 *   headers  五个安全响应头逐条在场；CSP 含百科图主机与本站 ws/wss；HSTS 只在 HTTPS 上出现；
 *            Server 头不暴露版本号（server_tokens off 的读数）
 *   cache    带内容哈希的产物长缓存（immutable）、页面外壳先回源校验（no-cache）
 *   limits   超限请求体被拒（413）：300 KB 由应用侧拒、2 MB 由反代或应用侧拒
 *   throttle 连续 5 次错误口令后第 6 次被限速（`too_many_attempts`），换个登录名不受牵连
 *
 * 用法（在仓库根运行）：
 *   node tools/verify-transport-hardening.mjs                                  # 本机宿主（默认 5080）
 *   node tools/verify-transport-hardening.mjs --base-url http://<主机>/<前缀>/  # 部署实例
 *   node tools/verify-transport-hardening.mjs --list-sections
 *   node tools/verify-transport-hardening.mjs --only limits
 *
 * 数据残留：**不写任何账号与桌**——限速段用的是不存在的登录名（只会失败，不会建号）。
 * 但失败计数会留在服务端的内存桶里（这是设计如此）：同一来源 5 分钟内反复运行会累计消耗
 * "同一地址 20 次"的额度，跑到第 4 次可能因为**地址级**额度用尽而判红——那不是实现缺陷，
 * 报告里会写明；等窗口过去再跑即可。
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
  { id: 'headers', title: '安全响应头与内容安全策略（G-A3-2）' },
  { id: 'cache', title: '缓存口径：带哈希的产物长缓存，页面外壳先回源校验（G-A3-6）' },
  { id: 'limits', title: '超限请求体被拒（G-A3-4：应用侧 256 KB / 反代 1 MB）' },
  { id: 'throttle', title: '账号入口限速：连续失败后被拒，且不误伤别的登录名（G-A1-1）' },
]

/** 三个文档段的响应头期望值（与服务端 ResponseHeadersMiddleware 同源）。 */
const EXPECTED_HEADERS = [
  ['x-content-type-options', 'nosniff'],
  ['referrer-policy', 'no-referrer'],
  ['x-frame-options', 'DENY'],
]

const REQUIRED_PERMISSIONS = ['camera=()', 'microphone=()', 'geolocation=()']
const WIKI_ORIGIN = 'https://clocktower-wiki.gstonegames.com'
const IMMUTABLE = 'public, max-age=31536000, immutable'
const REVALIDATE = 'no-cache'

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

/** 目标站点的某个路径（base 已保证以 / 结尾，所以相对路径能正确拼接前缀）。 */
function target(pathname) {
  return new URL(pathname, base).toString()
}

async function fetchWithTimeout(url, init = {}) {
  return await fetch(url, { ...init, signal: AbortSignal.timeout(timeoutMs), redirect: 'manual' })
}

function headerOf(response, name) {
  return response.headers.get(name)
}

/** 段 1：探活。不通就直接以退出码 2 收场（"断言失败"与"没连上"要分得清）。 */
if (runner.begin('probe')) {
  let home = null
  try {
    home = await fetchWithTimeout(target('./'))
  } catch (error) {
    console.error(`目标不可达：${target('./')} → ${error instanceof Error ? error.message : String(error)}`)
    process.exit(2)
  }

  if (home.status !== 200) {
    console.error(`目标返回 ${home.status}：${target('./')}（地址 / 前缀写错？）`)
    process.exit(2)
  }

  const html = await home.text()
  check('首页可达且是 HTML', html.includes('<html'), `status=${home.status}`)
}

const authority = base.host
let assetUrl = null
let shellUrl = null
/** 目标是不是挂在反代后面（从 Server 头判）：决定"反代那层上限"这一条是判还是跳过。 */
let behindReverseProxy = false

/** 段 2：安全响应头与内容安全策略。 */
if (runner.begin('headers')) {
  const home = await fetchWithTimeout(target('./'))
  const health = await fetchWithTimeout(target('healthz'))
  behindReverseProxy = (headerOf(home, 'server') ?? '').toLowerCase().includes('nginx')

  for (const [name, expected] of EXPECTED_HEADERS) {
    check(`${name} 在场`, headerOf(home, name) === expected, `实测 ${headerOf(home, name) ?? '（缺失）'}`)
  }

  const permissions = headerOf(home, 'permissions-policy') ?? ''
  const missing = REQUIRED_PERMISSIONS.filter((token) => !permissions.includes(token))
  check('Permissions-Policy 关掉了摄像头 / 麦克风 / 定位', missing.length === 0, permissions || '（缺失）')

  // /healthz 也是接口响应，同样必须带头（漏了接口那一份等于没做）。
  check('接口响应也带头（/healthz）', headerOf(health, 'x-content-type-options') === 'nosniff')

  const csp = headerOf(home, 'content-security-policy') ?? ''
  check('CSP 默认只允许本站', csp.includes("default-src 'self'"), csp.slice(0, 120))
  check('CSP 放行百科角色图（热链，D-0007）', csp.includes(`img-src 'self' ${WIKI_ORIGIN}`))
  check(
    'CSP 显式允许本站 WebSocket（部分浏览器不把 self 解析到 ws）',
    csp.includes(`connect-src 'self' ws://${authority} wss://${authority}`),
    `authority=${authority}`,
  )
  check('CSP 不放内联脚本 / 样式', !csp.includes('unsafe-inline') && !csp.includes('unsafe-eval'))
  check('CSP 禁止被嵌套（frame-ancestors none）', csp.includes("frame-ancestors 'none'"))

  const hsts = headerOf(home, 'strict-transport-security')
  if (base.protocol === 'https:') {
    check('HTTPS 上有 HSTS', typeof hsts === 'string' && hsts.includes('max-age='), hsts ?? '（缺失）')
  } else {
    check('明文实例上不发 HSTS（发了也没有意义）', hsts === null, hsts ?? '（未发）')
  }

  const server = headerOf(home, 'server')
  check(
    'Server 头不暴露版本号（server_tokens off）',
    server === null || !server.includes('/'),
    server ?? '（无 Server 头）',
  )
}

/** 段 3：缓存口径。 */
if (runner.begin('cache')) {
  const home = await fetchWithTimeout(target('./'))
  shellUrl = target('./')
  const html = await home.text()
  const match = html.match(/(?:src|href)="([^"]*\/assets\/[^"]+)"/)
  if (match === null) {
    check('页面里能找到带哈希的产物引用', false, '从 index.html 里找不到 /assets/ 引用')
  } else {
    assetUrl = new URL(match[1], base).toString()
    const asset = await fetchWithTimeout(assetUrl)
    check('带哈希的产物：Cache-Control 长缓存 + immutable', headerOf(asset, 'cache-control') === IMMUTABLE, headerOf(asset, 'cache-control') ?? '（缺失）')
    check('带哈希的产物：真的取得到', asset.status === 200, `status=${asset.status} ${assetUrl}`)
  }

  check('页面外壳：no-cache（先回源校验）', headerOf(home, 'cache-control') === REVALIDATE, headerOf(home, 'cache-control') ?? '（缺失）')
}

/** 段 4：请求体上限。 */
if (runner.begin('limits')) {
  const negotiate = target('hub/account/negotiate?negotiateVersion=1')

  // 应用侧：按声明长度提前拒（协商端点根本不读体，只靠 Kestrel 的读取上限拦不住它）。
  const appLevel = await fetchWithTimeout(negotiate, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: 'x'.repeat(300 * 1024),
  })
  check('300 KB（应用侧上限 256 KB）被拒', appLevel.status === 413, `status=${appLevel.status}`)
  check(
    '被拒的响应也带安全头（接线顺序：响应头在前，上限闸在后）',
    headerOf(appLevel, 'x-content-type-options') === 'nosniff',
    headerOf(appLevel, 'x-content-type-options') ?? '（缺失）',
  )

  // 反代侧：nginx 按 Content-Length 在转发之前就回 413（本机直跑宿主时没有这一层，不判也不假绿）。
  if (!behindReverseProxy) {
    console.log('  [SKIP] 2 MB（反代上限 1 MB）→ 本机直跑宿主，没有反代这一层；部署实例上才是判据')
  } else {
    const proxyLevel = await fetchWithTimeout(negotiate, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: 'x'.repeat(2 * 1024 * 1024),
    })
    check(
      '2 MB（反代上限 1 MB）被拒，且是**反代**拒的',
      proxyLevel.status === 413 && (headerOf(proxyLevel, 'server') ?? '').toLowerCase().includes('nginx'),
      `status=${proxyLevel.status} server=${headerOf(proxyLevel, 'server') ?? '（无）'}`,
    )
  }
}

/** 段 5：账号入口限速。 */
if (runner.begin('throttle')) {
  const stamp = Date.now().toString(36)
  // 长度与字符面都符合登录名口径（2–24 字符、无空白）；它**不存在**，所以不会建号、不留数据。
  const username = `thr-${stamp}`
  const other = `thr-b-${stamp}`
  const wrongPassword = `wrong-${stamp}`

  const connection = new signalR.HubConnectionBuilder()
    .withUrl(target('hub/account'))
    .configureLogging(signalR.LogLevel.Error)
    .build()

  try {
    await connection.start()

    const codes = []
    for (let attempt = 0; attempt < 5; attempt += 1) {
      const result = await connection.invoke('Login', username, wrongPassword)
      codes.push(result.code)
    }

    check('前 5 次错误口令都被受理并拒绝（invalid_credentials）', codes.every((code) => code === 'invalid_credentials'), codes.join(','))

    const blocked = await connection.invoke('Login', username, wrongPassword)
    check(
      '第 6 次被限速（too_many_attempts）',
      blocked.code === 'too_many_attempts',
      `code=${blocked.code} message=${blocked.message ?? ''}`,
    )
    check('限速文案给出等待时间（说人话）', typeof blocked.message === 'string' && blocked.message.includes('秒后再试'), blocked.message ?? '')

    const neighbour = await connection.invoke('Login', other, wrongPassword)
    check(
      '换个登录名不受牵连（限的是"这个来源在猜这个登录名"）',
      neighbour.code === 'invalid_credentials',
      `code=${neighbour.code}${neighbour.code === 'too_many_attempts' ? '（同一来源 5 分钟内的失败额度已用尽？等窗口过去再跑）' : ''}`,
    )
  } finally {
    await connection.stop().catch(() => {})
  }
}

runner.reportTimings()
checker.report()
process.exit(checker.results.some((result) => result.outcome === 'fail') ? 1 : 0)

/** 解析参数；--base-url 必须是带末尾斜杠的绝对地址（前缀拼接全靠它）。 */
function parseArguments(argv) {
  const parsed = {
    baseUrl: new URL('http://127.0.0.1:5080/'),
    timeoutSeconds: 30,
    only: [],
    from: undefined,
    listSections: false,
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
        parsed.timeoutSeconds = Number.parseInt(value ?? '', 10)
        if (!Number.isInteger(parsed.timeoutSeconds) || parsed.timeoutSeconds < 1) {
          throw new Error(`--timeout 必须是正整数秒：${value}`)
        }

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
        console.log('用法：node tools/verify-transport-hardening.mjs [--base-url http://主机/前缀/] [--timeout 30] [--only 段名] [--from 段名] [--list-sections]')
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
