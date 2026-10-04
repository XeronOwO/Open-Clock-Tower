<script setup lang="ts">
/**
 * 白天控制台（说书人）：开白天 / 计票 / 结束并处决。
 *
 * 白天没有自动计时（R-0017）：提名后由说书人决定何时计票、何时结束；按钮的使能条件
 * 与服务端四道闸同口径，真正的拒绝在服务端（这里只做"别让你点空"的呈现）。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { seatTextOf } from '@/display/format'
import HelpTip from '@/features/common/HelpTip.vue'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  closeDay,
  countVotes,
  startDay,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { computed, ref } from 'vue'

const props = defineProps<{ view: StorytellerViewDto; sender: CommandSender }>()
const emit = defineEmits<{ outcome: [CommandOutcome] }>()

const busy = ref(false)
const day = computed(() => props.view.day)
const openNominationIndex = computed(() => day.value?.openNominationIndex ?? null)

/** 开白天：上一个阶段是夜晚且已经走完（白天只能跟在夜晚之后）。 */
const canStartDay = computed(
  () =>
    props.view.planCompleted
    && (props.view.phase === 'FirstNight' || props.view.phase === 'OtherNight'),
)

/** 计票：白天开着且有一项提名在投票。 */
const canCount = computed(() => day.value?.status === 'Open' && openNominationIndex.value !== null)

/** 结束并处决：白天开着且没有未计票的提名。 */
const canClose = computed(() => day.value?.status === 'Open' && openNominationIndex.value === null)

async function run(action: () => Promise<CommandOutcome>): Promise<void> {
  busy.value = true
  try {
    emit('outcome', await action())
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section
    class="panel"
    data-testid="st-day"
    :data-day-number="day?.dayNumber ?? 0"
    :data-day-status="day?.status ?? 'None'"
  >
    <h2>白天<HelpTip topic="execution" /></h2>
    <p class="block-question">提名、计票、结束并处决；今天公开的事实都在这一段。</p>
    <p class="hint">提名后由你决定何时计票、何时结束并处决；平台没有超时（R-0017）。</p>
    <div class="row">
      <button
        type="button"
        class="primary"
        data-testid="st-start-day"
        :disabled="busy || !canStartDay"
        @click="run(() => startDay(sender, newIdempotencyKey('day')))"
      >
        开白天
      </button>
      <button
        type="button"
        data-testid="st-count-votes"
        :disabled="busy || !canCount"
        @click="run(() => countVotes(sender, openNominationIndex!, newIdempotencyKey('count')))"
      >
        计票
      </button>
      <button
        type="button"
        data-testid="st-close-day"
        :disabled="busy || !canClose"
        @click="run(() => closeDay(sender, newIdempotencyKey('close')))"
      >
        结束白天并处决
      </button>
    </div>

    <div v-if="day" class="facts" data-testid="st-day-facts">
      <p>
        第 <strong data-testid="st-day-number">{{ day.dayNumber }}</strong> 天 ·
        {{ day.status === 'Open' ? '进行中' : '已结束' }}
      </p>
      <p v-if="day.aboutToBeExecuted !== null" data-testid="st-about-to-be-executed" :data-seat="day.aboutToBeExecuted">
        即将被处决：{{ seatTextOf(day.aboutToBeExecuted, view.seatNames) }}
      </p>
      <p v-if="day.executed !== null" data-testid="st-executed" :data-seat="day.executed">
        已处决：{{ seatTextOf(day.executed, view.seatNames) }}
      </p>
      <h3 v-if="day.nominations.length > 0" class="sub-title">提名记录<HelpTip topic="votes" /></h3>
      <ul
        class="nominations"
        data-testid="st-day-nominations"
        :data-nomination-count="day.nominations.length"
        :data-open-nomination="openNominationIndex ?? ''"
      >
        <li
          v-for="nomination in day.nominations"
          :key="nomination.index"
          :data-nomination-index="nomination.index"
          :data-nomination-status="nomination.status"
          :data-nomination-votes="nomination.votes"
        >
          {{ nomination.index }}. {{ seatTextOf(nomination.nominator, view.seatNames) }} 提名
          {{ seatTextOf(nomination.nominee, view.seatNames) }} —— {{ nomination.votes }} 票（{{ nomination.status === 'Counted' ? '已计票' : '投票中' }}）
        </li>
      </ul>
    </div>
    <p v-else class="hint" data-testid="st-day-none">还没有开过白天。</p>
  </section>
</template>

<style scoped>
.row {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
  margin-bottom: 6px;
}

.facts p {
  margin: 2px 0;
}

.sub-title {
  margin: 6px 0 2px;
  font-size: 12px;
  color: var(--ink-soft);
}

.nominations {
  margin: 4px 0 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
</style>
