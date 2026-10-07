<script setup lang="ts">
/**
 * 玩家视图（同一 SPA 的另一套视图，D-0004）。
 *
 * 玩家只能看到服务端下发给他的东西：自己的席位、当前大阶段、发给自己的请求与信息类结果。
 * 看板 / 状态账 / 计划进度一概不下发——所以这里也不会有对应的代码路径（D-0013 §5）。
 *
 * M1（D-0029）：登录态与"我在哪一席"由 `sessionStorage` 承载，**刷新后自动坐回原席**；
 * 坐不回去（席位被解除绑定 / 桌没了 / 会话失效）时如实说明并清掉位置，不装作回来了。
 * 入座只有一条实现（`enterSeat`）——大厅点席位与刷新自动回座走的是同一条路。
 */
import type {
  DepartureRulingDto,
  GameOutcomeDto,
  InformationResultDto,
  JugglerGuessDto,
  KlutzChoiceDto,
  OperationRequestDto,
  PlayerDayDto,
  PlayerViewDto,
  ReplayViewDto,
} from '@/contracts/game'
import { alignmentLabelOf, characterLabelOf, labelOf, voidReasonLabelOf } from '@/display/labels'
import { optionDisplayOf, seatDisplayOf } from '@/display/format'
import HelpTip from '@/features/common/HelpTip.vue'
import type { LobbyTable } from '@/services/accountGateway'
import * as session from '@/services/accountSession'
import { PlayerGateway, type PlayerCallbacks } from '@/services/playerGateway'
import { newIdempotencyKey } from '@/services/idempotency'
import type { GatewayState } from '@/services/connectionState'
import AccountGate from '@/features/account/AccountGate.vue'
import AccountPanel from '@/features/account/AccountPanel.vue'
import PlayerDayPanel from '@/features/player/PlayerDayPanel.vue'
import ReplayPanel from '@/features/replay/ReplayPanel.vue'
import { computed, onBeforeUnmount, ref, watch } from 'vue'

const view = ref<PlayerViewDto | null>(null)
const pending = ref<OperationRequestDto | null>(null)
/** 白天投影（公开事实 + 自己的权限位）；服务端还没开过白天时为 null。 */
const day = ref<PlayerDayDto | null>(null)
const informationResults = ref<InformationResultDto[]>([])
/** 胜负结论；null = 游戏仍在进行（R-0024：结束后对全体玩家一致可见）。 */
const outcome = ref<GameOutcomeDto | null>(null)
/** 呆瓜的公开选择记录（含跳过；R-0027）。 */
const klutzChoices = ref<KlutzChoiceDto[]>([])
const connectionState = ref<GatewayState>('disconnected')
const diagnostics = ref<string[]>([])
const submitting = ref(false)
const selectedOption = ref('')
/** 两维选择（R-0021）的第二维取值；单维请求下保持空串。 */
const selectedSecondary = ref('')
const note = ref('')
/** 最近一次请求是怎么结束的（作废原因 / 说书人代填）；新请求到达即清空。 */
const settledNote = ref('')

/** 艺术家提问（R-0040）：服务端只在"白天 + 本人是艺术家 + 还没用过"时下发 true。 */
const canAskArtistQuestion = ref(false)
/** 本人进行中的提问全文；null = 没有在等回答。 */
const pendingQuestion = ref<string | null>(null)
const artistQuestion = ref('')
const artistQuestionSubmitting = ref(false)

/** 博学者要两条信息（R-0057）：服务端只在"白天 + 本人是博学者 + 今天还没要过"时下发 true。 */
const canAskSavantQuestion = ref(false)
/** 本人有一条博学者提问在等说书人给两条信息（等待态，服务端下发）。 */
const awaitingSavantQuestion = ref(false)
const savantSubmitting = ref(false)

/** 旅行者离场（D-0037）：本人已离场 / 此刻能不能申请 / 待批申请的理由 / 最近一次裁定。 */
const departed = ref(false)
const canRequestDeparture = ref(false)
const hasPendingDeparture = ref(false)
const pendingDepartureNote = ref<string | null>(null)
const lastDepartureRuling = ref<DepartureRulingDto | null>(null)
const departureNote = ref('')
const departureNotice = ref('')
const departureSubmitting = ref(false)

let gateway: PlayerGateway | null = null
let clientSequence = 0
/** 自动回座只做一次（M1）：成功或失败都不再重试，免得每次登录态变化都重放一遍失败。 */
let resumed = false

/**
 * 账号（D-0021 / D-0027 / D-0029）：会话由 `accountSession` 模块级单例持有，**两个面共用一份**——
 * 换面不重新登录，因为它本来就是"同一个人"；M1 起它还写进 `sessionStorage`，
 * 所以连刷新都不用再登一次（位置见下面的 `resumeSeat`）。
 */
const accountProfile = session.profile

/** 大厅（D-0025）：列出在开的桌，让玩家**自己选一个空席位坐下**——不需要任何票据。 */
const tables = ref<LobbyTable[]>([])
const lobbyBusy = ref(false)
const lobbyNotice = ref('')
/** 本连接所在的那一桌：入座时定下来（D-0027 之后没有"默认桌"可回落）。 */
const selectedTable = ref<LobbyTable | null>(null)

/** 本连接所在那一桌的标识：访问模式推送按它匹配（D-0037）。 */
const playerTableGameId = ref<string | undefined>(undefined)

