/**
 * 装置入场助手（D-0027）：**账号是唯一的身份证**。
 *
 * 票据退场之前，每一个需要说书人身份的装置都这么开场：
 *   直读 SQLite 的 `Games.StorytellerTicket` → 填进输入框 → 点「加入」。
 * 那条路随 D-0027 整个删除（列本身也没了：新库不建它，老库由启动守卫清掉），于是装置改成
 * **真实用法的那条路**：注册一个夹具账号 → 开一桌 → 用这个账号进主持台。
 *
 * 这样改有三个好处，都是这次改造想要的姿态：
 *   1. 装置不再依赖任何"从库里掏凭据"的旁路，跑的就是人跑的那条路；
 *   2. 说书人身份跟着**账号**走，所以"换设备还能回来"从此是被装置持续验证的行为；
 *   3. 入席凭据的取法收在一处（`issueInviteCode` / `issueInviteCodeViaHub`）——
 *      从前是"直读库里的席位票据"（`readSeatTickets`），D-0038 之后**库里读不到凭据了**
 *      （邀请码只存哈希、明文只在签发那一次出现），于是装置改成"让说书人签发一次"，
 *      与真人按下那个按钮走的是同一条路。库那边只剩席位名单（`readSeatNumbers`）。
 *
 * M1（D-0029）之后"回来"这条路径又变了一次：账号会话进 `sessionStorage`，**刷新即自动回到原席**
 * （`returnToSeat` 不再重新登录）；而**新标签页**仍必须重新登录（`openFreshTab`）——
 * 两条合起来才是完整约束：刷新不掉登录，但关掉标签页就清。
 *
 * 依赖的界面锚点（改界面时以这里为准）：`account-tab-register` / `account-username` /
 * `account-display-name` / `account-password` / `account-register` / `account-profile` /
 * `open-table-name` / `open-table-seats` / `open-table-submit` / `[data-my-table]` /
 * `host-enter` / `grimoire` / `player-lobby` / `[data-table]` / `[data-seat]` / `player-seat`
 * / `invite-seat` / `invite-issue` / `invite-issued`（`data-seat` + `.mono`；D-0038 的签发入口，
 * 每台装置开桌之后都要用它拿码——库里读不到）。
 *
 * 访问模式与旅行者离场那一批（D-0037）的锚点也登记在这里——`docs/acceptance/devices.md` §3 指明
 * "锚点如改名要同步本清单"，而它们不归本模块使用（用它们的是 `verify-table-access.mjs`，本模块只当登记处）：
 * 玩家侧 `player-table-access`（`data-invite-only`）/ `player-departed` / `departure-request` /
 * `departure-note` / `departure-pending` / `departure-ruling`（`data-approved`）；
 * 说书人侧 `table-access`（`data-invite-only`）/ `table-access-toggle` / `traveller-character` /
 * `traveller-join` / `traveller-issued`（`data-seat`）/ `traveller-departures` 与逐行的
 * `[data-departure-seat="N"]` / `departure-approve` / `departure-reject`；
 * 大厅行 `li[data-table][data-invite-only]`（席位按钮仍是 `[data-seat]`，`disabled` 即点不动）。
 *
 * 线级探针（不经界面的 Node SignalR 客户端）另有两条入口，都只走账号 Hub 的公开方法：
 * `registerProbeAccount`（`Register`）与 `loginProbeAccount`（`Login`）——它们拿到的账号会话要交给
 * **`JoinByInviteCode`**（`/hub/game`）才坐得进席位；"没有账号、只凭票据入座"的 `JoinSeat` 已随 D-0037
 * 整个删除，探针也不例外。
 *
 * 入口地址：说书人面 `/storyteller`（空地址是首页）、玩家面 `/play`。
 */
import { DatabaseSync } from 'node:sqlite'
import { readTextBounded } from './bounded-text.mjs'

/** 主路径动作的超时（注册 / 开桌 / 入座都是一次服务端往返，本机实测 < 1s）。 */
const ACTION_TIMEOUT_MS = 20_000

/**
 * 夹具账号名的长度上限：服务端口径是 **24 字符**（`UsernameText.MaxLength`）。
 * 超了不是"注册失败"这么简单——服务端只回 `invalid_username`，装置会卡在"等资料区出现"，
 * 所以这里**造名字的时候就守住**，并在越界时直接抛错（把配置错误暴露在装置侧）。
 */
