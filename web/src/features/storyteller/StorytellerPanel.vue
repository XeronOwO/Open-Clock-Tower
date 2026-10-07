<script setup lang="ts">
/**
 * 说书人上帝视角面板的容器：门 / 我的桌 / 命令回执 / 布局装配。
 *
 * 主视图是魔典圆环（`GrimoireView`，以席位为中心）；数据与审计收在可展开的下钻面板里
 * （与主视图同一份视图，矩阵行 6）；局务（兜底与推进 / 开局分配）在右列。
 *
 * **进主持台凭账号，不凭票据**（D-0027）：没登录时页面上只有一张登录卡；登录后先看到
 * 「我主持的桌」（服务端按 `CreatedByAccountId` 算好），点进去才连这一桌。
 * 于是换设备 / 清缓存之后，登录同一账号就能回来——票据整串凭据已经不存在了。
 *
 * 信息姿态：本面板只显示服务端下发的说书人视图（D-0012：视图由服务端重新投影）；
 * 前端不做领域推断，也不缓存旧值假装"还是那样"——掉线重连后整份重取。
 * 零信任姿态：命令必须带连接级凭据；**连接级**凭据只在内存里，不渲染、不落盘（D-0012）。
 * M1（D-0029）：账号会话进 `sessionStorage`，**刷新后自动接回主持台**；
 * 接不回去就说清楚并清掉位置，不装作还在主持。
 */
import type { ReplayViewDto, StorytellerViewDto } from '@/contracts/game'
import { clockTimeOf } from '@/display/format'
import { labelOf } from '@/display/labels'
import { StorytellerGateway, type GatewayState } from '@/services/storytellerGateway'
import type { LobbyTable } from '@/services/accountGateway'
import * as session from '@/services/accountSession'
import AccountGate from '@/features/account/AccountGate.vue'
import AccountPanel from '@/features/account/AccountPanel.vue'
import { readSeatCount, DEFAULT_SEAT_COUNT } from '@/services/serverConfig'
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
import TableAccessControl from '@/features/storyteller/TableAccessControl.vue'
import PitHagNightPanel from '@/features/storyteller/PitHagNightPanel.vue'
import OperationsControl from '@/features/storyteller/OperationsControl.vue'
import GrimoireView from '@/features/storyteller/GrimoireView.vue'
import GrimoireDataDrawer from '@/features/storyteller/GrimoireDataDrawer.vue'
import ReplayPanel from '@/features/replay/ReplayPanel.vue'
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'

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

/**
 * 我主持的桌（D-0027）：服务端按 `CreatedByAccountId` 算好的归属列表。
 *
 * 它是"换设备回来"的落点——此前说书人身份是一串只在开桌回执里出现一次的票据，
 * 换台设备就再也进不去那张桌；现在桌跟着账号走。
 */
const myTables = ref<LobbyTable[]>([])
const tablesBusy = ref(false)
const tablesNotice = ref('')

/**
 * 本桌的访问模式（D-0037）：null = 还不知道（大厅列表里没这一行、也还没收到推送）。
 *
 * 权威读取口是**大厅列表**（补全初始条件的正路）；`ReceiveTableAccessChanged` 只负责"不刷新不重连就变"。
 * 查不到就留 null：界面说"—"，绝不把"不知道"写成"公开桌"。
 */
const inviteOnly = ref<boolean | null>(null)

/** 从一份桌列表里取初值；没这一行就保持 null（不猜）。 */
function syncInviteOnlyFrom(tables: readonly LobbyTable[], gameId: string | undefined): void {
  if (gameId === undefined) {
    return
  }

  const row = tables.find((table) => table.gameId === gameId)
  if (row !== undefined) {
    inviteOnly.value = row.inviteOnly
  }
}

/** 开桌（D-0026：**登录即可**；D-0027：开完这一桌就记在你名下）。 */
const openName = ref('')
const openSeats = ref(7)
const openNotice = ref('')

const profile = session.profile
const accountBusy = session.busy

/** 现在能不能开桌：先要登录，再看服务端给的能力位（部署方可以关掉自助开桌——D-0026）。 */
const canOpenTable = computed(() => profile.value !== null && profile.value.canCreateTable)

