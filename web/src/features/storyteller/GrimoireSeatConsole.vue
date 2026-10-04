<script setup lang="ts">
/**
 * 席位操作台：把"当前必须处理的事"（卡点 / 待裁定）与"选中席位"的归因、效果链接、
 * 状态上报收在同一块面板里，贴着圆环摆放（`docs/architecture/storyteller-presentation.md` §4）。
 *
 * 命令口径不变（D-0012）：只拿得到 `sender`（连接 + 连接级凭据），拿不到裸连接；
 * 卡点 / 裁定 / 上报与旧面板是同一批命令，只是入口按席位就近呈现，不再有第二份实现。
 */
import type { EffectDto, StorytellerViewDto } from '@/contracts/game'
import GrimoireAnnotationControl from '@/features/storyteller/GrimoireAnnotationControl.vue'
import HelpTip from '@/features/common/HelpTip.vue'
import { ROSTER, characterLabelOf, dimensionLabelOf, labelOf } from '@/display/labels'
import { causedByLabelOf, seatDisplayOf, waitingSecondsTextOf } from '@/display/format'
import {
  buildSeatCard,
  decisionSeatOf,
  effectMarkLabel,
  optionSeatIsDead,
  seatNumbersOf,
  seatSummaryOf,
} from '@/display/grimoire'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  localFailure,
  proxyFill,
  punishExecution,
  reportSeatState,
  resolveDecisionPoint,
  voidRequest,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { computed, ref, watch } from 'vue'

const props = defineProps<{
  view: StorytellerViewDto
  sender: CommandSender
  /** 选中席位；null = 还没选。 */
  seat: number | null
  /** 席位名单是会话信息（服务端持有）；这里由宿主配置传入，客户端不猜。 */
  seatCount: number
}>()

const emit = defineEmits<{ outcome: [CommandOutcome]; locate: [number]; engage: [number] }>()

const model = computed(() => (props.seat === null ? null : buildSeatCard(props.view, props.seat)))

/** 待裁定归属的席位：与圆环高亮同一口径（服务端归属席位优先，再回退行动者 / 摘要）。 */
const decisionSeat = computed(() => decisionSeatOf(props.view))

const seatNumbers = computed(() => seatNumbersOf(props.view, props.seatCount))

// —— 卡点（代填 / 强制作废）——
const proxyOption = ref('')
const proxyNote = ref('')
const voidReason = ref('StorytellerForce')
const voidNote = ref('')

/**
 * 说书人**可手动选择**的作废原因（服务端只认枚举名），与 Kernel 枚举逐项核对过；
 * GameEnded 是系统专属原因（结束批次派发），不进这个下拉。
 */
const voidReasons = [
  { value: 'StorytellerForce', label: '说书人强制作废' },
  { value: 'StorytellerTakeover', label: '强推 / 接管切步了结' },
  { value: 'DependencyViolated', label: '座位依赖不再满足' },
  { value: 'PhaseAdvanced', label: '阶段已推进' },
  { value: 'Superseded', label: '被上游新请求取代' },
]

// —— 裁定点 ——
const decisionNote = ref('')
const freeDecision = ref('')

// —— 处罚处决（R-0020）——
/** 服务端只认枚举名：Cerenovus（洗脑师）/ Mutant（畸形秀演员）。 */
const punishSource = ref('Cerenovus')
const punishNote = ref('')
/** 低频危险操作默认收起（票据矩阵行 3）。 */
const punishOpen = ref(false)

// —— 状态上报 ——
const reason = ref('')
const causedBy = ref<number | ''>('')
const life = ref('')
const character = ref('')
const alignment = ref('')
const drunk = ref('')
const poison = ref('')
const useLife = ref(false)
const useCharacter = ref(false)
const useAlignment = ref(false)
const useDrunk = ref(false)
const usePoison = ref(false)

const busy = ref(false)

/**
 * 清空上报表单（勾选 + 取值 + 原因 + 归因）。
 * 换席必须**整表复位**：只清勾选不清取值，会把上一席的值静默带到下一席（对抗性复核 H-1）。
 */