const USERNAME_MAX_LENGTH = 24

/** 同一次进程里递增，保证同一装置多次建桌 / 入座不会撞名。 */
let nameCounter = 0

/**
 * 读一桌的**席位名单**（D-0038 起 `Games.SeatsJson` 只存席位号：`[1,2,3]`）。
 *
 * ⚠️ 这里**读不到邀请码**，也不该去读：邀请码只存哈希、明文只在签发那一次出现。
 * 取码只有一条路——让说书人签发（界面上是 `issueInviteCode`，线级是 `issueInviteCodeViaHub`）。
 *
 * @param {string} databasePath SQLite 路径。
 * @param {string} gameId 哪一桌。
 * @returns {number[]} 席位号，升序。
 */
export function readSeatNumbers(databasePath, gameId) {
  const database = new DatabaseSync(databasePath)
  try {
    const row = database.prepare('SELECT SeatsJson FROM Games WHERE GameId = ?').get(gameId)
    if (row === undefined || typeof row.SeatsJson !== 'string') {
      throw new Error(`数据库里没有这一桌的席位名单：game=${gameId}（Games.SeatsJson）`)
    }

    const parsed = JSON.parse(row.SeatsJson)
    if (!Array.isArray(parsed)) {
      throw new Error(`席位名单 JSON 形状不可识别：${row.SeatsJson}`)
    }

    return parsed
      .map((item) => seatNumberOf(item))
      .filter((seat) => Number.isFinite(seat))
      .sort((left, right) => left - right)
  } finally {
    database.close()
  }
}

/**
 * 让说书人面板为某一席**签发邀请码**（D-0038），读出转交的那一串（`桌标识:席位邀请码`）。
 *
 * 这是装置取码的正路之一（另一条是线级的 {@link issueInviteCodeViaHub}）：面板上那一段就是真用法——
 * 填席位号 → 点签发 → 把 `.mono` 里那一串交给玩家。**刷新 / 再查都拿不回来**，所以必须当场读。
 *
 * @param {import('playwright').Page} page 说书人那一页（停在主持台）。
 * @param {number} seat 哪一席。
 * @returns {Promise<string>} 完整邀请码（含桌标识）。
 */
export async function issueInviteCode(page, seat) {
  const issued = page.locator(`[data-testid="invite-issued"][data-seat="${seat}"]`)

  // 点之前先记下这一席**当前**的读数。同一席再签一次是"轮换"：元素留在原地、只换里面的文本，
  // 所以"等元素出现"会在点下去的**瞬间**就满足条件并返回**上一枚**（真机实测踩到：装置于是把
  // "新签的那一枚"判成了旧码，自己造出一条假红）。判据取"读数变了"。
  //
  // 读法走 `readTextBounded`：轮询里的无界读会白等满 Playwright 的 30 秒默认超时
  // （见 `lib/bounded-text.mjs` 与 done/device-poll-innertext-unbounded-wait.md）。
  const before = (await issued.count()) > 0 ? (await readTextBounded(issued.locator('.mono'), 500)).trim() : ''

  await page.getByTestId('invite-seat').fill(String(seat))
  await page.getByTestId('invite-issue').click()

  const deadline = Date.now() + ACTION_TIMEOUT_MS
  for (;;) {
    if ((await issued.count()) > 0) {
      const code = (await readTextBounded(issued.locator('.mono'), 500)).trim()
      if (code.length > 0 && code !== before) {
        return code
      }
    }

    if (Date.now() >= deadline) {
      throw new Error(
        `面板没有给出 ${seat} 号席的邀请码：${ACTION_TIMEOUT_MS / 1000}s 内读数没有变（上一枚「${before}」）`,
      )
    }

    await sleep(120)
  }
}

/**
 * 线级等价物：直接调 `IssueSeatInvitation`（说书人连接 + 连接凭据）。
 *
 * 无浏览器的装置（如 `verify-zero-trust`）走这条；它就是面板那一下按下去的同一个方法。
 * 返回**旧形状** `{seat, ticket}`（`ticket` = 冒号之后那一段），因为各装置一直按这个形状往下传。
 *
 * @param {{invoke: (method: string, ...args: unknown[]) => Promise<unknown>}} connection 游戏 Hub 连接。
 * @param {string} credential 说书人连接凭据。
 * @param {number} seat 哪一席。
 */
