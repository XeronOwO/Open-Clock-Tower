<script setup lang="ts">
/**
 * 说书人上帝视角面板的容器：连接 / 票据 / 命令回执 / 布局装配。
 *
 * 主视图是魔典圆环（`GrimoireView`，以席位为中心）；数据与审计收在可展开的下钻面板里
 * （与主视图同一份视图，矩阵行 6）；局务（兜底与推进 / 开局分配）在右列。
 *
 * 信息姿态：本面板只显示服务端下发的说书人视图（D-0012：视图由服务端重新投影）；
 * 前端不做领域推断，也不缓存旧值假装"还是那样"——掉线重连后整份重取。
 * 零信任姿态：命令必须带连接级凭据；凭据只在内存里，不渲染、不落盘。
 */
import type { ReplayViewDto, StorytellerViewDto } from '@/contracts/game'
import { clockTimeOf } from '@/display/format'
import { labelOf } from '@/display/labels'
import { StorytellerGateway, parseStorytellerTicket, type GatewayState } from '@/services/storytellerGateway'
import { AccountGateway, type AccountProfile, type LobbyCreateResult } from '@/services/accountGateway'
import AccountPanel from '@/features/account/AccountPanel.vue'
import { readSeatCount, DEFAULT_SEAT_COUNT } from '@/services/serverConfig'
import { TicketStore } from '@/services/ticketStore'
import type { CommandOutcome, CommandSender } from '@/services/storytellerCommands'
import StatusStrip from '@/features/storyteller/StatusStrip.vue'
import StepDigest from '@/features/storyteller/StepDigest.vue'
import SeatChangeTimeline from '@/features/storyteller/SeatChangeTimeline.vue'
import SeatLedgerPanel from '@/features/storyteller/SeatLedgerPanel.vue'
import EffectChainPanel from '@/features/storyteller/EffectChainPanel.vue'
import LedgerPanel from '@/features/storyteller/LedgerPanel.vue'
import AssignmentControl from '@/features/storyteller/AssignmentControl.vue'
import DayControl from '@/features/storyteller/DayControl.vue'
import TravellerControl from '@/features/storyteller/TravellerControl.vue'
import PitHagNightPanel from '@/features/storyteller/PitHagNightPanel.vue'
import OperationsControl from '@/features/storyteller/OperationsControl.vue'
import GrimoireView from '@/features/storyteller/GrimoireView.vue'
import GrimoireDataDrawer from '@/features/storyteller/GrimoireDataDrawer.vue'
import ReplayPanel from '@/features/replay/ReplayPanel.vue'
import { computed, onBeforeUnmount, onMounted, ref, shallowRef } from 'vue'

/**
 * 兜底席位：只在服务端读数取不到时使用（`readSeatCount` 返回 null）。真正的来源是服务端的
 * `GameServer:SeatCount`——两边分叉会让说书人给不出最后一席的角色，所以不在这里另设配置口径。
 */
const seatCount = ref(DEFAULT_SEAT_COUNT)

/**
 * 拉取服务端配置的席位数量。取不到就沿用兜底值并显式告知——绝不静默按 5 席渲染：
 * 那样服务端配 6 席时会少画一席，说书人给不出角色、最后一名玩家进不了局。
 */
async function syncSeatCount(): Promise<void> {
  const configured = await readSeatCount()
  if (configured === null) {
    pushDiagnostic(`没能从服务端读到席位数量，暂按 ${seatCount.value} 席显示；刷新页面可重试`)
    return
  }

  if (configured !== seatCount.value) {
    seatCount.value = configured
  }
}

const ticket = ref('')

/**
 * 开桌（D-0026：**登录即可**，开完自己就是这一桌的说书人）。
 *
 * 说书人拿到票据的路径此前只有"服务器建好那一桌、从日志里抄票据"——多桌之后这条路径不够用了：
 * 一桌一份票据，必须有界面能开新桌并把票据交给开桌的人。
 * 这条路径也是"说书人是玩这一局的角色、不是系统权限"的落点：默认任何登录账号都能开。
 */
const lobbyName = ref('')
const lobbySeats = ref(7)
const lobbyBusy = ref(false)
const lobbyNotice = ref('')
const newTable = ref<LobbyCreateResult | null>(null)
const lobbyProfile = ref<AccountProfile | null>(null)
const lobbyRecoveryCode = ref('')
let accountForLobby: AccountGateway | null = null