function resetReportForm(): void {
  useLife.value = false
  useCharacter.value = false
  useAlignment.value = false
  useDrunk.value = false
  usePoison.value = false
  life.value = ''
  character.value = ''
  alignment.value = ''
  drunk.value = ''
  poison.value = ''
  reason.value = ''
  causedBy.value = ''
}

watch(() => props.seat, resetReportForm)

const observedCount = computed(
  () =>
    [useLife.value, useCharacter.value, useAlignment.value, useDrunk.value, usePoison.value].filter(
      Boolean,
    ).length,
)

/** 维度值 → 呈现文案：角色维度显示中文名，其它维度翻枚举。 */
function valueTextOf(dimension: string, value: string): string {
  return dimension === 'Character' ? characterLabelOf(value) : labelOf(value)
}

/**
 * 一条效果的一句话归因（矩阵行 5）：名称 + 来源 + 现状；终止原因写在同一个句子里，
 * 不再把 id / 能力 / 类型 / 状态逐字段罗列（id 仍以 mono 原样留在行尾，供核对）。
 */
function effectSentenceOf(effect: EffectDto): string {
  const name = effectMarkLabel(effect)
  const source =
    effect.source === props.seat ? '自己' : seatDisplayOf(effect.source, props.view.seatNames)
  const base = `${name}：来自 ${source}`

  if (!effect.terminated) {
    return `${base}，正在生效。`
  }

  const reason = effect.terminationReason === null ? '' : `（${effect.terminationReason}）`
  const causedBy =
    effect.terminationCausedBy === null
      ? ''
      : `；由 ${seatDisplayOf(effect.terminationCausedBy, props.view.seatNames)} 导致`
  return `${base}，已终止：${labelOf(effect.terminationKind)}${reason}${causedBy}。`
}

async function run(action: () => Promise<CommandOutcome>): Promise<CommandOutcome> {
  busy.value = true
  try {
    const outcome = await action()
    emit('outcome', outcome)
    return outcome
  } finally {
    busy.value = false
  }
}

function reject(message: string): void {
  emit('outcome', localFailure(message))
}

/** 用户开始填写上报表单 → 锁定"目标席位"（父组件把选中态钉在该席，对抗性复核 H-2）。 */
function engage(): void {
  if (props.seat !== null) {
    emit('engage', props.seat)
  }
}

/** 一键把圆环与操作台定位到某个"当前待办"的归属席位。 */
function locate(seat: number): void {
  emit('locate', seat)
}

/** 子组件的命令回执原样上传给宿主（回执出口仍然只有 `StorytellerPanel` 一处）。 */
function forwardOutcome(value: CommandOutcome): void {
  emit('outcome', value)
}

async function fill(): Promise<void> {
  const pending = props.view.pending
  if (pending === null) {
    return
  }

  if (proxyOption.value.length === 0) {
    reject('代填需要选一个合法选项')
    return
  }

  await run(() =>
    proxyFill(
      props.sender,
      pending.requestId,
      proxyOption.value,
      proxyNote.value.length > 0 ? proxyNote.value : null,
      newIdempotencyKey('proxy'),
    ),
  )
}

async function voidPending(): Promise<void> {
  const pending = props.view.pending
  if (pending === null) {
    return
  }

  await run(() =>
    voidRequest(
      props.sender,
      pending.requestId,
      voidReason.value,
      voidNote.value.length > 0 ? voidNote.value : null,
      newIdempotencyKey('void'),
    ),
  )
}

async function decide(decision: string | null): Promise<void> {
  const decisionPointId = props.view.awaitingDecisionId
  if (decisionPointId === null) {
    return
  }

  await run(() =>
    resolveDecisionPoint(
      props.sender,
      decisionPointId,
      decision,
      decisionNote.value.length > 0 ? decisionNote.value : null,
      newIdempotencyKey('decision'),
    ),
  )
}

/** 处罚处决选中席位：依据是否成立由内核按来源契约判定，前端只做参数拼装。 */
async function punish(): Promise<void> {
  const target = props.seat
  if (target === null) {
    reject('还没有选中席位')
    return
  }

  await run(() =>
    punishExecution(
      props.sender,
      target,
      punishSource.value,
      punishNote.value.length > 0 ? punishNote.value : null,
      newIdempotencyKey('punish'),
    ),
  )
}

