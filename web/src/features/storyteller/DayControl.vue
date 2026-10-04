<script setup lang="ts">
/**
 * 白天控制台（说书人）：开白天 / 钟盘收票（开始 / 继续 / 参数）/ 计票 / 结束并处决。
 *
 * 收票时间轴由服务端节拍器推进（R-0017 目标形态）：前端只把参数发出去、渲染服务端下发的
 * 相位 / 当前席位 / 剩余时间；按钮使能条件与服务端四道闸同口径，真正的拒绝在服务端
 * （这里只做"别让你点空"的呈现）。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { seatTextOf } from '@/display/format'
import HelpTip from '@/features/common/HelpTip.vue'
import VoteDial from '@/features/common/VoteDial.vue'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  closeDay,
  countVotes,
  resumeVoteSweep,
  startDay,
  startVoteSweep,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { computed, ref } from 'vue'

const props = defineProps<{ view: StorytellerViewDto; sender: CommandSender }>()
const emit = defineEmits<{ outcome: [CommandOutcome] }>()

const busy = ref(false)
const day = computed(() => props.view.day)
const openNominationIndex = computed(() => day.value?.openNominationIndex ?? null)
const openNomination = computed(
  () =>
    day.value?.nominations.find((nomination) => nomination.index === openNominationIndex.value) ?? null,
)
const sweep = computed(() => openNomination.value?.sweep ?? null)
const seatNumbers = computed(() =>
  props.view.seats.map((seat) => seat.seat).sort((left, right) => left - right),
)

/** 倒计时 / 间隔（秒；默认 3s / 1s，与服务端默认一致；只在没开始收票时可改）。 */
const countdownSeconds = ref(3)
const intervalSeconds = ref(1)

/** 开白天：上一个阶段是夜晚且已经走完（白天只能跟在夜晚之后）。 */
const canStartDay = computed(
  () =>
    props.view.planCompleted
    && (props.view.phase === 'FirstNight' || props.view.phase === 'OtherNight'),
)

/** 开始收票：白天开着、有开放提名、还没有开始收票。 */
const canStartSweep = computed(
  () => day.value?.status === 'Open' && openNominationIndex.value !== null && sweep.value === null,
)

/** 继续收票：收票被中断（服务端重启 / 重建后不追补，等说书人显式继续）。 */
const canResumeSweep = computed(() => sweep.value?.phase === 'Interrupted')

/** 计票：收票全部走完（每一席都有冻结结论）后可用。 */
const canCount = computed(() => day.value?.status === 'Open' && sweep.value?.phase === 'AwaitingCount')

/** 结束并处决：白天开着且没有未计票的提名。 */
const canClose = computed(() => day.value?.status === 'Open' && openNominationIndex.value === null)

const phaseLabel = computed(() => {
  switch (sweep.value?.phase) {
    case 'Countdown':
      return '倒计时中：玩家举手 = 投这一票'
    case 'Collecting':
      return `分针收票中：当前指向 ${sweep.value.currentSeat ?? '—'} 号`
        + `（已收 ${sweep.value.collected.length} 席）`
    case 'Interrupted':
      return '收票已中断：点「继续收票」重新起倒计时，从下一未收席位接着收'
    case 'AwaitingCount':
      return '收票已走完：可以计票'
    default:
      return null
  }
})

async function run(action: () => Promise<CommandOutcome>): Promise<void> {
  busy.value = true
  try {
    emit('outcome', await action())
  } finally {
    busy.value = false
  }
}

function secondsToMilliseconds(seconds: number): number {
  return Math.round(seconds * 1000)
}

async function beginSweep(): Promise<void> {
  if (openNominationIndex.value === null) {
    return
  }

  await run(() =>
    startVoteSweep(
      props.sender,
      openNominationIndex.value!,
      secondsToMilliseconds(countdownSeconds.value),
      secondsToMilliseconds(intervalSeconds.value),
      newIdempotencyKey('sweep'),
    ),
  )
}
</script>

<template>
  <section
    class="panel"
    data-testid="st-day"
    :data-day-number="day?.dayNumber ?? 0"
    :data-day-status="day?.status ?? 'None'"
    :data-sweep-phase="sweep?.phase ?? ''"
    :data-sweep-current-seat="sweep?.currentSeat ?? ''"
  >
    <h2>白天<HelpTip topic="execution" /></h2>
    <p class="block-question">提名、钟盘收票、计票、结束并处决；今天公开的事实都在这一段。</p>
    <p class="hint">提名后由你决定何时开始收票、何时计票、何时结束并处决（R-0017）。</p>
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
      <template v-if="sweep === null">
        <label class="field">
          倒计时（秒）
          <input
            v-model.number="countdownSeconds"
            type="number"
            min="1"
            max="10"
            step="0.5"
            data-testid="st-sweep-countdown"
            :disabled="busy || !canStartSweep"
          />
        </label>
        <label class="field">
          间隔（秒）
          <input
            v-model.number="intervalSeconds"
            type="number"
            min="0.3"
            max="5"
            step="0.1"
            data-testid="st-sweep-interval"
            :disabled="busy || !canStartSweep"
          />
        </label>
        <button
          type="button"
          class="primary"
          data-testid="st-start-vote-sweep"
          :disabled="busy || !canStartSweep"
          @click="beginSweep()"
        >
          开始收票
        </button>
      </template>
      <button
        v-if="canResumeSweep"
        type="button"
        class="primary"
        data-testid="st-resume-vote-sweep"
        :disabled="busy"
        @click="run(() => resumeVoteSweep(sender, openNominationIndex!, newIdempotencyKey('sweep-resume')))"
      >
        继续收票
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

    <VoteDial
      v-if="openNomination"
      :seat-numbers="seatNumbers"
      :nominator="openNomination.nominator"
      :nominee="openNomination.nominee"
      :current-seat="sweep?.currentSeat ?? null"
      :collected="sweep?.collected ?? []"
      :hands-raised="openNomination.handsRaised"
      :phase="sweep?.phase ?? null"
      :next-beat-milliseconds="sweep?.nextBeatMilliseconds ?? null"
    />
    <p v-if="phaseLabel" class="hint" data-testid="st-sweep-phase">{{ phaseLabel }}</p>

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
          :data-nomination-hands="nomination.handsRaised.join(',')"
        >
          {{ nomination.index }}. {{ seatTextOf(nomination.nominator, view.seatNames) }} 提名
          {{ seatTextOf(nomination.nominee, view.seatNames) }} —— {{ nomination.votes }} 票（{{ nomination.status === 'Counted' ? '已计票' : '收票中' }}）
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

.field {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: var(--ink-soft);
}

.field input {
  width: 64px;
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