/**
 * 现在能不能开桌：先要登录（开桌要记在某个账号头上），再看服务端给的能力位
 * （部署方可以关掉自助开桌，此时只有运维能开——D-0026）。
 */
const canOpenTable = computed(() => lobbyProfile.value !== null && lobbyProfile.value.canCreateTable)

function ensureLobbyAccount(): AccountGateway {
  accountForLobby ??= new AccountGateway()
  return accountForLobby
}

/** 账号回执的失败文案（成功由调用方各自描述）。 */
function lobbyFailureText(code: string, message: string): string {
  if (message.length > 0) {
    return message
  }

  switch (code) {
    case 'invalid_credentials':
      return '登录名或口令不对'
    case 'username_taken':
      return '这个登录名已经被占用'
    case 'invalid_session':
      return '账号会话已过期，请重新登录'
    default:
      return `未成功（${code}）`
  }
}

async function registerForLobby(username: string, displayName: string, password: string): Promise<void> {
  lobbyBusy.value = true
  lobbyRecoveryCode.value = ''
  try {
    const result = await ensureLobbyAccount().register(username, displayName, password)
    if (!result.ok) {
      lobbyNotice.value = lobbyFailureText(result.code, result.message)
      return
    }

    lobbyProfile.value = ensureLobbyAccount().profile
    lobbyRecoveryCode.value = result.recoveryCode ?? ''
    lobbyNotice.value = result.canCreateTable
      ? '已注册并登录：可以开一桌自己主持'
      : '已注册并登录；本服当前不开放自助开桌（要开桌请联系运维）'
  } catch (error) {
    lobbyNotice.value = `注册失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    lobbyBusy.value = false
  }
}

async function loginForLobby(username: string, password: string): Promise<void> {
  lobbyBusy.value = true
  try {
    const result = await ensureLobbyAccount().login(username, password)
    if (!result.ok) {
      lobbyNotice.value = lobbyFailureText(result.code, result.message)
      return
    }

    lobbyProfile.value = ensureLobbyAccount().profile
    // 恢复码是**上一个账号**的一次性秘密：换账号时不清就会留在页面上（秘密卫生，照 PlayerPanel 的口径）。
    lobbyRecoveryCode.value = ''
    lobbyNotice.value = result.canCreateTable
      ? '已登录：可以开一桌自己主持'
      : '已登录；本服当前不开放自助开桌（要开桌请联系运维）'
  } catch (error) {
    lobbyNotice.value = `登录失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    lobbyBusy.value = false
  }
}

async function logoutForLobby(): Promise<void> {
  lobbyBusy.value = true
  try {
    await ensureLobbyAccount().logout()
    lobbyProfile.value = null
    // 登出即清掉一次性恢复码：它属于刚登出的那个账号，不该留在屏幕上给下一个人看。
    lobbyRecoveryCode.value = ''
    lobbyNotice.value = '已登出'
  } finally {
    lobbyBusy.value = false
  }
}

async function renameForLobby(displayName: string): Promise<void> {
  if (displayName.length === 0) {
    lobbyNotice.value = '玩家名不能为空'
    return
  }

  lobbyBusy.value = true
  try {
    const result = await ensureLobbyAccount().changeDisplayName(displayName)
    if (!result.ok) {
      lobbyNotice.value = lobbyFailureText(result.code, result.message)
      return
    }

    lobbyProfile.value = ensureLobbyAccount().profile
    lobbyNotice.value = `玩家名已改为「${result.displayName}」`
  } finally {
    lobbyBusy.value = false
  }
}

async function resetForLobby(username: string, recoveryCode: string, newPassword: string): Promise<void> {
  lobbyBusy.value = true
  try {
    const result = await ensureLobbyAccount().resetPassword(username, recoveryCode, newPassword)
    if (!result.ok) {
      lobbyNotice.value = lobbyFailureText(result.code, result.message)
      return
    }

    lobbyProfile.value = ensureLobbyAccount().profile
    lobbyRecoveryCode.value = result.recoveryCode ?? ''
    lobbyNotice.value = '口令已重置并重新登录'
  } finally {
    lobbyBusy.value = false
  }
}