export async function issueInviteCodeViaHub(connection, credential, seat) {
  const issued = await connection.invoke('IssueSeatInvitation', credential, seat)
  const inviteCode = String(issued?.inviteCode ?? '')
  if (inviteCode.length === 0) {
    throw new Error(`${seat} 号席的邀请码没有签出来：${JSON.stringify(issued)}`)
  }

  return { seat, ticket: inviteCode.slice(inviteCode.indexOf(':') + 1) }
}

/**
 * 逐席签发邀请码（1..seatCount）：开桌之后的常规动作，也是各装置拿到入席凭据的唯一来源。
 *
 * @param {import('playwright').Page} page 说书人那一页。
 * @param {number} seatCount 席位数量。
 * @returns {Promise<{seat: number, ticket: string}[]>} 按席位号升序（`ticket` = 冒号之后那一段）。
 */
export async function issueSeatInviteCodes(page, seatCount) {
  const issued = []
  for (let seat = 1; seat <= seatCount; seat += 1) {
    const code = await issueInviteCode(page, seat)
    issued.push({ seat, ticket: code.slice(code.indexOf(':') + 1) })
  }

  return issued
}

/**
 * 开一桌并以它的开桌账号进主持台（装置的开场动作）。
 *
 * 全程走界面：门 → 注册 → 开一桌 → 「我主持的桌」里点「进主持台」**→ 逐席签发邀请码**（D-0038）。
 * 返回的 `seatTickets` 供装置接着让玩家入座；它是**当场签发出来的**（库里只有哈希，读不回来）。
 *
 * @param {import('playwright').Page} page 说书人那一页（未打开也行，本函数会打开）。
 * @param {{frontUrl: string, databasePath: string, seats: number, serverUrl?: string, suffix?: string, name?: string}} options
 * @returns {Promise<{gameId: string, name: string, username: string, displayName: string, password: string, seatTickets: {seat: number, ticket: string}[], hubUrl?: string}>}
 */
export async function openTableAndHost(page, options) {
  const suffix = options.suffix ?? 'table'
  const host = fixtureAccount('host-', suffix, '说书人')
  const { username, displayName, password } = host
  const name = options.name ?? `夹具桌${suffix}`

  await page.goto(`${options.frontUrl}/storyteller`, { waitUntil: 'domcontentloaded' })
  await registerOnGate(page, { username, displayName, password })

  await waitFor(page.getByTestId('open-table-submit'), 1)
  await page.getByTestId('open-table-name').fill(name)
  await page.getByTestId('open-table-seats').fill(String(options.seats))
  await page.getByTestId('open-table-submit').click()

  const row = page.locator('[data-my-table]').first()
  await waitFor(row, 1)
  const gameId = await row.getAttribute('data-my-table')
  if (gameId === null || gameId.length === 0) {
    throw new Error('开桌之后「我主持的桌」里那一行没有桌标识（data-my-table）')
  }

  await row.getByTestId('host-enter').click()
  await waitFor(page.getByTestId('grimoire'), 1)

  return {
    gameId,
    name,
    username,
    displayName,
    password,
    seatTickets: await issueSeatInviteCodes(page, options.seats),
    // Node SignalR 客户端（装置里的"线级探针"）不再能连"没声明桌"的地址（D-0027），
    // 所以顺手给出这条连接该用的地址——装置把它赋给 hubUrl 即可。
    hubUrl:
      options.serverUrl === undefined
        ? undefined
        : `${options.serverUrl}/hub/game?gameId=${encodeURIComponent(gameId)}`,
  }
}

/**
 * 用**邀请码**入座（说书人中途签发的席位 / 换设备兜底）。
 *
 * 与 `seatByAccount` 的分工：大厅点得动的桌用它；桌已开局 / 已是邀请制（比如中途到场的旅行者）用这一条——
 * 码就是 `桌标识:席位票据`，与说书人面板上显示的那一串完全一致（`[data-testid="traveller-issued"]`）。
 *
 * @param {import('playwright').Page} page
 * @param {{frontUrl: string, code: string, suffix?: string, account?: {username: string, displayName?: string, password: string}}} options
 *   `account` 给了就用这个既有账号**登录**（换设备 / 刷新回来），否则注册一个新账号。
 * @returns {Promise<{username: string, displayName: string, password: string}>}
 */
