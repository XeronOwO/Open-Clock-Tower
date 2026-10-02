<script setup lang="ts">
/**
 * 裁定点与卡点操作：
 * - 裁定点（R-0009）：只呈现引擎算出的合法选项 + 自由决定输入；平台不替说书人拍板（D-0002）。
 * - 卡点（挂起请求）：说书人可代填或强制作废。
 * 命令必须带连接级凭据（D-0012）：组件只拿得到 `sender`，拿不到"裸连接"。
 */
import type { DecisionOptionDto, StorytellerViewDto } from '@/contracts/game'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  proxyFill,
  resolveDecisionPoint,
  voidRequest,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { seatLabelOf } from '@/display/format'
import { ref } from 'vue'

const props = defineProps<{ view: StorytellerViewDto; sender: CommandSender }>()
const emit = defineEmits<{ outcome: [CommandOutcome] }>()

const decisionNote = ref('')
const freeDecision = ref('')
const proxyOption = ref('')
const proxyNote = ref('')
const voidReason = ref('StorytellerForce')
const voidNote = ref('')
const busy = ref(false)

/** 取值与 Kernel 的 OperationRequestVoidReason 逐项一致；服务端只认枚举名。 */
const voidReasons = [
  { value: 'StorytellerForce', label: '说书人强制作废' },
  { value: 'StorytellerTakeover', label: '强推 / 接管切步了结' },
  { value: 'DependencyViolated', label: '座位依赖不再满足' },
  { value: 'PhaseAdvanced', label: '阶段已推进' },
  { value: 'Superseded', label: '被上游新请求取代' },
]

async function decide(decision: string | null): Promise<void> {
  const decisionPointId = props.view.awaitingDecisionId
  if (decisionPointId === null) {
    return
  }

  busy.value = true
  try {
    const outcome = await resolveDecisionPoint(
      props.sender,
      decisionPointId,
      decision,
      decisionNote.value.length > 0 ? decisionNote.value : null,
      newIdempotencyKey('decision'),
    )
    emit('outcome', outcome)
  } finally {
    busy.value = false
  }
}

async function fill(option: DecisionOptionDto | null): Promise<void> {
  const pending = props.view.pending
  if (pending === null) {
    return
  }

  const value = option?.value ?? proxyOption.value
  if (value.length === 0) {
    emit('outcome', { ok: false, kind: 'Rejected', sequence: null, message: '代填需要选一个合法选项' })
    return
  }

  busy.value = true
  try {
    const outcome = await proxyFill(
      props.sender,
      pending.requestId,
      value,
      proxyNote.value.length > 0 ? proxyNote.value : null,
      newIdempotencyKey('proxy'),
    )
    emit('outcome', outcome)
  } finally {
    busy.value = false
  }
}

async function voidPending(): Promise<void> {
  const pending = props.view.pending
  if (pending === null) {
    return
  }

  busy.value = true
  try {
    const outcome = await voidRequest(
      props.sender,
      pending.requestId,
      voidReason.value,
      voidNote.value.length > 0 ? voidNote.value : null,
      newIdempotencyKey('void'),
    )
    emit('outcome', outcome)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="panel">
    <h2>裁定点与卡点</h2>

    <div v-if="view.awaitingDecisionId" class="block decision">
      <div class="line">
        <span class="tag warn">待裁定的裁定点</span>
        <span class="mono">{{ view.awaitingDecisionId }}</span>
      </div>
      <p class="context">{{ view.awaitingDecisionContext ?? '（服务端未提供上下文）' }}</p>

      <div v-if="(view.awaitingDecisionOptions ?? []).length > 0" class="options">
        <button
          v-for="option in view.awaitingDecisionOptions ?? []"
          :key="option.value"
          type="button"
          class="primary"
          :disabled="busy"
          @click="decide(option.value)"
        >
          {{ option.preview }}
        </button>
      </div>
      <p v-else class="hint">引擎没有给出候选选项——按 R-0009 由说书人自由决定。</p>

      <div class="free">
        <input v-model="freeDecision" placeholder="自由决定的内容（可为空）" />
        <button type="button" :disabled="busy" @click="decide(freeDecision.length > 0 ? freeDecision : null)">
          按自由决定结清
        </button>
      </div>
      <input v-model="decisionNote" placeholder="备注（可选，会记进事件流）" />
    </div>
    <div v-else class="placeholder">当前没有等待裁定的裁定点。</div>

    <div v-if="view.pending" class="block pending">
      <div class="line">
        <span class="tag warn">卡点</span>
        <strong>{{ seatLabelOf(view.pending.seat) }}</strong>
        <span class="mono">{{ view.pending.requestId }}</span>
      </div>
      <p class="hint">请求正文在玩家端；说书人这里只能代填或作废，看不到玩家的选择界面。</p>
      <div class="free">
        <input v-model="proxyOption" placeholder="代填的值（与合法选项一致）" />
        <button type="button" :disabled="busy" @click="fill(null)">代填</button>
      </div>
      <input v-model="proxyNote" placeholder="代填备注（可选）" />
      <div class="free">
        <select v-model="voidReason">
          <option v-for="reason in voidReasons" :key="reason.value" :value="reason.value">
            {{ reason.label }}（{{ reason.value }}）
          </option>
        </select>
        <button type="button" :disabled="busy" @click="voidPending()">强制作废</button>
      </div>
      <input v-model="voidNote" placeholder="作废说明（可选）" />
    </div>
    <div v-else class="placeholder">当前没有卡住的请求。</div>
  </section>
</template>

<style scoped>
.block {
  border-top: 1px dashed var(--line);
  padding-top: 8px;
  margin-top: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.line {
  display: flex;
  gap: 8px;
  align-items: center;
}

.options {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.free {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.context {
  margin: 0;
}
</style>
