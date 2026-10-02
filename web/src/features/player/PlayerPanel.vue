<script setup lang="ts">
/**
 * 玩家视图（同一 SPA 的另一套视图，D-0004）。
 *
 * 玩家只能看到服务端下发给他的东西：自己的席位、当前大阶段、发给自己的请求与信息类结果。
 * 看板 / 状态账 / 计划进度一概不下发——所以这里也不会有对应的代码路径（D-0013 §5）。
 */
import type { InformationResultDto, OperationRequestDto, PlayerViewDto } from '@/contracts/game'
import { labelOf } from '@/display/labels'
import { seatLabelOf } from '@/display/format'
import { PlayerGateway, type PlayerCallbacks } from '@/services/playerGateway'
import { TicketStore } from '@/services/ticketStore'
import { newIdempotencyKey } from '@/services/idempotency'
import type { GatewayState } from '@/services/connectionState'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

const ticket = ref('')
const store = new TicketStore()
const view = ref<PlayerViewDto | null>(null)
const pending = ref<OperationRequestDto | null>(null)
const informationResults = ref<InformationResultDto[]>([])
const connectionState = ref<GatewayState>('disconnected')
const diagnostics = ref<string[]>([])
const joining = ref(false)
const submitting = ref(false)
const selectedOption = ref('')
const note = ref('')

let gateway: PlayerGateway | null = null
let clientSequence = 0

const stateText: Record<GatewayState, string> = {
  disconnected: '未连接',
  connecting: '连接中',
  connected: '已连接',
  reconnecting: '重连中',
}

const connected = computed(() => connectionState.value === 'connected' && view.value !== null)

function ensureGateway(): PlayerGateway {
  gateway ??= new PlayerGateway(buildCallbacks())

  return gateway
}

function buildCallbacks(): PlayerCallbacks {
  return {
    onRequest: (request) => {
      pending.value = request
      selectedOption.value = request?.options[0]?.value ?? ''
    },
    onInformation: (information) => {
      if (information !== null) {
        informationResults.value = [...informationResults.value, information]
      }
    },
    onState: (state) => {
      connectionState.value = state
    },
    onDiagnostic: (message) => pushDiagnostic(message),
  }
}

function pushDiagnostic(message: string): void {
  diagnostics.value = [message, ...diagnostics.value].slice(0, 5)
}

async function join(): Promise<void> {
  joining.value = true
  try {
    store.write(ticket.value.trim())
    const joined = await ensureGateway().joinSeat(ticket.value.trim())
    view.value = joined
    clientSequence = 0
    informationResults.value = [...joined.informationResults]
  } catch (error) {
    pushDiagnostic(`加入失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    joining.value = false
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

  submitting.value = true
  try {
    clientSequence += 1
    const raw = await ensureGateway().submitResponse(
      request.requestId,
      selectedOption.value,
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
    const joined = await ensureGateway().resync()
    view.value = joined
    informationResults.value = [...joined.informationResults]
    pushDiagnostic('已按自身序号重新补齐')
  } catch (error) {
    pushDiagnostic(`补齐失败：${error instanceof Error ? error.message : String(error)}`)
  }
}

async function disconnect(): Promise<void> {
  await gateway?.stop()
  view.value = null
  pending.value = null
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
    <section v-if="!connected" class="login panel">
      <h1>玩家端</h1>
      <p class="hint">席位票据由说书人分发。玩家端只会收到属于你自己的信息。</p>
      <div class="row">
        <input v-model="ticket" placeholder="席位票据" spellcheck="false" @keyup.enter="join()" />
        <button type="button" class="primary" :disabled="joining" @click="join()">加入</button>
      </div>
      <p class="hint">连接状态：{{ stateText[connectionState] }}</p>
      <ul v-if="diagnostics.length > 0" class="diagnostics">
        <li v-for="message in diagnostics" :key="message">{{ message }}</li>
      </ul>
    </section>

    <template v-else>
      <header class="panel head">
        <div>
          <span class="tag">{{ seatLabelOf(view!.seat) }}</span>
          <strong>{{ labelOf(view!.phase) === '—' ? '阶段未知' : labelOf(view!.phase) }}</strong>
        </div>
        <div class="row">
          <button type="button" @click="resync()">补齐</button>
          <button type="button" @click="disconnect()">断开</button>
          <span class="hint">连接：{{ stateText[connectionState] }}</span>
        </div>
      </header>

      <section class="panel">
        <h2>当前请求</h2>
        <div v-if="pending === null" class="placeholder">
          现在没有需要你做的事。夜晚是统一界面：不会有"轮到谁 / 还有几步"的提示。
        </div>
        <template v-else>
          <p class="context">{{ pending.context }}</p>
          <div class="options">
            <label v-for="option in pending.options" :key="option.value" class="option">
              <input v-model="selectedOption" type="radio" :value="option.value" />
              {{ option.preview }}
            </label>
          </div>
          <input v-model="note" placeholder="备注（可选）" />
          <button type="button" class="primary" :disabled="submitting" @click="submit()">提交</button>
        </template>
      </section>

      <section class="panel">
        <h2>我收到的信息</h2>
        <div v-if="informationResults.length === 0" class="placeholder">还没有收到信息。</div>
        <ul v-else class="information">
          <li v-for="(information, index) in informationResults" :key="`${index}-${information.ability}`">
            <span class="mono">{{ information.ability }}</span>
            <span>{{ information.content }}</span>
          </li>
        </ul>
        <p class="hint">信息可能是错的——说书人对醉酒 / 中毒玩家的信息有裁量权（D-0002）。</p>
      </section>

      <ul v-if="diagnostics.length > 0" class="diagnostics">
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

.diagnostics {
  margin: 0;
  padding-left: 18px;
  color: var(--warn);
  font-size: 12px;
}
</style>
