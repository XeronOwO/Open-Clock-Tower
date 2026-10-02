<script setup lang="ts">
/**
 * 说书人上帝视角面板：把服务端已经算好的每一步摘要、状态账与归因、两本账、结算结论、
 * 裁定点与卡点操作、以及兜底控制全部呈现出来。
 *
 * 信息姿态：本面板只显示服务端下发的说书人视图（D-0012：视图由服务端重新投影）；
 * 前端不做领域推断，也不缓存旧值假装"还是那样"——掉线重连后整份重取。
 * 零信任姿态：命令必须带连接级凭据；凭据只在内存里，不渲染、不落盘。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { labelOf } from '@/display/labels'
import { StorytellerGateway, type GatewayState } from '@/services/storytellerGateway'
import { TicketStore } from '@/services/ticketStore'
import type { CommandOutcome, CommandSender } from '@/services/storytellerCommands'
import StatusStrip from '@/features/storyteller/StatusStrip.vue'
import StepDigest from '@/features/storyteller/StepDigest.vue'
import DecisionPanel from '@/features/storyteller/DecisionPanel.vue'
import SeatChangeTimeline from '@/features/storyteller/SeatChangeTimeline.vue'
import SeatLedgerPanel from '@/features/storyteller/SeatLedgerPanel.vue'
import EffectChainPanel from '@/features/storyteller/EffectChainPanel.vue'
import LedgerPanel from '@/features/storyteller/LedgerPanel.vue'
import AssignmentControl from '@/features/storyteller/AssignmentControl.vue'
import StateReportControl from '@/features/storyteller/StateReportControl.vue'
import OperationsControl from '@/features/storyteller/OperationsControl.vue'
import { computed, onBeforeUnmount, onMounted, ref, shallowRef } from 'vue'

/** 席位名单是会话信息（服务端持有）。真实服务端的席位数量由配置决定，可用 VITE_SEAT_COUNT 覆盖。 */
function configuredSeatCount(): number {
  const raw = import.meta.env['VITE_SEAT_COUNT']
  const parsed = typeof raw === 'string' ? Number.parseInt(raw, 10) : Number.NaN
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 5
}

const seatCount = configuredSeatCount()

const ticket = ref('')
const store = new TicketStore()
const view = ref<StorytellerViewDto | null>(null)
const connectionState = ref<GatewayState>('disconnected')
const diagnostics = ref<string[]>([])
const outcome = ref<CommandOutcome | null>(null)
/** 回执序号：每次收到新回执自增。它是"这轮回执是不是新的"的判据——kind 可能重复（两次都 Accepted）。 */
const outcomeSerial = ref(0)
const joining = ref(false)

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

const stateText: Record<GatewayState, string> = {
  disconnected: '未连接',
  connecting: '连接中',
  connected: '已连接',
  reconnecting: '重连中',
}

const connected = computed(() => connectionState.value === 'connected' && view.value !== null)

function ensureGateway(): StorytellerGateway {
  if (gateway === null) {
    const created = new StorytellerGateway({
      onView: (next) => {
        // 每次 Join（含重连后的重新加入）都换一条连接：凭据与视图一起刷新，绝不沿用旧连接的。
        credential.value = created.credential
        view.value = next
      },
      onState: (state) => {
        connectionState.value = state
      },
      onDiagnostic: (message) => pushDiagnostic(message),
    })
    gateway = created
    gatewayRef.value = created
  }

  return gateway
}

function pushDiagnostic(message: string): void {
  diagnostics.value = [message, ...diagnostics.value].slice(0, 5)
}

async function join(): Promise<void> {
  joining.value = true
  try {
    store.write(ticket.value.trim())
    const current = ensureGateway()
    await current.join(ticket.value.trim())
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
        说书人票据由服务端在引导时生成（数据库 <span class="mono">Games.StorytellerTicket</span>
        或启动日志）。票据就是身份：本面板只是说书人端，玩家请用
        <span class="mono">#player</span> 入口。
      </p>
      <div class="row">
        <input v-model="ticket" placeholder="说书人票据" spellcheck="false" @keyup.enter="join()" />
        <button type="button" class="primary" :disabled="joining" @click="join()">加入</button>
      </div>
      <p class="hint">连接状态：{{ stateText[connectionState] }}</p>
      <ul v-if="diagnostics.length > 0" class="diagnostics">
        <li v-for="message in diagnostics" :key="message">{{ message }}</li>
      </ul>
    </section>

    <template v-else>
      <StatusStrip :view="view!" />
      <div class="body">
        <aside class="controls">
          <div class="row">
            <button type="button" @click="refresh()">刷新视图</button>
            <button type="button" @click="disconnect()">断开</button>
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
            </template>
          </div>
          <AssignmentControl
            v-if="sender"
            :view="view!"
            :sender="sender"
            :seat-count="seatCount"
            @outcome="showOutcome"
          />
          <OperationsControl v-if="sender" :view="view!" :sender="sender" @outcome="showOutcome" />
          <DecisionPanel v-if="sender" :view="view!" :sender="sender" @outcome="showOutcome" />
          <StateReportControl
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

        <main class="board">
          <StepDigest :view="view!" />
          <SeatLedgerPanel :view="view!" />
          <SeatChangeTimeline :view="view!" />
          <EffectChainPanel :view="view!" />
          <LedgerPanel :view="view!" />
        </main>
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
  grid-template-columns: minmax(320px, 420px) 1fr;
  gap: 10px;
  align-items: start;
}

@media (max-width: 1100px) {
  .body {
    grid-template-columns: 1fr;
  }
}

.controls {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.board {
  display: flex;
  flex-direction: column;
  gap: 10px;
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

.diagnostics {
  margin: 0;
  padding-left: 18px;
  color: var(--warn);
  font-size: 12px;
}
</style>
