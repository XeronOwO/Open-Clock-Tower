<script setup lang="ts">
/**
 * 玩家视图（同一 SPA 的另一套视图，D-0004）。
 *
 * 玩家只能看到服务端下发给他的东西：自己的席位、当前大阶段、发给自己的请求与信息类结果。
 * 看板 / 状态账 / 计划进度一概不下发——所以这里也不会有对应的代码路径（D-0013 §5）。
 */
import type {
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
import { AccountGateway, type AccountProfile, type LobbyTable } from '@/services/accountGateway'
import { PlayerGateway, type PlayerCallbacks } from '@/services/playerGateway'
import { TicketStore } from '@/services/ticketStore'
import { newIdempotencyKey } from '@/services/idempotency'
import type { GatewayState } from '@/services/connectionState'
import AccountPanel from '@/features/account/AccountPanel.vue'
import PlayerDayPanel from '@/features/player/PlayerDayPanel.vue'
import ReplayPanel from '@/features/replay/ReplayPanel.vue'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

const ticket = ref('')
const store = new TicketStore()
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
const joining = ref(false)
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

let gateway: PlayerGateway | null = null
let clientSequence = 0

/** 账号（D-0021）：会话凭据只在 AccountGateway 内存里；面板只持有展示资料。 */
const accountProfile = ref<AccountProfile | null>(null)
const accountBusy = ref(false)
const accountNotice = ref('')
const accountRecoveryCode = ref('')
let accountGateway: AccountGateway | null = null

/** 大厅（D-0025）：列出在开的桌，让玩家**自己选一个空席位坐下**——不再需要说书人发席位票据。 */
const tables = ref<LobbyTable[]>([])
const lobbyBusy = ref(false)
const lobbyNotice = ref('')
/** 本连接所在的那一桌；null = 还没选（用默认桌）。 */
const selectedTable = ref<LobbyTable | null>(null)

async function loadTables(): Promise<void> {
  lobbyBusy.value = true
  try {
    tables.value = await ensureAccountGateway().listTables()
    lobbyNotice.value = ''
  } catch (error) {
    lobbyNotice.value = `读取桌列表失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    lobbyBusy.value = false
  }
}

/** 选一个席位坐下：重建连接指向该桌，然后只凭账号入座（不需要票据）。 */
async function takeSeat(table: LobbyTable, seat: number): Promise<void> {
  const session = accountProfile.value?.accountSession
  if (session === undefined) {
    lobbyNotice.value = '请先注册或登录，再选席位入座'
    return
  }

  lobbyBusy.value = true
  lobbyNotice.value = ''
  try {
    // 连接必须指向那一桌：先结束旧网关（它连的是别的桌），再按桌建新的。
    await gateway?.stop()
    gateway = null
    ticket.value = ''
    clientSequence = 0
    selectedTable.value = table
    await ensureGateway().joinTable(seat, session)
    lobbyNotice.value = `已坐在 ${table.name.length > 0 ? table.name : table.gameId} 的 ${seat} 号席位`
    // 人数变了：刷新列表，别让大厅停在旧数字上。
    await loadTables()
  } catch (error) {
    lobbyNotice.value = `入座失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    lobbyBusy.value = false
  }
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

function ensureAccountGateway(): AccountGateway {
  accountGateway ??= new AccountGateway()
  return accountGateway
}

function accountFailureText(code: string, message: string): string {
  return message.length > 0 ? `账号操作未成功（${code}）：${message}` : `账号操作未成功（${code}）`
}

async function registerAccount(username: string, displayName: string, password: string): Promise<void> {
  accountBusy.value = true
  accountRecoveryCode.value = ''
  try {
    const result = await ensureAccountGateway().register(username, displayName, password)
    if (!result.ok) {
      accountNotice.value = accountFailureText(result.code, result.message)
      return
    }

    accountProfile.value = ensureAccountGateway().profile
    accountNotice.value = '注册成功，已登录；凭票据加入即可认领席位'
    accountRecoveryCode.value = result.recoveryCode ?? ''
  } catch (error) {
    accountNotice.value = `注册失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    accountBusy.value = false
  }
}

async function loginAccount(username: string, password: string): Promise<void> {
  accountBusy.value = true
  accountRecoveryCode.value = ''
  try {
    const result = await ensureAccountGateway().login(username, password)
    if (!result.ok) {
      accountNotice.value = accountFailureText(result.code, result.message)
      return
    }

    accountProfile.value = ensureAccountGateway().profile
    accountNotice.value = '已登录；凭票据加入即可认领席位'
  } catch (error) {
    accountNotice.value = `登录失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    accountBusy.value = false
  }
}

async function logoutAccount(): Promise<void> {
  accountBusy.value = true
  try {
    await ensureAccountGateway().logout()
    accountProfile.value = null
    accountNotice.value = '已登出（席位票据仍然有效）'
    accountRecoveryCode.value = ''
  } catch (error) {
    accountNotice.value = `登出失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    accountBusy.value = false
  }
}

async function renameAccount(displayName: string): Promise<void> {
  if (displayName.length === 0) {
    accountNotice.value = '玩家名不能为空'
    return
  }

  accountBusy.value = true
  try {
    const result = await ensureAccountGateway().changeDisplayName(displayName)
    if (!result.ok) {
      accountNotice.value = accountFailureText(result.code, result.message)
      return
    }

    accountProfile.value = ensureAccountGateway().profile
    accountNotice.value = `玩家名已改为「${result.displayName}」，已同步给同桌`
  } catch (error) {
    accountNotice.value = `改名失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    accountBusy.value = false
  }
}

async function resetAccountPassword(
  username: string,
  recoveryCode: string,
  newPassword: string,
): Promise<void> {
  accountBusy.value = true
  accountRecoveryCode.value = ''
  try {
    const result = await ensureAccountGateway().resetPassword(username, recoveryCode, newPassword)
    if (!result.ok) {
      accountNotice.value = accountFailureText(result.code, result.message)
      return
    }

    accountProfile.value = ensureAccountGateway().profile
    accountNotice.value = '口令已重置并重新登录；旧会话已失效'
    accountRecoveryCode.value = result.recoveryCode ?? ''
  } catch (error) {
    accountNotice.value = `重置失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    accountBusy.value = false
  }
}

const stateText: Record<GatewayState, string> = {
  disconnected: '未连接',
  connecting: '连接中',
  connected: '已连接',
  reconnecting: '重连中',
}

const connected = computed(() => connectionState.value === 'connected' && view.value !== null)

function ensureGateway(): PlayerGateway {
  // 连到"选中的那一桌"；没选就是本机默认桌（既有票据流程与装置不受影响）。
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

async function join(): Promise<void> {
  joining.value = true
  try {
    const seatTicket = ticket.value.trim()
    store.write(seatTicket)
    // 视图由网关合并后经 onView 下发；这里只负责发起与报错。
    // 账号（D-0021）：登录后带账号会话，票据用于首次认领；认领之后可以只凭账号（票据留空）。
    await ensureGateway().joinSeat(seatTicket, accountProfile.value?.accountSession ?? null)
    clientSequence = 0
  } catch (error) {
    pushDiagnostic(`加入失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    joining.value = false
  }
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

onMounted(() => {
  // 大厅是公开门面：先把在开的桌列出来（未登录也能看，坐下才需要账号）。
  void loadTables()
  const remembered = store.read()
  if (remembered.length > 0) {
    ticket.value = remembered
    void join()
  }
})

onBeforeUnmount(() => {
  void gateway?.stop()
  void accountGateway?.stop()
})
</script>

<template>
  <div class="shell">
    <section v-if="!connected" class="login panel">
      <h1>玩家端</h1>
      <p class="hint">
        登录后从下面的桌里直接选一个空席位坐下，不需要说书人发票据。席位票据仍然可用（邀请 / 换设备兜底）。
      </p>

      <!-- 大厅（D-0025）：公开信息；坐下需要账号。 -->
      <div class="lobby" data-testid="player-lobby">
        <div class="row">
          <strong>在开的桌</strong>
          <button type="button" :disabled="lobbyBusy" @click="loadTables()">刷新列表</button>
          <span class="hint">共 {{ tables.length }} 桌</span>
        </div>
        <p v-if="lobbyNotice.length > 0" class="hint" data-testid="lobby-notice">{{ lobbyNotice }}</p>
        <ul v-if="tables.length > 0" class="tables">
          <li v-for="table in tables" :key="table.gameId" :data-table="table.gameId">
            <span>{{ table.name.length > 0 ? table.name : table.gameId }}</span>
            <span class="hint">
              {{ table.takenSeatCount }} / {{ table.seatCapacity }} 人 · {{ table.started ? '已开局' : '等人' }} ·
              {{ table.locked ? '已锁定' : '可入座' }}
            </span>
            <span class="seats">
              <button
                v-for="seat in table.seatCapacity"
                :key="seat"
                type="button"
                class="seat"
                :disabled="lobbyBusy || table.locked || table.started"
                :data-seat="`${table.gameId}-${seat}`"
                @click="takeSeat(table, seat)"
              >
                {{ seat }}
              </button>
            </span>
          </li>
        </ul>
        <p v-else class="hint">还没有开桌。等管理员开一桌，或先用下面的票据入口。</p>
      </div>

      <p class="hint">也可以凭席位票据加入（说书人给你的那一串）。玩家端只会收到属于你自己的信息。</p>
      <div class="row">
        <input v-model="ticket" placeholder="席位票据" spellcheck="false" @keyup.enter="join()" />
        <button type="button" class="primary" :disabled="joining" @click="join()">加入</button>
      </div>
      <AccountPanel
        :profile="accountProfile"
        :busy="accountBusy"
        :notice="accountNotice"
        :recovery-code="accountRecoveryCode"
        @register="registerAccount"
        @login="loginAccount"
        @logout="logoutAccount"
        @rename="renameAccount"
        @reset="resetAccountPassword"
      />
      <p class="hint">连接状态：{{ stateText[connectionState] }}</p>
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
        <p class="block-question">你的公开玩家名来自这里；改名、登出、找回口令都在这。</p>
        <AccountPanel
          compact
          foldable
          :profile="accountProfile"
          :busy="accountBusy"
          :notice="accountNotice"
          :recovery-code="accountRecoveryCode"
          @register="registerAccount"
          @login="loginAccount"
          @logout="logoutAccount"
          @rename="renameAccount"
          @reset="resetAccountPassword"
        />
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