async function openTable(): Promise<void> {
  lobbyBusy.value = true
  lobbyNotice.value = ''
  newTable.value = null
  try {
    accountForLobby ??= new AccountGateway()
    const result = await accountForLobby.createTable(lobbyName.value.trim(), lobbySeats.value)
    if (!result.ok) {
      lobbyNotice.value = `开桌被拒：${result.message.length > 0 ? result.message : result.code}`
      return
    }

    newTable.value = result
    if (result.storytellerTicket !== null) {
      // 开桌者就是这一桌的说书人：票据直接填进上面的输入框，省掉"抄一串再粘回来"这一步。
      // 落盘发生在点「加入」时（`join()` 里写 TicketStore），所以这句提示不提前说"已存好"。
      ticket.value = `${result.gameId}:${result.storytellerTicket}`
      lobbyNotice.value =
        `已开桌：${result.gameId}（${result.seatCount} 席）。票据已填进上面的输入框——` +
        '点「加入」即进主持台，票据会在那时记到本机。'
      return
    }

    // 没拿到票据 = 开得出桌却主持不了（协议不该这样），明说而不是让人对着空输入框发呆。
    lobbyNotice.value = `已开桌：${result.gameId}，但服务端没有返回说书人票据——请联系运维。`
  } catch (error) {
    lobbyNotice.value = `开桌失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    lobbyBusy.value = false
  }
}
const store = new TicketStore()
const view = ref<StorytellerViewDto | null>(null)
const connectionState = ref<GatewayState>('disconnected')
const diagnostics = ref<string[]>([])
const outcome = ref<CommandOutcome | null>(null)
/** 回执序号：每次收到新回执自增。它是"这轮回执是不是新的"的判据——kind 可能重复（两次都 Accepted）。 */
const outcomeSerial = ref(0)
const joining = ref(false)
/** 复盘面板开关：说书人是实时面（进行中也能看；R-0043 第 3 条）。 */
const replayOpen = ref(false)

let gateway: StorytellerGateway | null = null

/** 网关实例的响应式引用：命令发送方要随它计算。 */
const gatewayRef = shallowRef<StorytellerGateway | null>(null)

/** 当前连接级凭据（内存态；重连换新凭据后由 onView 回调刷新）。 */
const credential = ref('')

/**
 * 命令发送方 = 连接 + 连接级凭据。
 * 只有"已连接 + 凭据在手"时才存在：没有凭据就不给任何发命令的入口（D-0012）。
 */
const sender = computed<CommandSender | null>(() => {
  const current = gatewayRef.value
  return connectionState.value === 'connected' && current !== null && credential.value.length > 0
    ? { connection: current.raw, credential: credential.value }
    : null
})

/** 复盘取数：交给共用面板（服务端按事件序号分页；说书人随时可读，玩家面由服务端闸）。 */
function fetchReplay(afterSequence: number, pageSize: number): Promise<ReplayViewDto> {
  const current = gateway
  if (current === null) {
    return Promise.reject(new Error('尚未连接：不能读取复盘'))
  }

  return current.fetchReplay(afterSequence, pageSize)
}

const stateText: Record<GatewayState, string> = {
  disconnected: '未连接',
  connecting: '连接中',
  connected: '已连接',
  reconnecting: '重连中',
}

/** 重建报告旗标 → 人话（null = 该项没有结论，例如无快照）。 */
function equivalenceText(value: boolean | null): string {
  return value === null ? '—' : value ? '一致' : '不一致'
}

/** 重建报告旗标 → 数据属性；批次装置按属性断言，不解析文案。 */
function equivalenceAttr(value: boolean | null): string {
  return value === null ? '' : String(value)
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

const connected = computed(() => connectionState.value === 'connected' && view.value !== null)

/** 当前网关连的是哪一桌（票据里声明的）；换桌要重建连接。 */
let gatewayGameId: string | undefined

/** 丢弃当前网关（换桌或重连前调用）：必须等它真的停下来，否则新连接的 start 会撞上关闭过程。 */
async function dropGateway(): Promise<void> {
  const previous = gateway
  gateway = null
  gatewayRef.value = null
  if (previous !== null) {
    await previous.stop()
  }
}

function ensureGateway(gameId?: string): StorytellerGateway {
  if (gateway === null) {
    const created = new StorytellerGateway(
      {
        onView: (next) => {
          // 每次 Join（含重连后的重新加入）都换一条连接：凭据与视图一起刷新，绝不沿用旧连接的。
          credential.value = created.credential
          view.value = next
        },
        onState: (state) => {
          connectionState.value = state
        },
        onDiagnostic: (message) => pushDiagnostic(message),
      },
      undefined,
      gameId,
    )
    gateway = created
    gatewayRef.value = created
    gatewayGameId = gameId
  }

  return gateway
}

function pushDiagnostic(message: string): void {
  diagnostics.value = [message, ...diagnostics.value].slice(0, 5)
}

async function join(): Promise<void> {
  joining.value = true
  try {
    // 票据可以写成 `桌标识:票据`（开桌时给出的就是这种自描述写法）：这样面板知道该连哪一桌。
    const parsed = parseStorytellerTicket(ticket.value)
    store.write(ticket.value.trim())

    // 换桌（或首次）要重建连接：`?gameId=` 属于连接，不能复用连到别桌的那条。
    if (gateway !== null && gatewayGameId !== parsed.gameId) {
      await dropGateway()
    }

    const current = ensureGateway(parsed.gameId)
    await current.join(parsed.ticket)
    credential.value = current.credential
    outcome.value = null
  } catch (error) {
    pushDiagnostic(`加入失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    joining.value = false
  }
}