/**
 * 本桌的访问模式（D-0037）：null = 还不知道。
 *
 * 权威读取口是大厅列表那一行的 `inviteOnly`（补全初始条件的正路）；`ReceiveTableAccessChanged`
 * 只负责"说书人一切换，在场的人不刷新不重连就变"。不知道时界面说"—"，绝不写成"公开桌"。
 */
const tableInviteOnly = ref<boolean | null>(null)

/** 本桌标识：优先当前连接，其次大厅里选中的那一行，最后是记住的位置（刷新回座时还没连上）。 */
function currentTableGameId(): string | undefined {
  if (playerTableGameId.value !== undefined) {
    return playerTableGameId.value
  }

  const selected = selectedTable.value?.gameId
  if (selected !== undefined) {
    return selected
  }

  const remembered = session.activeTable.value
  return remembered !== null && remembered.surface === 'player' ? remembered.gameId : undefined
}

/** 从大厅列表取访问模式初值；列表里没有这一桌就留 null（宁可说"—"，不猜一个值写上去）。 */
function syncTableAccessFromLobby(): void {
  const gameId = currentTableGameId()
  tableInviteOnly.value =
    gameId === undefined
      ? null
      : (tables.value.find((table) => table.gameId === gameId)?.inviteOnly ?? null)
}

async function loadTables(): Promise<void> {
  if (accountProfile.value === null) {
    // 没登录就没有"我"，也就没有可挑的桌：大厅是登录之后才出现的东西（D-0027）。
    tables.value = []
    return
  }

  lobbyBusy.value = true
  try {
    tables.value = await session.listTables()
    // 列表是访问模式的权威读取口（D-0037）：刷新列表顺带把本桌那一行的读数对齐。
    syncTableAccessFromLobby()
    lobbyNotice.value = ''
  } catch (error) {
    lobbyNotice.value = `读取桌列表失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    lobbyBusy.value = false
  }
}

/**
 * 进这一桌的某个席位：换连接 → 只凭账号入座（不需要票据）。
 *
 * 这是入座的**唯一实现**——大厅点席位与刷新后的自动回座（M1）都走它；
 * 各写一份，迟早会分叉出"其中一条少校验一步"。
 */
async function enterSeat(gameId: string, seat: number): Promise<void> {
  const accountSession = accountProfile.value?.accountSession
  if (accountSession === undefined) {
    throw new Error('尚未登录：没有账号会话')
  }

  playerTableGameId.value = gameId
  // 换桌 = 换连接：先让旧网关**停干净**再建新的（`stop()` 会等到状态真的 Disconnected）。
  // 顺序很要紧：若先建新连接，旧连接的关闭还在进行中，SignalR 会报
  // "Failed to start the HttpConnection before stop() was called"（实测踩到）。
  const previous = gateway
  gateway = null
  if (previous !== null) {
    await previous.stop()
  }

  clientSequence = 0
  // 换桌即换读数：新桌的访问模式从列表取初值，别把上一桌的「邀请制」留在屏幕上。
  syncTableAccessFromLobby()
  const current = new PlayerGateway(buildCallbacks(), undefined, gameId)
  gateway = current
  try {
    await current.joinTable(seat, accountSession)
  } catch (error) {
    // 入座失败：把半成品网关丢掉，免得界面留着一个连上了却没入座的连接。
    gateway = null
    await current.stop()
    throw error
  }
}

/** 选一个席位坐下（大厅主路径）：入座成功即记住位置，刷新后能自动回到这里。 */
async function takeSeat(table: LobbyTable, seat: number): Promise<void> {
  if (accountProfile.value === null) {
    lobbyNotice.value = '请先注册或登录，再选席位入座'
    return
  }

  lobbyBusy.value = true
  lobbyNotice.value = ''
  try {
    selectedTable.value = table
    await enterSeat(table.gameId, seat)
    // 记住"我在哪一席"（M1 / D-0029）：刷新回来该直接坐回去，而不是重新逛一遍大厅。
    session.rememberTable({ surface: 'player', gameId: table.gameId, seat })
    lobbyNotice.value = `已坐在 ${table.name.length > 0 ? table.name : table.gameId} 的 ${seat} 号席位`

    // 人数变了：刷新列表，别让大厅停在旧数字上。
    await loadTables()
  } catch (error) {
    selectedTable.value = null
    lobbyNotice.value = `入座失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    lobbyBusy.value = false
  }
}

/**
 * 回到上一次坐的那一席（M1 / D-0029）：刷新的正常路径。
 *
 * 只在"已登录 + 还没连上任何一桌 + 记得位置"时跑一次。回不去就**清掉位置并说明原因**——
 * 留着一条永远失败的记录，只会让每次刷新都重演同一个失败。
 */
async function resumeSeat(): Promise<void> {
  if (resumed || gateway !== null || accountProfile.value === null) {
    return
  }

  const target = session.activeTable.value
  if (target === null || target.surface !== 'player' || target.seat === null) {
    return
  }

  resumed = true
  try {
    selectedTable.value = tables.value.find((table) => table.gameId === target.gameId) ?? null
    await enterSeat(target.gameId, target.seat)
    lobbyNotice.value = `已回到「${target.gameId}」的 ${target.seat} 号席位`
  } catch (error) {
    session.forgetTable()
    selectedTable.value = null
    lobbyNotice.value = `没能回到 ${target.seat} 号席位：${
      error instanceof Error ? error.message : String(error)
    }`
  }
}

/** 邀请码入座（兜底路径）：说书人给的那一串，写成「桌标识:席位邀请码」。 */
const inviteCode = ref('')
const inviteBusy = ref(false)
const inviteNotice = ref('')