async function submitReport(): Promise<void> {
  const target = props.seat
  if (target === null) {
    reject('还没有选中席位')
    return
  }

  if (reason.value.trim().length === 0) {
    reject('上报必须给出原因')
    return
  }

  if (observedCount.value === 0) {
    reject('至少勾选一个本次观测到的维度')
    return
  }

  // 勾了却没选值 = 这一维根本没上报：服务端把空串解析成 null，会"受理"但静默丢维度（复核 M-2）。
  const missing = [
    { used: useLife.value, value: life.value, label: '生死' },
    { used: useCharacter.value, value: character.value, label: '角色' },
    { used: useAlignment.value, value: alignment.value, label: '阵营' },
    { used: useDrunk.value, value: drunk.value, label: '醉酒' },
    { used: usePoison.value, value: poison.value, label: '中毒' },
  ]
    .filter((entry) => entry.used && entry.value.length === 0)
    .map((entry) => entry.label)
  if (missing.length > 0) {
    reject(`勾选的维度必须先选一个取值：${missing.join('、')}`)
    return
  }

  const outcome = await run(() =>
    reportSeatState(
      props.sender,
      {
        seat: target,
        life: useLife.value ? life.value : null,
        character: useCharacter.value ? character.value : null,
        alignment: useAlignment.value ? alignment.value : null,
        drunk: useDrunk.value ? drunk.value : null,
        poison: usePoison.value ? poison.value : null,
        reason: reason.value.trim(),
        causedBySeat: causedBy.value === '' ? null : causedBy.value,
      },
      newIdempotencyKey('report'),
    ),
  )

  // 受理后整表复位：下一次上报从干净状态开始，不带上一次的值。
  if (outcome.ok) {
    resetReportForm()
  }
}
</script>