export async function seatByInviteCode(page, options) {
  const account =
    options.account === undefined
      ? fixtureAccount('seat-', options.suffix ?? 'invite')
      : {
          username: options.account.username,
          displayName: options.account.displayName ?? options.account.username,
          password: options.account.password,
        }

  await page.goto(`${options.frontUrl}/play`, { waitUntil: 'domcontentloaded' })
  if (options.account === undefined) {
    await registerOnGate(page, account)
  } else {
    await loginOnGate(page, account)
  }

  const invite = page.getByTestId('seat-invite')
  await waitFor(invite, 1)
  const opened = await invite.evaluate((element) => element.hasAttribute('open')).catch(() => false)
  if (!opened) {
    await invite.locator('summary').click()
  }

  await page.getByTestId('seat-invite-code').fill(options.code)
  await page.getByTestId('seat-invite-join').click()
  await waitFor(page.getByTestId('player-seat'), 1)

  return account
}

/**
 * 刷新回来：**刷新页面即自动回到原来那一席**（M1 / D-0029）。
 *
 * 上一版这条路是"刷新 → 用同一个账号重新登录 → 点「回到我的座位」"——账号会话当时只活在网关内存里。
 * M1 之后凭据进了 `sessionStorage`，刷新时前端自己向服务端确认（`Resume`）并按位置坐回原位；
 * 装置要跑的正是这条真实路径：**不填登录卡、不点席位**，刷新完就该在原席上。
 *
 * 反方向（"关标签页即清"）由 `openFreshTab` 断言——两条合起来才是完整的约束。
 *
 * @param {import('playwright').Page} page
 * @param {{frontUrl: string}} options 账号参数已经不需要了（不再重新登录）；签名保持不变，调用点不动。
 */
export async function returnToSeat(page, options) {
  // **强制整页重载**：这条路线的语义就是"刷新一次"。
  // 目标地址与当前地址往往只差一个路径段（或只差查询串），那时 `goto` 可能属于同文档导航、
  // 文档不重载，页面还停在"已连接"的状态上——那样就根本没验到"刷新之后能不能回来"。
  await page.goto(`${options.frontUrl}/play`, { waitUntil: 'domcontentloaded' })
  await page.reload({ waitUntil: 'domcontentloaded' })

  // 主路径（M1）：刷新即自动按位置坐回去（`JoinTable`）——先给它一个有界窗口。
  const auto = await page
    .getByTestId('player-seat')
    .waitFor({ timeout: 8_000 })
    .then(() => true)
    .catch(() => false)
  if (auto) {
    return
  }

  // 兜底：位置已经被清掉（上一次入座失败时就该忘掉它）就走 D-0027 那条路——
  // 大厅里自己那一格**始终点得动**。两条都是真实路径，这里判的是"最终回到了原席"；
  // "刷新即自动回座"本身的强断言在 `verify-accounts` 与 `verify-live-open-table` 里，不靠这条兜底。
  const mine = page.locator('[data-seat-mine="true"]').first()
  await waitFor(mine, 1)
  await mine.click()
  await waitFor(page.getByTestId('player-seat'), 1)
}

/**
 * **新标签页**打开玩家面：`sessionStorage` 是每个标签页一份的，所以这里必须重新登录
 * （M1 / D-0029 的另一半：关标签页即清）。
 *
 * 这条断言看着"逆着功能走"，其实是在钉住持久化的边界：凭据没有跨标签页共享，
 * 也没有落到 `localStorage` 那种长期驻留的地方——"刷新不掉登录"不是靠
 * "把凭据放进谁都能读的共享存储"换来的。
 *
 * @param {import('playwright').BrowserContext} context 与原页面同一个浏览器上下文（cookie 等仍共享）。
 * @param {{frontUrl: string, username: string, password: string, displayName?: string}} options
 * @returns {Promise<import('playwright').Page>} 新标签页（已重新登录）。
 */
export async function openFreshTab(context, options) {
  const page = await context.newPage()
  await page.goto(`${options.frontUrl}/play`, { waitUntil: 'domcontentloaded' })
  await waitFor(page.getByTestId('account-gate'), 1)
  await loginOnGate(page, {
    username: options.username,
    displayName: options.displayName ?? options.username,
    password: options.password,
  })
  return page
}