/**
 * 用邀请码入座：**这是给"大厅点不动"的场合留的一条路**——桌是邀请制（或已开局）时，
 * 说书人新签发的旅行者席位、以及换设备的兜底，都走这里。
 *
 * 码写成 `桌标识:席位邀请码`（说书人面板显示的就是这个形态）：桌标识属于连接，
 * 光有票据不知道连哪一桌。**入座必须登录**（D-0037）：没有账号就没有"你是哪一席"，
 * 所以未登录时这里直接拦下并说明，绝不把 null 当账号会话发出去。
 */
async function joinByInviteCode(): Promise<void> {
  const current = accountProfile.value
  if (current === null) {
    inviteNotice.value = '请先登录，再凭邀请码入座'
    return
  }

  const raw = inviteCode.value.trim()
  const separator = raw.indexOf(':')
  if (separator <= 0 || separator === raw.length - 1) {
    inviteNotice.value = '邀请码要写成「桌标识:席位邀请码」——说书人面板上显示的就是这一串'
    return
  }

  const gameId = raw.slice(0, separator).trim()
  const ticket = raw.slice(separator + 1).trim()
  inviteBusy.value = true
  inviteNotice.value = ''
  try {
    const previous = gateway
    gateway = null
    if (previous !== null) {
      await previous.stop()
    }

    clientSequence = 0
    selectedTable.value = null
    playerTableGameId.value = gameId
    // 邀请码进来的人也看得到本桌的访问模式：大厅列表里那一行就是初值（读不到就等推送）。
    syncTableAccessFromLobby()
    gateway = new PlayerGateway(buildCallbacks(), undefined, gameId)
    const joined = await gateway.joinSeat(ticket, current.accountSession)
    inviteNotice.value = `已凭邀请码入座（${gameId}）`
    // 邀请码也是入座：位置照样记住（M1），否则刷新之后这条路径进来的人回不去。
    if (joined.seat > 0) {
      session.rememberTable({ surface: 'player', gameId, seat: joined.seat })
    }
  } catch (error) {
    inviteNotice.value = `凭邀请码入座失败：${error instanceof Error ? error.message : String(error)}`
    gateway = null
  } finally {
    inviteBusy.value = false
  }
}

/**
 * 这个席位按钮能不能点（D-0027 / D-0037）。
 *
 * 「回到我的座位」是**始终可点**的一格：它不能被"邀请制 / 已开局"的整排置灰吃掉——
 * 只按旧口径禁用，换设备回来（或清掉浏览器登录态）的玩家就再也回不到自己的位置。
 * 服务端本来就允许同一账号选回自己已认领的席位，界面不该比服务端更严。
 *
 * 另外两种点不动都是**服务端的闸**在界面上的映射：邀请制桌（要凭邀请码）与已开局的桌
 * （一开局自助入座就关了，迟到的旅行者由说书人发邀请码进来）。前端不判规则，只是不显示入口。
 */
function seatDisabled(table: LobbyTable, seat: number): boolean {
  if (table.mySeatNumbers.includes(seat)) {
    return false
  }

  return table.inviteOnly || table.started || table.occupiedSeatNumbers.includes(seat)
}

function seatTitle(table: LobbyTable, seat: number): string {
  if (table.mySeatNumbers.includes(seat)) {
    return `回到我的座位（${seat} 号席）`
  }

  if (table.occupiedSeatNumbers.includes(seat)) {
    return '这个席位已经有人了'
  }

  return table.inviteOnly || table.started
    ? '这一桌是邀请制（或已开局），需要邀请码'
    : `坐 ${seat} 号席`
}

/** 同桌名单：有玩家名的席位 + 自己（自己还没名字时也列出来，显示回退的席位号）。 */
const roster = computed(() => {
  const current = view.value
  if (current === null) {
    return []
  }

  const named = new Set<number>(current.seatNames.map((entry) => entry.seat))
  named.add(current.seat)
  return [...named].sort((left, right) => left - right)
})

const stateText: Record<GatewayState, string> = {
  disconnected: '还没连上',
  connecting: '正在连',
  connected: '已连接',
  reconnecting: '重连中',
}

const connected = computed(() => connectionState.value === 'connected' && view.value !== null)

function ensureGateway(): PlayerGateway {
  // 连到"选中的那一桌"：入座时定下来（D-0027 之后没有默认桌可回落）。
  gateway ??= new PlayerGateway(buildCallbacks(), undefined, selectedTable.value?.gameId)

  return gateway
}

/** 复盘面板开关：入口只在结束批次之后出现（R-0043）；服务端另有闸，前端只是不显示。 */
const replayOpen = ref(false)

/** 复盘取数：把共用面板接到本连接（服务端按事件序号分页）。 */
function fetchReplay(afterSequence: number, pageSize: number): Promise<ReplayViewDto> {
  const current = gateway
  if (current === null) {
    return Promise.reject(new Error('尚未加入：不能读取复盘'))
  }

  return current.fetchReplay(afterSequence, pageSize)
}