/** 读「我主持的桌」；未登录时清空（没有"我"，也就没有我的桌）。 */
async function loadMyTables(): Promise<void> {
  if (profile.value === null) {
    myTables.value = []
    return
  }

  tablesBusy.value = true
  try {
    const tables = await session.listTables()
    myTables.value = tables.filter((table) => table.createdByMe)
    // 列表是访问模式的权威读取口（D-0037）：进桌时那一行就是初值，推送只负责"当场变"。
    syncInviteOnlyFrom(myTables.value, gatewayGameId.value)
    tablesNotice.value = ''
  } catch (error) {
    tablesNotice.value = `读取「我主持的桌」失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    tablesBusy.value = false
  }
}

async function openTable(): Promise<void> {
  openNotice.value = ''
  const result = await session.createTable(openName.value.trim(), openSeats.value)
  if (!result.ok) {
    openNotice.value = `开桌被拒：${result.message.length > 0 ? result.message : result.code}`
    return
  }

  openNotice.value = `已开桌：${result.gameId}（${result.seatCount} 席）——它在下面的「我主持的桌」里，点「进主持台」即可。`
  openName.value = ''
  await loadMyTables()
}
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

/** 自动接回主持台只做一次（M1）：成功或失败都不再重试，免得每次登录态变化都重放一遍失败。 */
let resumedHost = false

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
  disconnected: '还没连上',
  connecting: '正在连',
  connected: '已连上',
  reconnecting: '正在重连',
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
/** 当前网关连的是哪一桌（进桌时定下来）；换桌要重建连接。做成 ref：邀请码要读它。 */
const gatewayGameId = ref<string | undefined>(undefined)

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
        // 访问模式变了（D-0037）：说书人自己拨完开关也会收到这条（广播覆盖本桌全部连接）。
        // 只认本桌：别的桌的读数不该改这一行。
        onTableAccess: (access) => {
          if (access.gameId === gatewayGameId.value) {
            inviteOnly.value = access.inviteOnly
          }
        },
      },
      undefined,
      gameId,
    )
    gateway = created
    gatewayRef.value = created
    gatewayGameId.value = gameId
  }

  return gateway
}

function pushDiagnostic(message: string): void {
  diagnostics.value = [message, ...diagnostics.value].slice(0, 5)
}

/**
 * 进这一桌的主持台（D-0027）：出示账号会话，服务端判定"你是不是开这一桌的账号"。
 *
 * 桌标识必须是这一桌的：`?gameId=` 属于连接（多桌 D-0024），所以换桌要重建连接。
 * 返回是否真的进去了——自动接回（M1）要据此决定"位置还留不留"。
 */
async function enterTable(gameId: string): Promise<boolean> {
  const current = profile.value
  if (current === null) {
    pushDiagnostic('还没登录：请先登录，再进主持台')
    return false
  }

  joining.value = true
  try {
    if (gateway !== null && gatewayGameId.value !== gameId) {
      await dropGateway()
    }

    const gatewayForTable = ensureGateway(gameId)
    await gatewayForTable.joinWithAccount(current.accountSession)
    credential.value = gatewayForTable.credential
    outcome.value = null
    // 进桌即取访问模式初值（D-0037）：列表已经读回来了就用它，读不到就等推送 / 显示"—"。
    syncInviteOnlyFrom(myTables.value, gameId)
    // 记住"我正在主持哪一桌"（M1 / D-0029）：刷新回来直接接回主持台，不用再从列表里点一次。
    session.rememberTable({ surface: 'storyteller', gameId, seat: null })
    return true
  } catch (error) {
    pushDiagnostic(`进主持台失败：${error instanceof Error ? error.message : String(error)}`)
    return false
  } finally {
    joining.value = false
  }
}

/**
 * 接回上一次主持的那一桌（M1 / D-0029）：刷新的正常路径。
 *
 * 接不回去就**清掉位置并说明**——留着一条永远接不回去的记录，只会让每次刷新都重演同一个失败。
 */
async function resumeTable(): Promise<void> {
  if (resumedHost || connected.value || profile.value === null) {
    return
  }

  const target = session.activeTable.value
  if (target === null || target.surface !== 'storyteller') {
    return
  }

  resumedHost = true
  if (await enterTable(target.gameId)) {
    return
  }

  session.forgetTable()
  pushDiagnostic(`没能接回「${target.gameId}」的主持台：这一桌可能已经不在了，或你的账号不再是它的开桌人`)
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
  // 离开这一桌：访问模式的读数跟着清掉（它属于"本桌"，不属于说书人）。
  inviteOnly.value = null
  // 主动断开 = 主动离开这一桌（M1）：位置一起忘掉，别让下一次刷新又把人送回来。
  session.forgetTable()
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
})

// 登录 / 登出之后「我主持的桌」要跟着变：换了账号就不该看到上一个人的桌。
// 接回主持台也挂在这里（M1 / D-0029）：登录态是**异步**恢复的（`accountSession.restore`），
// 挂载那一刻还读不到"我是谁"，只能等它落定再决定接哪一桌。
// `immediate` 覆盖"页内切到这一面时已经登录"的情形——那正是"从别处回来要落在主持台"。
watch(
  profile,
  async () => {
    await loadMyTables()
    await resumeTable()
  },
  { immediate: true },
)

onBeforeUnmount(() => {
  void gateway?.stop()
})
</script>

<template>
  <div class="shell">
    <!-- 没登录：页面上只有一张账号卡（D-0027）。 -->
    <AccountGate v-if="!connected && profile === null" />

    <!-- 登录了但还没进桌：先看「我主持的桌」，或者开一桌新的。 -->
    <section v-else-if="!connected" class="home panel">
      <h1>主持一局</h1>
      <p class="hint">
        你开的桌都在下面；换台设备、清掉缓存，登录同一个账号就还认得你。
      </p>

      <div class="my-tables" data-testid="my-tables">
        <div class="row">
          <strong>我主持的桌</strong>
          <button type="button" :disabled="tablesBusy" @click="loadMyTables()">刷新</button>
          <span class="hint">共 {{ myTables.length }} 桌</span>
        </div>
        <p v-if="tablesNotice.length > 0" class="hint">{{ tablesNotice }}</p>
        <ul v-if="myTables.length > 0" class="tables">
          <li v-for="table in myTables" :key="table.gameId" :data-my-table="table.gameId">
            <span>{{ table.name.length > 0 ? table.name : table.gameId }}</span>
            <span class="hint">
              {{ table.takenSeatCount }} / {{ table.seatCapacity }} 人 ·
              {{ table.started ? '已开局' : '等人' }} · {{ table.inviteOnly ? '邀请制' : '公开' }}
            </span>
            <button type="button" data-testid="host-enter" :disabled="joining" @click="enterTable(table.gameId)">
              进主持台
            </button>
          </li>
        </ul>
        <p v-else class="hint">还没有开桌。下面开一桌，你就是这一桌的说书人。</p>
      </div>

      <!-- 开桌（D-0026）：登录即可；开完这一桌就记在你名下（D-0027），不需要抄任何东西。 -->
      <div class="row">
        <input v-model="openName" placeholder="桌名（可留空）" spellcheck="false" data-testid="open-table-name" />
        <input
          v-model.number="openSeats"
          type="number"
          min="1"
          max="20"
          class="seats-input"
          data-testid="open-table-seats"
        />
        <button
          type="button"
          :disabled="accountBusy || !canOpenTable"
          data-testid="open-table-submit"
          @click="openTable()"
        >
          开一桌
        </button>
      </div>
      <p v-if="!canOpenTable" class="hint" data-testid="open-table-blocked">
        本服当前不开放自助开桌，请联系运维开桌。
      </p>
      <p v-if="openNotice.length > 0" class="hint" data-testid="open-table-notice">{{ openNotice }}</p>

      <AccountPanel />
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
          <TableAccessControl
            v-if="sender"
            :invite-only="inviteOnly ?? false"
            :sender="sender"
            @update:invite-only="inviteOnly = $event"
            @outcome="showOutcome"
          />
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
