<script setup lang="ts">
/**
 * 博学者的两条信息：两个槽位 + 候选事实辅助面（R-0057-C）。
 *
 * 只做呈现与拼装：候选与真值都来自服务端，组合结论 = **服务端声明的约束** × 服务端下发的真值
 * （见 `display/savantFacts.ts`）；提交仍由服务端按当时的账重新求值与核对（前端拦下非法组合
 * 只是省一次往返，不是权威判定）。「与另一槽位互为反面」的候选按服务端给的互斥组**预先灰掉**。
 * 兜底入口保留自由文本（平台不校验真假，R-0057 第 3 条）。
 */
import type { DecisionOptionDto } from '@/contracts/game'
import {
  combinationVerdict,
  exclusionConflictOf,
  filterSavantOptions,
  savantDecisionOf,
  savantGroupsOf,
  truthLabelOf,
  truthToneOf,
} from '@/display/savantFacts'
import { computed, ref } from 'vue'

const props = defineProps<{
  options: readonly DecisionOptionDto[]
  /** 服务端声明的真值组合约束；null = 不适用（此时不显示结论条）。 */
  rule: string | null
  /** 约束说明（原因与依据，服务端给）。 */
  note: string | null
  busy: boolean
}>()

const emit = defineEmits<{ submit: [string] }>()

const first = ref<DecisionOptionDto | null>(null)
const second = ref<DecisionOptionDto | null>(null)
const activeSlot = ref<1 | 2>(1)
const query = ref('')
const activeGroup = ref<string | null>(null)
const freeFirst = ref('')
const freeSecond = ref('')

const groups = computed(() => savantGroupsOf(props.options))
const visible = computed(() => filterSavantOptions(props.options, query.value, activeGroup.value))
const verdict = computed(() => combinationVerdict(props.rule, first.value, second.value))
const canSubmit = computed(() => verdict.value.ok && first.value !== null && second.value !== null)
const canSubmitFree = computed(
  () => freeFirst.value.trim().length > 0 && freeSecond.value.trim().length > 0,
)

const firstTruth = computed(() => truthLabelOf(first.value?.truth ?? null))
const firstTone = computed(() => `tone-${truthToneOf(first.value?.truth ?? null)}`)
const secondTruth = computed(() => truthLabelOf(second.value?.truth ?? null))
const secondTone = computed(() => `tone-${truthToneOf(second.value?.truth ?? null)}`)

/**
 * 这条候选与「另一个槽位已选的那条」互为反面时的理由（C4 防呆，R-0057-C）；
 * 不互为反面（或另一个槽位还空着）时返回 null。
 *
 * 「另一个槽位」= 这条候选没坐的那个槽位：已经坐在第一个槽位里 → 与第二个比，反之亦然。
 * 命中就该灰掉并写明原因：点它必然被服务端拒绝（服务端提交时仍会按当时的账再拒一次）。
 */
function oppositeReasonOf(candidate: DecisionOptionDto): string | null {
  const other = candidate.value === first.value?.value ? second.value : first.value
  return exclusionConflictOf(candidate, other)?.reason ?? null
}

/** 点候选 → 填进当前槽位；另一个槽位还空着就把焦点交过去（两次点击完成两条）。 */
function pick(option: DecisionOptionDto): void {
  if (activeSlot.value === 1) {
    first.value = option
    if (second.value === null) {
      activeSlot.value = 2
    }
    return
  }

  second.value = option
  if (first.value === null) {
    activeSlot.value = 1
  }
}

function clearSlot(slot: 1 | 2): void {
  if (slot === 1) {
    first.value = null
  } else {
    second.value = null
  }

  activeSlot.value = slot
}

/** 分类栏：再点一次同一类 = 回到全部。 */
function toggleGroup(group: string): void {
  activeGroup.value = activeGroup.value === group ? null : group
}

function isPicked(option: DecisionOptionDto): boolean {
  return option.value === first.value?.value || option.value === second.value?.value
}

function submitStructured(): void {
  if (!canSubmit.value || first.value === null || second.value === null) {
    return
  }

  emit('submit', savantDecisionOf(first.value, second.value))
}

function submitFree(): void {
  if (!canSubmitFree.value) {
    return
  }

  emit('submit', `${freeFirst.value.trim()}|${freeSecond.value.trim()}`)
}
</script>