/**
 * 用**账号**在玩家面入座（主路径：登录后从大厅挑一个空席位）。
 *
 * @param {import('playwright').Page} page 这个席位的页面（未打开也行，本函数会打开玩家面）。
 * @param {{frontUrl: string, gameId: string, seat: number, suffix?: string}} options
 * @returns {Promise<{username: string, displayName: string, password: string}>}
 */
export async function seatByAccount(page, options) {
  const account = fixtureAccount('seat-', options.suffix ?? 'seat')

  await page.goto(`${options.frontUrl}/play`, { waitUntil: 'domcontentloaded' })
  await registerOnGate(page, account)

  const row = page.locator(`[data-table="${options.gameId}"]`)
  await waitFor(row, 1)
  await row.locator(`[data-seat="${options.gameId}-${options.seat}"]`).click()
  await waitFor(page.getByTestId('player-seat'), 1)

  return account
}

/**
 * 为线级探针注册一个夹具账号并登录，返回账号会话（D-0037：入座必须登录，探针也不例外）。
 * 每个席位一个账号：服务端"一账号一局只坐一席"，共用账号会被拒。
 * @param {object} signalR 调用方已加载的 @microsoft/signalr 模块（各装置自己 requireFromWeb 得来）
 * @param {string} accountHubUrl `${serverUrl}/hub/account`
 * @param {string} suffix 夹具后缀（登录名会带序号，见 fixtureAccount）
 * @returns {Promise<{username: string, displayName: string, password: string, accountSession: string}>}
 */
export async function registerProbeAccount(signalR, accountHubUrl, suffix) {
  return issueProbeSession(
    signalR,
    accountHubUrl,
    fixtureAccount('probe-', suffix),
    (connection, account) => connection.invoke('Register', account.username, account.displayName, account.password),
    '注册',
  )
}

/**
 * 为线级探针**登录一个既有夹具账号**，返回账号会话。
 *
 * 为什么需要这一条：一席只属于一个账号，所以当探针要坐的席位**已经被某个浏览器页认领**时
 * （`verify-mathematician` 的无关席位就是这种：页与探针共用 5 号），探针不能另注册一个账号——
 * 那会被"席位已经由其他账号认领"挡住。用原账号再登录一次即可：同一账号多会话并存，
 * 探针这条连接与页那条是同一账号的两台"设备"，入的还是自己那一席。
 *
 * @param {object} signalR 调用方已加载的 @microsoft/signalr 模块。
 * @param {string} accountHubUrl `${serverUrl}/hub/account`
 * @param {{username: string, displayName?: string, password: string}} account 既有夹具账号（如 `seatByAccount` 的返回值）。
 * @returns {Promise<{username: string, displayName: string, password: string, accountSession: string}>}
 */
export async function loginProbeAccount(signalR, accountHubUrl, account) {
  return issueProbeSession(
    signalR,
    accountHubUrl,
    {
      username: account.username,
      displayName: account.displayName ?? account.username,
      password: account.password,
    },
    (connection, own) => connection.invoke('Login', own.username, own.password),
    '登录',
  )
}

/**
 * 注册 / 登录的公共部分：连账号 Hub → 调用 → **断言成功** → 关连接。
 *
 * 失败一律抛错并带上服务端回的 code / message：若静默返回空会话，装置会拿着空串去入座，
 * 最后以"某条断言红了"的形式出现——那是把配置错误藏进了别处（与 `fixtureAccount` 同一个理由）。
 *
 * 连接用完即关：注册 / 登录是一次性动作，账号会话不绑连接（`AccountSessionRegistry`），
 * 留着它只会让装置收尾时多一条要关的东西。
 */