async function refresh(): Promise<void> {
  try {
    await ensureGateway().refresh()
  } catch (error) {
    pushDiagnostic(`刷新失败：${error instanceof Error ? error.message : String(error)}`)
  }
}

async function disconnect(): Promise<void> {
  await gateway?.stop()
  view.value = null
  credential.value = ''
}

/** 命令回执统一在这里展示：服务端的拒绝是信息，不是故障。 */
function showOutcome(result: CommandOutcome): void {
  outcome.value = result
  outcomeSerial.value += 1
  if (!result.ok) {
    pushDiagnostic(`命令未成功：${result.kind} ${result.message}`)
    return
  }

  // 命令成功但通知可能缺失（例如宿主动作不发推送）时，主动拉一次，保证面板不陈旧。
  void refresh()
}

onMounted(() => {
  void syncSeatCount()
  const remembered = store.read()
  if (remembered.length > 0) {
    ticket.value = remembered
    void join()
  }
})

onBeforeUnmount(() => {
  void gateway?.stop()
})
</script>

<template>
  <div class="shell">
    <section class="login panel" v-if="!connected">
      <h1>说书人上帝视角</h1>
      <p class="hint">
        说书人票据是主持一桌的凭据：开一桌新的会直接把票据给你；默认桌的票据由服务端在引导时生成
        （数据库 <span class="mono">Games.StorytellerTicket</span> 或启动日志）。
        本面板只是说书人端，玩家请用 <span class="mono">#player</span> 入口。
      </p>
      <div class="row">
        <input v-model="ticket" placeholder="说书人票据" spellcheck="false" data-testid="storyteller-ticket" @keyup.enter="join()" />
        <button type="button" class="primary" :disabled="joining" @click="join()">加入</button>
      </div>

      <!-- 开桌（D-0026）：登录即可，开完自己就是这一桌的说书人；部署方可以关掉自助开桌。 -->
      <details class="lobby-open" data-testid="storyteller-open-table">
        <summary>开一桌新的（自己主持）</summary>
        <AccountPanel
          :profile="lobbyProfile"
          :busy="lobbyBusy"
          :notice="lobbyNotice"
          :recovery-code="lobbyRecoveryCode"
          @register="registerForLobby"
          @login="loginForLobby"
          @logout="logoutForLobby"
          @rename="renameForLobby"
          @reset="resetForLobby"
        />
        <div class="row">
          <input v-model="lobbyName" placeholder="桌名（可留空）" spellcheck="false" data-testid="open-table-name" />
          <input
            v-model.number="lobbySeats"
            type="number"
            min="1"
            max="20"
            class="seats-input"
            data-testid="open-table-seats"
          />
          <button
            type="button"
            :disabled="lobbyBusy || !canOpenTable"
            data-testid="open-table-submit"
            @click="openTable()"
          >
            开桌
          </button>
        </div>
        <p v-if="!canOpenTable" class="hint" data-testid="open-table-blocked">
          {{
            lobbyProfile === null
              ? '开桌要先登录：这一桌会记在你名下，票据也只回给你。'
              : '本服当前不开放自助开桌，请联系运维开桌。'
          }}
        </p>
        <p v-if="lobbyNotice.length > 0" class="hint" data-testid="open-table-notice">{{ lobbyNotice }}</p>
        <p v-if="newTable !== null && newTable.storytellerTicket !== null" class="hint">
          这一桌是 <strong>{{ newTable.gameId }}</strong>。下面这串是它的说书人票据
          （点「加入」后记到本机；换设备要重新开一桌或另找运维）——直接填到上面的输入框里即可进入主持台：
          <span class="mono" data-testid="new-table-ticket">{{
            `${newTable.gameId}:${newTable.storytellerTicket}`
          }}</span>
        </p>
      </details>
      <p class="hint">连接状态：{{ stateText[connectionState] }}</p>
      <ul v-if="diagnostics.length > 0" class="diagnostics">
        <li v-for="message in diagnostics" :key="message">{{ message }}</li>
      </ul>
    </section>

    <template v-else>
      <StatusStrip :view="view!" />
      <!-- 本局结束（R-0024）：胜方 + 条件 + 说明。结束后服务端拒绝一切新命令，
           面板仍可查看状态与重建——D-0014 的兜底入口不因结束而关闭。 -->
      <section
        v-if="view!.outcome"
        class="panel ended"
        data-testid="storyteller-outcome"
        :data-outcome-winner="view!.outcome.winner"
      >
        <strong>本局结束：{{ winnerLabelOf(view!.outcome.winner) }}</strong>
        <span class="mono">{{ view!.outcome.condition }}</span>
        <span>{{ view!.outcome.detail }}</span>
      </section>
      <!-- 呆瓜的公开选择（含"没选"的跳过）：公开事实，玩家端也看得到（R-0027）。 -->
      <section v-if="view!.klutzChoices.length > 0" class="panel" data-testid="storyteller-klutz-choices">
        <strong>呆瓜的公开选择</strong>
        <ul>
          <li v-for="choice in view!.klutzChoices" :key="choice.sequence">{{ choice.detail }}</li>
        </ul>
      </section>
      <!-- 降级位：恢复失败 = 数据可能已丢。它只说书人可见（玩家投影里没有此字段，D-0012 §4.3），
           且服务端在显式重建成功前不会清除——说书人必须先看见它，才谈得上兜底。 -->
      <section v-if="view!.health.degraded" class="health panel" data-testid="room-health-degraded">
        <strong>房间数据已降级：数据可能已丢失</strong>
        <span v-if="view!.health.reason">{{ view!.health.reason }}</span>
        <span v-if="view!.health.since" class="mono">发生时间：{{ clockTimeOf(view!.health.since) }}</span>
        <span class="hint">用「重建房间」按事件日志恢复；重建成功前该标记不会清除。</span>
      </section>
      <div class="body">
        <main class="stage">
          <GrimoireView
            v-if="sender"
            :view="view!"
            :sender="sender"
            :seat-count="seatCount"
            @outcome="showOutcome"
          />
          <!-- 占位提示只在"连接了但还没拿到连接级凭据"时出现（此前 v-else 误绑在复盘开关上，
               于是复盘收起时它一直挂在魔典下面，说了一句与状态不符的话）。 -->
          <p v-else class="placeholder">已连接，但还没有可用的连接级凭据——先在登录区重新加入。</p>
          <ReplayPanel v-if="replayOpen" :fetch-replay="fetchReplay" @close="replayOpen = false" />
        </main>

        <aside class="dock">
          <div class="row">
            <button type="button" @click="refresh()">刷新视图</button>
            <button type="button" @click="disconnect()">断开</button>
            <button type="button" data-testid="storyteller-replay-open" @click="replayOpen = true">复盘</button>
            <span class="hint">连接：{{ stateText[connectionState] }}</span>
          </div>
          <div
            class="outcome"
            :class="outcome === null ? 'idle' : outcome.ok ? 'ok' : 'bad'"
            data-testid="outcome"
            :data-outcome-serial="outcomeSerial"
          >
            <template v-if="outcome === null">
              <span class="hint">尚未提交命令</span>
            </template>
            <template v-else>
              <strong>{{ labelOf(outcome.kind) }}</strong>
              <span v-if="outcome.sequence !== null && outcome.sequence > 0" class="mono">序号 {{ outcome.sequence }}</span>
              <span v-if="outcome.message">{{ outcome.message }}</span>
              <span class="marker" :data-outcome-marker="outcome.kind" hidden>#</span>
              <!-- 重建对比照实回给说书人：少了它，"重建成功"就还是半个结论（D-0014 能力 3）。 -->
              <span
                v-if="outcome.rebuild"
                class="rebuild"
                data-testid="rebuild-report"
                :data-machine-equivalent="equivalenceAttr(outcome.rebuild.machineEquivalent)"
                :data-snapshot-equivalent="equivalenceAttr(outcome.rebuild.snapshotEquivalent)"
                :data-ledger-equivalent="equivalenceAttr(outcome.rebuild.ledgerEquivalent)"
              >
                重建对比（重建前）：内存 {{ equivalenceText(outcome.rebuild.machineEquivalent) }}；快照
                {{ equivalenceText(outcome.rebuild.snapshotEquivalent) }}；状态账
                {{ equivalenceText(outcome.rebuild.ledgerEquivalent) }}
              </span>
            </template>
          </div>
          <DayControl v-if="sender" :view="view!" :sender="sender" @outcome="showOutcome" />
          <TravellerControl v-if="sender" :view="view!" :sender="sender" @outcome="showOutcome" />
          <PitHagNightPanel v-if="sender" :view="view!" :sender="sender" @outcome="showOutcome" />
          <OperationsControl v-if="sender" :view="view!" :sender="sender" @outcome="showOutcome" />
          <AssignmentControl
            v-if="sender"
            :view="view!"
            :sender="sender"
            :seat-count="seatCount"
            @outcome="showOutcome"
          />
          <ul v-if="diagnostics.length > 0" class="diagnostics">
            <li v-for="message in diagnostics" :key="message">{{ message }}</li>
          </ul>
        </aside>

        <section class="data">
          <GrimoireDataDrawer>
            <StepDigest :view="view!" />
            <SeatLedgerPanel :view="view!" />
            <SeatChangeTimeline :view="view!" />
            <EffectChainPanel :view="view!" />
            <LedgerPanel :view="view!" />
          </GrimoireDataDrawer>
        </section>
      </div>
    </template>
  </div>