<template>
  <div class="truth-picker" data-testid="savant-picker">
    <div class="banner">
      <p v-if="note" class="note" data-testid="savant-rule-note">{{ note }}</p>
      <p
        class="verdict"
        :class="verdict.ok ? 'ok' : 'bad'"
        data-testid="savant-verdict"
        :data-verdict-ok="verdict.ok ? 'true' : 'false'"
      >
        当前组合：{{ verdict.text }}
      </p>
    </div>

    <div class="slots">
      <div
        class="slot"
        :class="{ active: activeSlot === 1 }"
        data-testid="savant-slot-1"
        :data-slot-value="first === null ? '' : first.value"
      >
        <button type="button" class="slot-pick" @click="activeSlot = 1">第一条</button>
        <template v-if="first !== null">
          <span class="slot-text">{{ first.preview }}</span>
          <span class="badge" :class="firstTone">{{ firstTruth }}</span>
          <button type="button" class="slot-clear" @click="clearSlot(1)">清空</button>
        </template>
        <span v-else class="slot-empty">未选（点下面的候选）</span>
      </div>
      <div
        class="slot"
        :class="{ active: activeSlot === 2 }"
        data-testid="savant-slot-2"
        :data-slot-value="second === null ? '' : second.value"
      >
        <button type="button" class="slot-pick" @click="activeSlot = 2">第二条</button>
        <template v-if="second !== null">
          <span class="slot-text">{{ second.preview }}</span>
          <span class="badge" :class="secondTone">{{ secondTruth }}</span>
          <button type="button" class="slot-clear" @click="clearSlot(2)">清空</button>
        </template>
        <span v-else class="slot-empty">未选（点下面的候选）</span>
      </div>
    </div>

    <div class="finder">
      <input v-model="query" data-testid="savant-search" placeholder="搜索候选（文案或编码）" />
      <div class="groups">
        <button
          type="button"
          class="group"
          :class="{ active: activeGroup === null }"
          data-testid="savant-group-all"
          @click="activeGroup = null"
        >
          全部（{{ options.length }}）
        </button>
        <button
          v-for="entry in groups"
          :key="entry.group"
          type="button"
          class="group"
          :class="{ active: activeGroup === entry.group }"
          :data-testid="`savant-group-${entry.group}`"
          @click="toggleGroup(entry.group)"
        >
          {{ entry.group }}（{{ entry.options.length }}）
        </button>
      </div>
    </div>

    <div v-if="visible.length > 0" class="options" data-testid="savant-options">
      <button
        v-for="option in visible"
        :key="option.value"
        type="button"
        class="option"
        :class="{ picked: isPicked(option), blocked: oppositeReasonOf(option) !== null }"
        :disabled="busy || oppositeReasonOf(option) !== null"
        :data-testid="`savant-option-${option.value}`"
        :data-truth="option.truth === null ? '' : option.truth"
        :data-group="option.group === null ? '' : option.group"
        :data-opposite="oppositeReasonOf(option) === null ? '' : 'true'"
        :data-code="option.code === null ? '' : option.code"
        :data-exclusion="option.exclusionGroup === null ? '' : option.exclusionGroup"
        @click="pick(option)"
      >
        <span class="option-text">{{ option.preview }}</span>
        <span class="badge" :class="`tone-${truthToneOf(option.truth)}`">
          {{ truthLabelOf(option.truth) }}
        </span>
        <span v-for="tag in option.tags" :key="tag" class="tag">{{ tag }}</span>
        <span v-if="oppositeReasonOf(option) !== null" class="blocked-note">
          {{ oppositeReasonOf(option) }}
        </span>
      </button>
    </div>
    <p v-else class="hint">没有匹配的候选：换个关键字，或清掉分类筛选。</p>

    <div class="submit-row">
      <button
        type="button"
        class="primary"
        data-testid="savant-submit"
        :disabled="busy || !canSubmit"
        @click="submitStructured"
      >
        按候选结清
      </button>
    </div>

    <details class="fallback">
      <summary>自己写两条（平台不校验真假）</summary>
      <div class="free">
        <input v-model="freeFirst" data-testid="savant-free-first" placeholder="第一条" />
        <input v-model="freeSecond" data-testid="savant-free-second" placeholder="第二条" />
        <button
          type="button"
          data-testid="savant-submit-free"
          :disabled="busy || !canSubmitFree"
          @click="submitFree"
        >
          按自由文本结清
        </button>
      </div>
      <p class="hint">自由文本不做真值校验，事件里会标「未校验」。</p>
    </details>
  </div>
</template>

<style scoped>
.truth-picker {
  display: grid;
  gap: 0.5rem;
  margin-top: 0.5rem;
}

.banner {
  display: grid;
  gap: 0.2rem;
}

.note {
  margin: 0;
  font-size: 0.82rem;
  opacity: 0.85;
}

.verdict {
  margin: 0;
  font-size: 0.85rem;
  font-weight: 600;
}

.verdict.ok {
  color: #2f7a4f;
}

.verdict.bad {
  color: #b3352f;
}

.slots {
  display: grid;
  gap: 0.3rem;
}

.slot {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.3rem 0.45rem;
  border: 1px solid rgba(127, 127, 127, 0.4);
  border-radius: 6px;
}

.slot.active {
  border-color: #6a8cff;
  box-shadow: inset 0 0 0 1px rgba(106, 140, 255, 0.5);
}

.slot-pick {
  font-weight: 600;
}

.slot-text {
  flex: 1;
}

.slot-empty {
  flex: 1;
  opacity: 0.6;
  font-size: 0.85rem;
}

.finder {
  display: grid;
  gap: 0.3rem;
}

.groups {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
}

.group.active {
  border-color: #6a8cff;
}

.options {
  display: grid;
  gap: 0.2rem;
  max-height: 15rem;
  overflow-y: auto;
}

.option {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  text-align: left;
}

.option-text {
  flex: 1;
}

.option.picked {
  border-color: #6a8cff;
  font-weight: 600;
}

/* 与另一槽位互为反面：灰掉并写明原因（C4 防呆，R-0057-C）——点它必然被服务端拒绝。 */
.option.blocked {
  opacity: 0.45;
}

.option.blocked .option-text {
  text-decoration: line-through;
}

.blocked-note {
  font-size: 0.75rem;
  opacity: 0.9;
}

.badge {
  font-size: 0.75rem;
  padding: 0 0.3rem;
  border-radius: 4px;
  border: 1px solid currentColor;
}

.badge.tone-true {
  color: #2f7a4f;
}

.badge.tone-false {
  color: #b3352f;
}

.badge.tone-unknown {
  opacity: 0.6;
}

.tag {
  font-size: 0.75rem;
  padding: 0 0.3rem;
  border-radius: 4px;
  border: 1px solid #b3352f;
  color: #b3352f;
}

.submit-row {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.fallback .free {
  display: grid;
  gap: 0.3rem;
  margin-top: 0.3rem;
}

.hint {
  font-size: 0.8rem;
  opacity: 0.75;
  margin: 0;
}
</style>