function buildCallbacks(): PlayerCallbacks {
  return {
    // 视图的唯一写入点：快照与推送已在网关按序号合并，这里只做呈现态同步。
    onView: (next) => applyView(next),
    onRequestVoided: (voided) => {
      // 只处理正挂在面板上的那一条：其他请求的作废与这名玩家无关（D-0013 §5）。
      if (pending.value === null || pending.value.requestId !== voided.requestId) {
        return
      }

      pending.value = null
      selectedOption.value = ''
      selectedSecondary.value = ''
      const detail = voided.note === null ? '' : `（${voided.note}）`
      settledNote.value = `请求已作废：${voidReasonLabelOf(voided.reason)}${detail}`
    },
    onRequestAnswered: (answered) => {
      if (pending.value === null || pending.value.requestId !== answered.requestId) {
        return
      }

      pending.value = null
      selectedOption.value = ''
      selectedSecondary.value = ''
      // 玩家本人作答的面板在提交回执到达时就会清空；这里只给"被说书人代填"一个交代。
      settledNote.value =
        answered.source === 'StorytellerProxy' ? '请求已了结：由说书人代填' : ''
    },
    onState: (state) => {
      connectionState.value = state
    },
    // 桌的访问模式变了（D-0037）：说书人一切换就推给该桌全部连接，界面当场改读数——
    // 不用刷新、不用重连，也不用重读大厅列表。只认本桌：别的桌的读数不该改这一行。
    onTableAccess: (access) => {
      if (access.gameId === currentTableGameId()) {
        tableInviteOnly.value = access.inviteOnly
      }
    },
    onDiagnostic: (message) => pushDiagnostic(message),
  }
}

/** 应用合并后的视图：面板的呈现态只在这里从服务端数据同步（唯一写入者）。 */
function applyView(next: PlayerViewDto): void {
  const previous = pending.value
  view.value = next
  pending.value = next.pendingRequest
  day.value = next.day
  informationResults.value = [...next.informationResults]
  outcome.value = next.outcome

  // 刷新 / 重连后按 URL 里的序号恢复复盘位置（票据矩阵行 7）：只在结束批次之后自动打开。
  if (next.outcome !== null && !replayOpen.value && /[?&]replay=\d+/.test(window.location.hash)) {
    replayOpen.value = true
  }
  klutzChoices.value = [...next.klutzChoices]
  canAskArtistQuestion.value = next.canAskArtistQuestion
  pendingQuestion.value = next.pendingQuestion
  canAskSavantQuestion.value = next.canAskSavantQuestion
  awaitingSavantQuestion.value = next.awaitingSavantQuestion
  departed.value = next.departed
  canRequestDeparture.value = next.canRequestDeparture
  hasPendingDeparture.value = next.hasPendingDeparture
  pendingDepartureNote.value = next.pendingDepartureNote
  lastDepartureRuling.value = next.lastDepartureRuling

  if (next.pendingRequest === null) {
    selectedOption.value = ''
    selectedSecondary.value = ''
    return
  }

  if (previous === null || previous.requestId !== next.pendingRequest.requestId) {
    selectedOption.value = next.pendingRequest.options[0]?.value ?? ''
    selectedSecondary.value = next.pendingRequest.secondaryOptions[0]?.value ?? ''
  }

  // 新请求（含重投）到达即清空说明：这条说明只描述"上一次请求是怎么结束的"。
  settledNote.value = ''
}

function pushDiagnostic(message: string): void {
  // 空消息不是诊断：忽略它，别把空条目渲染进列表（网关已只转发非空，这里再兜一层）。
  if (message.trim().length === 0) {
    return
  }

  diagnostics.value = [message, ...diagnostics.value].slice(0, 5)
}

/** 提名 / 举手包装：把网关实例收敛成两个纯函数，交给白天面板（面板不持有连接）。 */
function nominateSeat(seat: number, idempotencyKey: string): Promise<unknown> {
  return ensureGateway().nominate(seat, idempotencyKey)
}

function voteOnNomination(nominationIndex: number, voted: boolean, idempotencyKey: string): Promise<unknown> {
  return ensureGateway().castVote(nominationIndex, voted, idempotencyKey)
}

/** 旅行者与流放（D1 / D2 / D7）：发起流放、流放举手、屠夫窗口的额外提名（R-0044 / R-0050）。 */
function proposeExileSeat(seat: number, idempotencyKey: string): Promise<unknown> {
  return ensureGateway().proposeExile(seat, idempotencyKey)
}

function voteOnExile(exileIndex: number, voted: boolean, idempotencyKey: string): Promise<unknown> {
  return ensureGateway().castExileVote(exileIndex, voted, idempotencyKey)
}

function nominateExtraSeat(seat: number, idempotencyKey: string): Promise<unknown> {
  return ensureGateway().nominateExtra(seat, idempotencyKey)
}

/** 杂耍艺人的公开猜测（R-0057-B）：猜测进公开面，猜对数当晚只到本人。 */
function makeJugglerGuesses(
  guesses: JugglerGuessDto[],
  idempotencyKey: string,
): Promise<unknown> {
  return ensureGateway().makeJugglerGuesses(guesses, idempotencyKey)
}