async function issueProbeSession(signalR, accountHubUrl, account, call, action) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(accountHubUrl)
    .configureLogging(signalR.LogLevel.None)
    .build()
  try {
    await connection.start()
    const result = await call(connection, account)
    if (result?.ok !== true || typeof result.accountSession !== 'string' || result.accountSession.length === 0) {
      throw new Error(
        `夹具账号${action}失败：code=${result?.code ?? '未知'} message=${result?.message ?? '（无）'}（${account.username}）`,
      )
    }

    return {
      username: account.username,
      // 玩家名以**服务端回执**为准：席位名投影用的是服务端那一份，装置拿它对断言才同源。
      displayName:
        typeof result.displayName === 'string' && result.displayName.length > 0
          ? result.displayName
          : account.displayName,
      password: account.password,
      accountSession: result.accountSession,
    }
  } finally {
    await connection.stop().catch(() => {})
  }
}

/**
 * 在这张登录卡上注册并登录（登录 / 注册是两个页签，新用户先切到「注册」）。
 *
 * **导出给"注册完就停在大厅"的场景**（`verify-table-access.mjs` 的大厅观察员）：本模块其余入口
 * 全都是"注册 / 登录之后立刻入座"，而"大厅里这一桌长什么样"只有停在登录后大厅的人才看得到
 * （邀请制那一行仍然列出、席位按钮点不动）。复用它而不是在装置里重写一遍：注册这条路的锚点
 * 只有一个事实来源，重写必然与这里漂移。
 *
 * @param {import('playwright').Page} page 玩家面（本函数不做跳转，调用方自己 goto）。
 * @param {{username: string, displayName: string, password: string}} account 夹具账号。
 */
export async function registerOnGate(page, account) {
  const registerTab = page.getByTestId('account-tab-register')
  if ((await registerTab.count()) > 0) {
    await registerTab.click()
  }

  await waitFor(page.getByTestId('account-display-name'), 1)
  await page.getByTestId('account-username').fill(account.username)
  await page.getByTestId('account-display-name').fill(account.displayName)
  await page.getByTestId('account-password').fill(account.password)
  await page.getByTestId('account-register').click()

  await waitFor(page.getByTestId('account-profile'), 1)
}

/** 用**既有账号**登录（换设备 / 刷新回来时用；注册与登录是两个页签，默认就在「登录」）。 */
async function loginOnGate(page, account) {
  const loginTab = page.getByTestId('account-tab-login')
  if ((await loginTab.count()) > 0) {
    await loginTab.click()
  }

  await waitFor(page.getByTestId('account-username'), 1)
  await page.getByTestId('account-username').fill(account.username)
  await page.getByTestId('account-password').fill(account.password)
  await page.getByTestId('account-login').click()

  await waitFor(page.getByTestId('account-profile'), 1)
}

/** 等定位器数量达到期望值；超时抛错（这是装置开场的前置，失败要立刻看得见）。 */
async function waitFor(locator, expected, timeoutMs = ACTION_TIMEOUT_MS) {
  const deadline = Date.now() + timeoutMs
  for (;;) {
    if ((await locator.count().catch(() => 0)) >= expected) {
      return
    }

    if (Date.now() >= deadline) {
      throw new Error(`等不到元素（${expected} 个）：${String(locator)}`)
    }

    await sleep(120)
  }
}

/**
 * 造一个夹具账号：`登录名 + 玩家名 + 口令`，登录名**在越界前就抛错**。
 *
 * 加序号是为了同一个装置在一次运行里开两张桌 / 坐多个席位时不撞名（服务端会回 `username_taken`，
 * 而那种失败会以"等不到资料区"的形式出现，很难查）。
 */
function fixtureAccount(prefix, suffix, role = '玩家') {
  nameCounter += 1
  const username = `${prefix}${suffix}-${nameCounter}`
  if (username.length > USERNAME_MAX_LENGTH) {
    throw new Error(
      `夹具登录名超过 ${USERNAME_MAX_LENGTH} 字符上限：${username}（长 ${username.length}）——把 suffix 改短`,
    )
  }

  const displayName = `夹具${role}${suffix}`
  return {
    username,
    displayName: displayName.length <= USERNAME_MAX_LENGTH ? displayName : displayName.slice(0, USERNAME_MAX_LENGTH),
    password: `fixture-pw-${suffix}`,
  }
}

/** `SeatId` 在 JSON 里是 `{ value: N }`；两种形态都认。 */
function seatNumberOf(raw) {
  if (typeof raw === 'number') {
    return raw
  }

  if (raw !== null && typeof raw === 'object' && typeof raw.value === 'number') {
    return raw.value
  }

  return Number.parseInt(String(raw ?? ''), 10)
}

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}
