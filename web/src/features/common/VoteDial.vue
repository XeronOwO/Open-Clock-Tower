<script setup lang="ts">
/**
 * 钟盘：蓝针（时针）= 提名者、红针（分针）= 被提名者 / 收票中的当前席位（R-0017 目标形态）。
 *
 * 只做呈现：席位顺序、当前席位、已收席位与剩余时间全部来自服务端投影；
 * 这里不判断"能不能举手"、不推进任何状态（web/AGENTS §4）。
 */
import { computed } from 'vue'
import { dialAngle, dialPoint } from '@/features/common/voteDial'

const props = defineProps<{
  /** 全部席位号（按升序）。 */
  seatNumbers: number[]
  /** 蓝针（时针）指向：提名者。 */
  nominator: number | null
  /** 红针（分针）静态指向：被提名者。 */
  nominee: number | null
  /** 红针收票中的当前席位；为空时指被提名者。 */
  currentSeat: number | null
  /** 已收票的席位（按收票顺序）。 */
  collected: number[]
  /** 当前举手的席位。 */
  handsRaised: number[]
  /** 收票相位：Countdown / Collecting / Interrupted / AwaitingCount；没有收票时为 null。 */
  phase: string | null
  /** 距离下一拍的毫秒数。 */
  nextBeatMilliseconds: number | null
  /** 钟盘身份：提名（默认）/ 流放；只影响呈现属性（testid / aria），不影响几何。 */
  dialKind?: 'nomination' | 'exile'
}>()

const seatRadius = 78
const hourLength = 42
const minuteLength = 64

/** 提名钟盘沿用 `vote-dial`（既有装置的选择器），流放用 `exile-dial`——同页两个钟盘不再歧义。 */
const dialTestId = computed(() => (props.dialKind === 'exile' ? 'exile-dial' : 'vote-dial'))
const dialLabel = computed(() =>
  props.dialKind === 'exile'
    ? '流放钟盘（蓝针提议人 / 红针目标）'
    : '投票钟盘（蓝针提名者 / 红针被提名者）',
)

const seatPoints = computed(() =>
  props.seatNumbers.flatMap((seat) => {
    const point = dialPoint(props.seatNumbers, seat, seatRadius)
    return point === null ? [] : [{ seat, ...point }]
  }),
)
const hourAngle = computed(() => dialAngle(props.seatNumbers, props.nominator ?? -1) ?? 0)
const minuteTarget = computed(() => props.currentSeat ?? props.nominee)
const minuteAngle = computed(() => dialAngle(props.seatNumbers, minuteTarget.value ?? -1) ?? 0)

/** 倒计时读数（秒）；只在倒计时相位显示。 */
const countdown = computed(() =>
  props.phase === 'Countdown' && props.nextBeatMilliseconds !== null
    ? Math.max(0, Math.ceil(props.nextBeatMilliseconds / 1000))
    : null,
)

const isCollected = (seat: number): boolean => props.collected.includes(seat)
const isRaised = (seat: number): boolean => props.handsRaised.includes(seat)
</script>

<template>
  <div
    class="dial"
    :data-testid="dialTestId"
    :data-dial-kind="dialKind ?? 'nomination'"
    :data-phase="phase ?? ''"
    :data-current-seat="currentSeat ?? ''"
    :data-nominator="nominator ?? ''"
    :data-nominee="nominee ?? ''"
    :data-collected-seats="collected.join(',')"
    :data-hands-raised="handsRaised.join(',')"
    :data-remaining-ms="nextBeatMilliseconds ?? ''"
  >
    <svg viewBox="0 0 200 200" role="img" :aria-label="dialLabel">
      <circle class="face" cx="100" cy="100" r="92" />
      <g v-for="point in seatPoints" :key="point.seat">
        <circle
          class="seat"
          :class="{ 'is-collected': isCollected(point.seat), 'is-raised': isRaised(point.seat) }"
          :cx="point.x"
          :cy="point.y"
          r="10"
          :data-seat="point.seat"
          :data-collected="isCollected(point.seat) ? 'true' : 'false'"
          :data-hand="isRaised(point.seat) ? 'up' : 'down'"
        />
        <text class="seat-label" :x="point.x" :y="point.y + 3.5">{{ point.seat }}</text>
      </g>

      <!-- 蓝针：提名者；红针：被提名者 / 当前收票席位（CSS transform 让逐席旋转可见）。 -->
      <line
        class="hour-hand"
        x1="100"
        y1="100"
        x2="100"
        :y2="100 - hourLength"
        :style="{ transform: `rotate(${hourAngle}deg)`, transformOrigin: '100px 100px' }"
      />
      <line
        class="minute-hand"
        x1="100"
        y1="100"
        x2="100"
        :y2="100 - minuteLength"
        :style="{ transform: `rotate(${minuteAngle}deg)`, transformOrigin: '100px 100px' }"
      />
      <circle class="cap" cx="100" cy="100" r="5" />
      <text v-if="countdown !== null" class="countdown" x="100" y="120" data-testid="vote-dial-countdown">
        {{ countdown }}
      </text>
    </svg>
    <p class="legend">
      <span class="hour">蓝针 {{ nominator === null ? '—' : `${nominator} 号` }}</span>
      <span class="minute">红针 {{ minuteTarget === null ? '—' : `${minuteTarget} 号` }}</span>
    </p>
  </div>
</template>

<style scoped>
.dial {
  max-width: 260px;
}

.face {
  fill: #fff;
  stroke: var(--line);
  stroke-width: 2;
}

.seat {
  fill: #fff;
  stroke: var(--ink-soft);
  stroke-width: 1.5;
}

.seat.is-collected {
  fill: var(--ink-soft);
}

.seat.is-raised {
  stroke: var(--ok, #2e7d32);
  stroke-width: 3;
}

.seat-label {
  font-size: 10px;
  text-anchor: middle;
  fill: var(--ink);
  pointer-events: none;
}

.seat.is-collected + .seat-label {
  fill: #fff;
}

.hour-hand {
  stroke: #2d6cdf;
  stroke-width: 6;
  stroke-linecap: round;
  transition: transform 0.6s ease;
}

.minute-hand {
  stroke: #d64545;
  stroke-width: 4;
  stroke-linecap: round;
  transition: transform 0.3s linear;
}

.cap {
  fill: var(--ink);
}

.countdown {
  font-size: 22px;
  font-weight: 700;
  text-anchor: middle;
  fill: #d64545;
}

.legend {
  display: flex;
  gap: 12px;
  justify-content: center;
  margin: 4px 0 0;
  font-size: 12px;
  color: var(--ink-soft);
}

.legend .hour {
  color: #2d6cdf;
}

.legend .minute {
  color: #d64545;
}
</style>