<template>
  <section class="panel console" data-testid="seat-console" :data-console-seat="seat ?? ''">
    <h2>席位操作台<HelpTip topic="seat" /></h2>
    <p class="block-question">卡点 / 待裁定就近处理；选中席位的记录与上报都在这里。</p>

    <div v-if="view.pending" class="block pending" data-testid="console-pending">
      <div class="line">
        <span class="tag warn">卡点</span>
        <strong>{{ seatDisplayOf(view.pending.seat, view.seatNames) }}</strong>
        <span class="mono">{{ view.pending.requestId }}</span>
        <span v-if="waitingSecondsTextOf(view.pending.waitingSeconds)" class="hint">
          已等待 {{ waitingSecondsTextOf(view.pending.waitingSeconds) }}
        </span>
        <button v-if="seat !== view.pending.seat" type="button" @click="locate(view.pending.seat)">
          定位到 {{ seatDisplayOf(view.pending.seat, view.seatNames) }}
        </button>
      </div>
      <p class="hint">请求正文在玩家端；说书人这里只能代填或作废，看不到玩家的选择界面。</p>
      <div class="free">
        <input v-model="proxyOption" placeholder="代填的值（与合法选项一致）" />
        <button type="button" :disabled="busy" @click="fill()">代填</button>
      </div>
      <input v-model="proxyNote" placeholder="代填备注（可选）" />
      <div class="free">
        <select v-model="voidReason">
          <option v-for="choice in voidReasons" :key="choice.value" :value="choice.value">
            {{ choice.label }}（{{ choice.value }}）
          </option>
        </select>
        <button type="button" :disabled="busy" @click="voidPending()">强制作废</button>
      </div>
      <input v-model="voidNote" placeholder="作废说明（可选）" />
    </div>

    <div v-if="view.awaitingDecisionId" class="block decision" data-testid="console-decision">
      <div class="line">
        <span class="tag warn">待裁定的裁定点</span>
        <span class="mono">{{ view.awaitingDecisionId }}</span>
        <span v-if="decisionSeat !== null" class="hint" data-testid="console-decision-seat">
          归属：{{ seatDisplayOf(decisionSeat, view.seatNames) }}
        </span>
        <span v-else class="hint">（归属席位未知，就近在操作台处理）</span>
        <button
          v-if="decisionSeat !== null && decisionSeat !== seat"
          type="button"
          @click="locate(decisionSeat)"
        >
          定位到 {{ seatDisplayOf(decisionSeat, view.seatNames) }}
        </button>
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
          <!-- 席位候选的生死标注：只对 `seat:N` 选项按状态账打标，不影响候选集合（R-0039）。 -->
          <span
            v-if="optionSeatIsDead(view, option.value)"
            class="tag dead"
            data-testid="option-dead"
          >
            已死亡
          </span>
        </button>
      </div>
      <p v-else class="hint">引擎没有给出候选选项——按 R-0009 由说书人自由决定。</p>

      <div class="free">
        <input v-model="freeDecision" placeholder="自由决定的内容（可为空）" />
        <button
          type="button"
          :disabled="busy"
          @click="decide(freeDecision.length > 0 ? freeDecision : null)"
        >
          按自由决定结清
        </button>
      </div>
      <input v-model="decisionNote" placeholder="备注（可选，会记进事件流）" />
    </div>

    <div v-if="!view.pending && view.awaitingDecisionId === null" class="placeholder">
      当前没有卡点，也没有等待裁定的裁定点。
    </div>

    <template v-if="model">
      <div class="detail">
        <h3>{{ seatDisplayOf(model.seat, view.seatNames) }}</h3>
        <p class="summary" data-testid="console-seat-summary">
          {{ seatSummaryOf(seatDisplayOf(model.seat, view.seatNames), model) }}<HelpTip topic="status-ledger" />
        </p>

        <table v-if="model.facts.length > 0">
          <thead>
            <tr>
              <th>维度</th>
              <th>当前值</th>
              <th>来由</th>
              <th>归因</th>
              <th>效果</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="fact in model.facts" :key="fact.dimension">
              <td>{{ dimensionLabelOf(fact.dimension) }}</td>
              <td>{{ valueTextOf(fact.dimension, fact.value) }}</td>
              <td>{{ fact.reason }}</td>
              <td>{{ causedByLabelOf(fact.causedBy) ?? '—' }}</td>
              <td class="mono">{{ fact.effectId ?? '—' }}</td>
            </tr>
          </tbody>
        </table>
        <p v-else class="hint">该席位还没有可显示的维度事实（未观测 ≠ 默认值）。</p>

        <div v-if="model.effects.length > 0" class="effects">
          <div
            v-for="effect in model.effects"
            :key="effect.effectId"
            class="effect"
            :data-effect-id="effect.effectId"
          >
            <span v-if="!effect.terminated" class="tag good">生效中</span>
            <span v-else class="tag evil">已终止</span>
            <span>{{ effectSentenceOf(effect) }}</span>
            <span class="mono">{{ effect.effectId }}</span>
          </div>
        </div>

        <div v-if="model.madnesses.length > 0" class="line" data-testid="console-madnesses">
          <span class="tag warn">疯狂要求</span>
          <span v-for="requirement in model.madnesses" :key="requirement">{{ requirement }}</span>
          <HelpTip topic="madness" />
        </div>

        <GrimoireAnnotationControl
          :view="view"
          :sender="sender"
          :seat="seat"
          @outcome="forwardOutcome"
          @engage="engage()"
        />

        <div class="punish">
          <!-- 低频且不可逆：默认收起，点开才出现来源 / 说明 / 执行（票据矩阵行 3 的降密度）。 -->
          <button
            type="button"
            class="toggle"
            data-testid="console-punish-toggle"
            :aria-expanded="punishOpen ? 'true' : 'false'"
            @click="punishOpen = !punishOpen"
          >
            <span class="tag evil">处罚处决</span>
            <span class="hint">{{ punishOpen ? '收起' : '展开' }}</span>
          </button>
          <div v-show="punishOpen" class="punish-body">
            <div class="line">
              <select v-model="punishSource" data-testid="console-punish-source">
                <option value="Cerenovus">洗脑师：未按疯狂要求行动</option>
                <option value="Mutant">畸形秀演员：疯狂地证明自己是外来者</option>
              </select>
              <button type="button" :disabled="busy" data-testid="console-punish" @click="punish()">
                处罚处决 {{ seatDisplayOf(model.seat, view.seatNames) }}
              </button>
            </div>
            <input v-model="punishNote" placeholder="执行说明（可选，会记进事件流）" />
            <p class="hint">
              白天处罚会立即结束白天并占用当天处决上限；夜晚处罚不占次日上限（R-0020）。
            </p>
          </div>
        </div>

        <div class="report" @focusin="engage()">
          <div class="line">
            <span class="tag">上报到</span>
            <strong>{{ seatDisplayOf(model.seat, view.seatNames) }}</strong>
            <label class="inline">
              归因到
              <select v-model="causedBy">
                <option value="">（无人可归因）</option>
                <option v-for="number in seatNumbers" :key="number" :value="number">
                  {{ seatDisplayOf(number, view.seatNames) }}
                </option>
              </select>
            </label>
          </div>

          <div class="dimensions">
            <label>
              <input v-model="useLife" type="checkbox" />
              生死
              <select v-model="life" :disabled="!useLife">
                <option value="">（未选择）</option>
                <option value="Alive">存活</option>
                <option value="Dead">死亡</option>
              </select>
            </label>
            <label>
              <input v-model="useCharacter" type="checkbox" />
              角色
              <select v-model="character" :disabled="!useCharacter">
                <option value="">（请选择角色）</option>
                <option v-for="profile in ROSTER" :key="profile.slug" :value="profile.slug">
                  {{ profile.name }}（{{ profile.slug }}）
                </option>
              </select>
            </label>
            <label>
              <input v-model="useAlignment" type="checkbox" />
              阵营
              <select v-model="alignment" :disabled="!useAlignment">
                <option value="">（未选择）</option>
                <option value="Good">善良</option>
                <option value="Evil">邪恶</option>
              </select>
            </label>
            <label>
              <input v-model="useDrunk" type="checkbox" />
              醉酒
              <select v-model="drunk" :disabled="!useDrunk">
                <option value="">（未选择）</option>
                <option value="Sober">清醒</option>
                <option value="Drunk">醉酒</option>
              </select>
            </label>
            <label>
              <input v-model="usePoison" type="checkbox" />
              中毒
              <select v-model="poison" :disabled="!usePoison">
                <option value="">（未选择）</option>
                <option value="Healthy">健康</option>
                <option value="Poisoned">中毒</option>
              </select>
            </label>
          </div>

          <input v-model="reason" placeholder="变化原因（必填，会随事件流记录）" />
          <div class="actions">
            <button type="button" class="primary" :disabled="busy" @click="submitReport()">上报</button>
            <span class="hint">
              只报本次真正观测到的维度：不给的维度不参与判定、也不进状态账（D-0015）。
            </span>
          </div>
        </div>
      </div>
    </template>
    <div v-else class="placeholder">点选圆环上的席位牌，查看该席归因与操作。</div>
  </section>
</template>

<style scoped>
.console {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.block {
  border-top: 1px dashed var(--line);
  padding-top: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.line {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}

.free {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.options {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.dead {
  border: 1px solid var(--evil);
  color: var(--evil);
  border-radius: 999px;
  padding: 0 6px;
  font-size: 11px;
}

.context {
  margin: 0;
}

.detail {
  border-top: 1px solid var(--line);
  padding-top: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.detail h3 {
  margin: 0;
  font-size: 14px;
}

.summary {
  margin: 0;
  font-size: 13px;
}

.effects {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.effect {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
  align-items: baseline;
  font-size: 12px;
}

.report {
  border-top: 1px dashed var(--line);
  padding-top: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.punish {
  border-top: 1px dashed var(--line);
  padding-top: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.toggle {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  width: 100%;
  text-align: left;
}

.punish-body {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.dimensions {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 12px;
}

.dimensions label,
.inline {
  display: flex;
  align-items: center;
  gap: 4px;
}

.actions {
  display: flex;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
}
</style>
