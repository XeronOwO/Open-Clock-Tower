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
 *   3. 席位票据的读法收在一处（`readSeatTickets`）——那张票还没退场，D-0027 明确留给了下一张票。
 *
 * M1（D-0029）之后"回来"这条路径又变了一次：账号会话进 `sessionStorage`，**刷新即自动回到原席**
 * （`returnToSeat` 不再重新登录）；而**新标签页**仍必须重新登录（`openFreshTab`）——
 * 两条合起来才是完整约束：刷新不掉登录，但关掉标签页就清。
 *
 * 依赖的界面锚点（改界面时以这里为准）：`account-tab-register` / `account-username` /
 * `account-display-name` / `account-password` / `account-register` / `account-profile` /
 * `open-table-name` / `open-table-seats` / `open-table-submit` / `[data-my-table]` /
 * `host-enter` / `grimoire` / `player-lobby` / `[data-table]` / `[data-seat]` / `player-seat`。
 *
 * 入口地址：说书人面 `/storyteller`（空地址是首页）、玩家面 `/play`。
 */
import { DatabaseSync } from 'node:sqlite'

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
 * 读一桌的席位票据（**仍然直读库**：席位票据本身没动，见 D-0027 的残余）。
 * @param {string} databasePath SQLite 路径。
 * @param {string} gameId 哪一桌。
 * @returns {{seat: number, ticket: string}[]} 按席位号升序。
 */
export function readSeatTickets(databasePath, gameId) {
  const database = new DatabaseSync(databasePath)
  try {
    const row = database.prepare('SELECT SeatsJson FROM Games WHERE GameId = ?').get(gameId)
    if (row === undefined || typeof row.SeatsJson !== 'string') {
      throw new Error(`数据库里没有这一桌的席位票据：game=${gameId}（Games.SeatsJson）`)
    }

    const parsed = JSON.parse(row.SeatsJson)
    if (!Array.isArray(parsed) || parsed.length === 0) {
      throw new Error('席位票据 JSON 形状不可识别')
    }

    return parsed
      .map((item) => ({ seat: seatNumberOf(item?.seat), ticket: String(item?.ticket ?? '') }))
      .filter((item) => Number.isFinite(item.seat) && item.ticket.length > 0)
      .sort((left, right) => left.seat - right.seat)
  } finally {
    database.close()
  }
}

/**
 * 开一桌并以它的开桌账号进主持台（装置的开场动作）。
 *
 * 全程走界面：门 → 注册 → 开一桌 → 「我主持的桌」里点「进主持台」。
 * 返回的 `seatTickets` 供装置接着让玩家入座。
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
    seatTickets: readSeatTickets(options.databasePath, gameId),
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
 * 与 `seatByAccount` 的分工：大厅点得动的桌用它；桌已开局 / 已锁桌（比如中途到场的旅行者）用这一条——
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

/** 在这张登录卡上注册并登录（登录 / 注册是两个页签，新用户先切到「注册」）。 */
async function registerOnGate(page, account) {
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