</template>

<style scoped>
.shell {
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.login {
  max-width: 560px;
  margin: 40px auto;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

h1 {
  margin: 0;
  font-size: 20px;
}

.row {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}

.body {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(340px, 400px);
  gap: 10px;
  align-items: start;
}

.stage {
  grid-column: 1;
  grid-row: 1;
  min-width: 0;
}

.dock {
  grid-column: 2;
  grid-row: 1 / span 2;
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-width: 0;
}

.data {
  grid-column: 1;
  grid-row: 2;
  min-width: 0;
}

@media (max-width: 1180px) {
  .body {
    grid-template-columns: 1fr;
  }

  .stage,
  .dock,
  .data {
    grid-column: 1;
    grid-row: auto;
  }
}

.outcome {
  border-radius: 8px;
  padding: 6px 10px;
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  align-items: baseline;
}

/* 未提交过命令时也保留这一格：它是"命令回执区"，位置稳定，不随视图推送闪烁。 */
.outcome.idle {
  background: var(--paper);
  border: 1px dashed var(--line);
}

.outcome.ok {
  background: #e6f2ea;
  border: 1px solid #b9d9c6;
}

.outcome.bad {
  background: var(--accent-soft);
  border: 1px solid #e0b8ad;
}

.health {
  display: flex;
  flex-direction: column;
  gap: 2px;
  background: var(--accent-soft);
  border: 1px solid #e0b8ad;
}

.health strong {
  color: var(--warn);
}

.diagnostics {
  margin: 0;
  padding-left: 18px;
  color: var(--warn);
  font-size: 12px;
}
</style>