/** 艺术家提问（R-0040）：问题由玩家决定；「要求重问」不消耗能力，回答只到本人。 */
async function askArtistQuestion(): Promise<void> {
  const question = artistQuestion.value.trim()
  if (question.length === 0) {
    pushDiagnostic('问题不能为空')
    return
  }

  artistQuestionSubmitting.value = true
  try {
    const raw = await ensureGateway().askArtistQuestion(
      question,
      newIdempotencyKey('artist-question'),
    )
    if (raw === null || typeof raw !== 'object') {
      pushDiagnostic('回执形状不可识别')
      return
    }

    const kind = (raw as Record<string, unknown>)['kind']
    if (kind === 'Accepted' || kind === 'Duplicate') {
      artistQuestion.value = ''
      return
    }

    pushDiagnostic(`提问未成功：${String(kind)}`)
  } catch (error) {
    pushDiagnostic(`提问失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    artistQuestionSubmitting.value = false
  }
}

/** 博学者要两条信息（R-0057）：内容由说书人给（一真一假），两条都只发给他自己。 */
async function askSavantQuestion(): Promise<void> {
  savantSubmitting.value = true
  try {
    const raw = await ensureGateway().askSavantQuestion(newIdempotencyKey('savant-question'))
    if (raw === null || typeof raw !== 'object') {
      pushDiagnostic('回执形状不可识别')
      return
    }

    const kind = (raw as Record<string, unknown>)['kind']
    if (kind === 'Accepted' || kind === 'Duplicate') {
      return
    }

    pushDiagnostic(`要信息未成功：${String(kind)}`)
  } catch (error) {
    pushDiagnostic(`要信息失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    savantSubmitting.value = false
  }
}

async function submit(): Promise<void> {
  const request = pending.value
  if (request === null) {
    return
  }

  if (selectedOption.value.length === 0) {
    pushDiagnostic('请先选择一个选项')
    return
  }

  // 两维选择必须两维都给全（R-0021）：服务端按 `{第一维}|{第二维}` 组合校验，缺一维会被拒。
  if (request.secondaryOptions.length > 0 && selectedSecondary.value.length === 0) {
    pushDiagnostic('这次选择有两个维度：两项都要选')
    return
  }

  submitting.value = true
  try {
    clientSequence += 1
    const answer =
      request.secondaryOptions.length > 0
        ? `${selectedOption.value}|${selectedSecondary.value}`
        : selectedOption.value
    const raw = await ensureGateway().submitResponse(
      request.requestId,
      answer,
      newIdempotencyKey('response'),
      clientSequence,
    )
    if (raw === null || typeof raw !== 'object') {
      pushDiagnostic('回执形状不可识别')
      return
    }

    const kind = (raw as Record<string, unknown>)['kind']
    if (kind === 'Accepted' || kind === 'Duplicate') {
      pending.value = null
      note.value = ''
      return
    }

    pushDiagnostic(`提交未成功：${String(kind)}`)
  } catch (error) {
    pushDiagnostic(`提交失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    submitting.value = false
  }
}

/**
 * 玩家命令回执 → 可展示的说明（服务端文案原样显示，不吞、不改写）。
 * 界面只关心"受理了没有"与"服务端说了什么"——真正的拒绝理由在服务端（前端不判规则）。
 */
function commandReplyOf(raw: unknown): { ok: boolean; message: string } {
  if (raw === null || typeof raw !== 'object') {
    return { ok: false, message: '回执形状不可识别' }
  }

  const result = raw as Record<string, unknown>
  const kind = result['kind']
  const parts = [result['rejectionCode'], result['rejectionMessage'], result['failure']].filter(
    (part): part is string => typeof part === 'string' && part.length > 0,
  )
  return { ok: kind === 'Accepted' || kind === 'Duplicate', message: parts.join('：') }
}

/**
 * 提出离场申请（D-0037）：玩家发起、说书人裁定。
 *
 * 受理之后不进"已离场"——状态由服务端随后推送的本人视图给出（`hasPendingDeparture` 为真即等待态）；
 * 这里只把失败的服务端文案显示出来，绝不自己先把界面改成"已提交"。
 */
async function requestDeparture(): Promise<void> {
  departureSubmitting.value = true
  departureNotice.value = ''
  try {
    const note = departureNote.value.trim()
    const reply = commandReplyOf(
      await ensureGateway().requestTravellerDeparture(
        note.length > 0 ? note : null,
        newIdempotencyKey('departure-request'),
      ),
    )
    if (!reply.ok) {
      departureNotice.value =
        reply.message.length > 0 ? `申请未成功：${reply.message}` : '申请未成功：服务端没有受理'
      return
    }

    // 已受理：等待态由服务端随后推回的 hasPendingDeparture / pendingDepartureNote 表达，输入框随之清空。
    departureNote.value = ''
  } catch (error) {
    departureNotice.value = `申请失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    departureSubmitting.value = false
  }
}

async function resync(): Promise<void> {
  try {
    // 视图由网关合并后经 onView 下发（迟到的快照不会覆盖窗口内到达的推送）。
    await ensureGateway().resync()
    pushDiagnostic('已按自身序号重新补齐')
  } catch (error) {
    pushDiagnostic(`补齐失败：${error instanceof Error ? error.message : String(error)}`)
  }
}

async function disconnect(): Promise<void> {
  await gateway?.stop()
  // 主动离开就是主动离开（M1）：位置一起忘掉，别让下一次刷新又把人送回这一席。
  session.forgetTable()
  // 访问模式的读数属于"本桌"：断开就清掉，别把上一桌的「邀请制」留在下一屏。
  playerTableGameId.value = undefined
  tableInviteOnly.value = null
  view.value = null
  pending.value = null
  day.value = null
  outcome.value = null
  klutzChoices.value = []
  canAskArtistQuestion.value = false
  pendingQuestion.value = null
  artistQuestion.value = ''
  canAskSavantQuestion.value = false
  awaitingSavantQuestion.value = false
  departed.value = false
  canRequestDeparture.value = false
  hasPendingDeparture.value = false
  pendingDepartureNote.value = null
  lastDepartureRuling.value = null
  departureNote.value = ''
  departureNotice.value = ''
}

/** 胜方文案：未知取值原样回显（服务端数据是不可信输入，不猜、不吞）。 */
function winnerLabelOf(winner: string): string {
  if (winner === 'Good') {
    return '善良阵营获胜'
  }

  if (winner === 'Evil') {
    return '邪恶阵营获胜'
  }

  return `未知胜方（${winner}）`
}

// 登录 / 登出之后桌列表要跟着变：换了账号就不该看到上一个人的事实（比如"我开的桌"标记）。
// 自动回座也挂在这里（M1 / D-0029）：登录态是**异步**恢复的（`accountSession.restore`），
// 挂载那一刻还读不到"我是谁"，只能等它落定再决定回哪一桌。
// `immediate` 让"挂载时已经登录"（例如页内切面后回来）这条也算上。
watch(
  accountProfile,
  async () => {
    await loadTables()
    await resumeSeat()
  },
  { immediate: true },
)

onBeforeUnmount(() => {
  void gateway?.stop()
})
</script>

<template>
  <div class="shell">
    <!-- 没登录：页面上只有一张账号卡（D-0027）。桌列表是登录之后才出现的东西。 -->
    <AccountGate v-if="!connected && accountProfile === null" />

    <section v-else-if="!connected" class="home panel">
      <h1>加入一桌</h1>
      <p class="hint">
        公开桌挑一个空席位坐下就行，不需要说书人给你发任何东西；邀请制桌与已开局的桌要凭邀请码。
      </p>

      <div class="lobby" data-testid="player-lobby">
        <div class="row">
          <strong>在开的桌</strong>
          <button type="button" :disabled="lobbyBusy" @click="loadTables()">刷新列表</button>
          <span class="hint">共 {{ tables.length }} 桌</span>
        </div>
        <p v-if="lobbyNotice.length > 0" class="hint" data-testid="lobby-notice">{{ lobbyNotice }}</p>
        <ul v-if="tables.length > 0" class="tables">
          <li
            v-for="table in tables"
            :key="table.gameId"
            :data-table="table.gameId"
            :data-invite-only="String(table.inviteOnly)"
          >
            <span>{{ table.name.length > 0 ? table.name : table.gameId }}</span>
            <span class="hint">
              {{ table.takenSeatCount }} / {{ table.seatCapacity }} 人 · {{ table.started ? '已开局' : '等人' }} ·
              {{ table.inviteOnly ? '邀请制' : '公开' }}
            </span>
            <span class="seats">
              <button
                v-for="seat in table.seatCapacity"
                :key="seat"
                type="button"
                class="seat"
                :disabled="lobbyBusy || seatDisabled(table, seat)"
                :data-seat="`${table.gameId}-${seat}`"
                :data-seat-mine="table.mySeatNumbers.includes(seat) ? 'true' : undefined"
                :title="seatTitle(table, seat)"
                @click="takeSeat(table, seat)"
              >
                {{ seat }}
              </button>
            </span>
          </li>
        </ul>
        <p v-else class="hint">
          现在还没有人开桌。去顶栏的「主持一局」开一桌，你就是那一桌的说书人。
        </p>
      </div>

      <!-- 邀请码：邀请制桌与已开局的桌在大厅点不动——中途到场的旅行者、换设备的兜底都走这里。
           入座必须登录（D-0037），所以未登录时这里只说明一句，不发命令。 -->
      <details class="invite" data-testid="seat-invite">
        <summary>有邀请码？凭邀请码入座</summary>
        <p class="hint">
          说书人给你的那一串，形态是「桌标识:席位邀请码」（他面板上显示的就是它）；要先登录才能入座。
        </p>
        <div class="row">
          <input v-model="inviteCode" data-testid="seat-invite-code" placeholder="桌标识:席位邀请码" spellcheck="false" />
          <button type="button" :disabled="inviteBusy" data-testid="seat-invite-join" @click="joinByInviteCode()">
            入座
          </button>
        </div>
        <p v-if="inviteNotice.length > 0" class="hint" data-testid="seat-invite-notice">{{ inviteNotice }}</p>
      </details>

      <AccountPanel />
      <ul v-if="diagnostics.length > 0" class="diagnostics">
        <li v-for="message in diagnostics" :key="message">{{ message }}</li>
      </ul>
    </section>

    <template v-else>
      <header class="panel head">
        <div>
          <span class="tag" data-testid="player-seat">{{
            seatDisplayOf(view!.seat, view!.seatNames)
          }}</span>
          <strong data-testid="player-phase">{{
            labelOf(view!.phase) === '—' ? '阶段未知' : labelOf(view!.phase)
          }}</strong>
          <HelpTip topic="phase" />
          <!-- 本桌的访问模式（D-0037）：说书人一切换就推过来，不刷新不重连也变；不知道时说「—」。 -->
          <span class="hint" data-testid="player-table-access" :data-invite-only="String(tableInviteOnly)"
            >本桌：{{ tableInviteOnly === null ? '—' : tableInviteOnly ? '邀请制' : '公开桌' }}</span
          >
        </div>
        <div class="row">
          <button type="button" @click="resync()">补齐</button>
          <button type="button" @click="disconnect()">断开</button>
          <span class="hint">连接：{{ stateText[connectionState] }}</span>
        </div>
      </header>

      <!-- 「我是谁」（R-0059）：本人角色与阵营，只发给自己；换角后服务端定向推一次本人视图。 -->
      <section
        class="panel"
        data-testid="player-own-character"
        :data-character="view!.character ?? ''"
        :data-alignment="view!.alignment ?? ''"
      >
        <h2>我的角色<HelpTip topic="player-character" /></h2>
        <p class="block-question">你这一局拿到的角色与阵营；换角之后这里会跟着变。</p>
        <p v-if="view!.character === null" class="placeholder" data-testid="player-own-character-empty">
          还没有分配角色。
        </p>
        <p v-else class="own-character">
          <strong data-testid="player-own-character-name">{{
            characterLabelOf(view!.character)
          }}</strong>
          <span
            v-if="view!.alignment !== null"
            class="own-alignment"
            data-testid="player-own-character-alignment"
            >{{ alignmentLabelOf(view!.alignment) }}</span
          >
        </p>
      </section>

      <!-- 旅行者离场（D-0037）：玩家发起 → 说书人裁定。四个位都空时整块不渲染（不留空壳）。 -->
      <section
        v-if="departed || canRequestDeparture || hasPendingDeparture || lastDepartureRuling !== null"
        class="panel"
        data-testid="player-departure"
      >
        <h2>离场</h2>
        <p class="block-question">想以旅行者身份离开本局时由你提出，说书人裁定；批准后座位从本局移除。</p>

        <p v-if="departed" class="context" data-testid="player-departed">
          你已经离场（座位已从本局移除，不再计入任何人数口径）
        </p>

        <!-- 等待态看 hasPendingDeparture，**不是**"理由非 null"：理由是可选字段，
             拿它当判据会让"没写理由"这条默认路径下整块界面消失（装置咬出来的真缺陷）。 -->
        <p v-if="hasPendingDeparture" class="context" data-testid="departure-pending">
          已提出离场申请，等说书人裁定<template v-if="pendingDepartureNote !== null && pendingDepartureNote.length > 0">
            （你写的理由：{{ pendingDepartureNote }}）
          </template>
        </p>

        <template v-if="canRequestDeparture && !departed">
          <input
            v-model="departureNote"
            :maxlength="200"
            placeholder="离场理由（可选，只说给说书人）"
            spellcheck="false"
            data-testid="departure-note"
          />
          <button
            type="button"
            class="primary"
            :disabled="departureSubmitting"
            data-testid="departure-request"
            @click="requestDeparture()"
          >
            申请离场
          </button>
        </template>

        <p
          v-if="lastDepartureRuling !== null"
          class="context"
          data-testid="departure-ruling"
          :data-approved="String(lastDepartureRuling.approved)"
        >
          说书人已{{ lastDepartureRuling.approved ? '批准' : '驳回' }}了你的离场申请<template
            v-if="lastDepartureRuling.note !== null"
          >
            （{{ lastDepartureRuling.note }}）
          </template>
        </p>

        <p v-if="departureNotice.length > 0" class="hint" data-testid="departure-notice">
          {{ departureNotice }}
        </p>
      </section>

      <section
        v-if="outcome"
        class="panel"
        data-testid="player-outcome"
        :data-outcome-winner="outcome.winner"
      >
        <h2>本局结束</h2>
        <p class="block-question">这一局的胜负结论与结束方式。</p>
        <p class="winner">{{ winnerLabelOf(outcome.winner) }}</p>
        <p class="hint" data-testid="player-outcome-detail">{{ outcome.detail }}</p>
      </section>

      <section v-if="outcome" class="panel" data-testid="player-replay-entry">
        <button type="button" data-testid="player-replay-open" @click="replayOpen = true">查看复盘</button>
        <ReplayPanel
          v-if="replayOpen"
          :fetch-replay="fetchReplay"
          :fallback-seat="view?.seat ?? null"
          @close="replayOpen = false"
        />
      </section>

      <section
        class="panel"
        data-testid="player-request-panel"
        :data-request-state="pending === null ? 'idle' : 'pending'"
        :data-request-id="pending?.requestId ?? ''"
      >
        <h2>当前请求<HelpTip topic="player-request" /></h2>
        <p class="block-question">轮到你要做的选择；没有就是先等着。</p>
        <div v-if="pending === null" class="placeholder" data-testid="player-idle">
          现在没有需要你做的事；轮到你时请求会自动出现。
        </div>
        <template v-else>
          <p class="context" data-testid="player-request-context">{{ pending.context }}</p>
          <div class="options" data-testid="player-request-options">
            <label
              v-for="option in pending.options"
              :key="option.value"
              class="option"
              :data-option-value="option.value"
            >
              <input v-model="selectedOption" type="radio" :value="option.value" />
              {{ optionDisplayOf(option, view!.seatNames) }}
            </label>
          </div>
          <div
            v-if="pending.secondaryOptions.length > 0"
            class="options"
            data-testid="player-request-secondary-options"
          >
            <p class="hint">这一步要同时选两项（第二项）：</p>
            <label
              v-for="option in pending.secondaryOptions"
              :key="option.value"
              class="option"
              :data-option-value="option.value"
            >
              <input v-model="selectedSecondary" type="radio" :value="option.value" />
              {{ optionDisplayOf(option, view!.seatNames) }}
            </label>
          </div>
          <input v-model="note" placeholder="备注（可选）" />
          <button type="button" class="primary" :disabled="submitting" data-testid="player-submit" @click="submit()">
            提交
          </button>
        </template>
        <p
          v-if="pending === null && settledNote.length > 0"
          class="settled-note"
          data-testid="player-settled-note"
        >
          {{ settledNote }}<HelpTip topic="voided-request" />
        </p>
      </section>

      <PlayerDayPanel
        v-if="day"
        :day="day"
        :seat="view!.seat"
        :seat-names="view!.seatNames"
        :nominate="nominateSeat"
        :vote="voteOnNomination"
        :propose-exile="proposeExileSeat"
        :cast-exile-vote="voteOnExile"
        :nominate-extra="nominateExtraSeat"
        :make-juggler-guesses="makeJugglerGuesses"
        @diagnostic="pushDiagnostic"
      />

      <!-- 三态：可提问（idle）/ 等待回答（waiting；此时服务端权限位为 false，靠 pendingQuestion 保持可见）/ 已用尽（整块撤下）。 -->
      <section
        v-if="canAskArtistQuestion || pendingQuestion !== null"
        class="panel"
        data-testid="player-artist-question"
        :data-question-state="pendingQuestion === null ? 'idle' : 'waiting'"
      >
        <h2>向说书人提问</h2>
        <p class="block-question">每局限一次；「要求重问」不消耗能力，回答只发给你自己。</p>
        <p
          v-if="pendingQuestion !== null"
          class="context"
          data-testid="player-artist-question-pending"
        >
          已提问，等待说书人回答：「{{ pendingQuestion }}」
        </p>
        <template v-else>
          <input
            v-model="artistQuestion"
            :maxlength="200"
            placeholder="是 / 否问题，例如：2 号是爪牙吗？"
            spellcheck="false"
            @keyup.enter="askArtistQuestion()"
          />
          <button
            type="button"
            class="primary"
            :disabled="artistQuestionSubmitting"
            data-testid="player-artist-question-submit"
            @click="askArtistQuestion()"
          >
            提问
          </button>
        </template>
      </section>

      <!-- 三态：可要信息（idle）/ 等待说书人给两条（waiting）/ 今天已经要过（整块撤下）。 -->
      <section
        v-if="canAskSavantQuestion || awaitingSavantQuestion"
        class="panel"
        data-testid="player-savant-question"
        :data-question-state="awaitingSavantQuestion ? 'waiting' : 'idle'"
      >
        <h2>向说书人要两条信息</h2>
        <p class="block-question">每个白天一次；说书人会给你一条正确、一条错误的信息，你不知道哪条是哪个。两条都只发给你自己。</p>
        <p
          v-if="awaitingSavantQuestion"
          class="context"
          data-testid="player-savant-question-pending"
        >
          已开口，等待说书人给出两条信息。
        </p>
        <button
          v-else
          type="button"
          class="primary"
          :disabled="savantSubmitting"
          data-testid="player-savant-question-submit"
          @click="askSavantQuestion()"
        >
          要两条信息
        </button>
      </section>

      <section v-if="klutzChoices.length > 0" class="panel" data-testid="player-klutz-choices">
        <h2>呆瓜的公开选择</h2>
        <p class="block-question">结束时的公开选择记录；这一步所有人都看得到。</p>
        <ul class="information">
          <li v-for="choice in klutzChoices" :key="choice.sequence">{{ choice.detail }}</li>
        </ul>
      </section>

      <section class="panel" data-testid="player-information" :data-information-count="informationResults.length">
        <h2>我收到的信息<HelpTip topic="information" /></h2>
        <p class="block-question">只发给你的信息结果。</p>
        <div v-if="informationResults.length === 0" class="placeholder">还没有收到信息。</div>
        <ul v-else class="information">
          <li
            v-for="(information, index) in informationResults"
            :key="`${index}-${information.ability}`"
            :data-information-index="index"
          >
            <strong>{{ characterLabelOf(information.ability) }}</strong>：{{ information.content }}
          </li>
        </ul>
        <p class="hint">信息可能是错的——说书人对醉酒 / 中毒玩家的信息有裁量权（D-0002）。</p>
      </section>

      <section class="panel" data-testid="player-roster">
        <h2>同桌<HelpTip topic="player-name" /></h2>
        <p class="block-question">这一桌都有谁；席位号后面是玩家名。</p>
        <p v-if="roster.length === 0" class="hint">还没有席位信息。</p>
        <ul v-else class="roster">
          <li v-for="seat in roster" :key="seat" :data-seat="seat">
            {{ seatDisplayOf(seat, view!.seatNames) }}<span v-if="seat === view!.seat">（你）</span>
          </li>
        </ul>
      </section>

      <section class="panel" data-testid="player-account">
        <h2>账号<HelpTip topic="player-name" /></h2>
        <p class="block-question">你的公开玩家名来自这里；改名、登出都在这。</p>
        <AccountPanel />
      </section>

      <ul v-if="diagnostics.length > 0" class="diagnostics" data-testid="player-diagnostics">
        <li v-for="message in diagnostics" :key="message">{{ message }}</li>
      </ul>
    </template>
  </div>
</template>

<style scoped>
.shell {
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  max-width: 720px;
  margin: 0 auto;
}

.login {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 40px;
}

h1 {
  margin: 0;
  font-size: 20px;
}

.head {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
}

.row {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}

.options {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 6px;
}

.option {
  display: flex;
  gap: 6px;
  align-items: center;
}

.context {
  margin: 0 0 6px;
  font-size: 15px;
}

.information {
  margin: 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

/* 同桌改紧凑一行制：席位多时不再占满整屏（信息密度，矩阵行 3）。 */
.roster {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 4px 10px;
}

.roster li {
  font-size: 13px;
}

.diagnostics {
  margin: 0;
  padding-left: 18px;
  color: var(--warn);
  font-size: 12px;
}

.settled-note {
  margin: 6px 0 0;
  color: var(--ink-soft);
  font-size: 13px;
}

.winner {
  margin: 0 0 6px;
  font-size: 18px;
  font-weight: 700;
}

/* 「我的角色」：进桌第一眼要看到的东西，字号比正文大一档。 */
.own-character {
  margin: 0;
  display: flex;
  gap: 10px;
  align-items: baseline;
  flex-wrap: wrap;
  font-size: 18px;
}

.own-alignment {
  font-size: 13px;
  color: var(--ink-soft);
}
</style>
