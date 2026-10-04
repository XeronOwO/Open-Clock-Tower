<script setup lang="ts">
/**
 * 复盘面板（D-0020）：说书人实时面与玩家终局后共用同一套呈现。
 *
 * 数据来自 `GetReplay`（按事件序号分页）；面板只持有**呈现态**：当前位置、播放 / 暂停、已加载窗口。
 * 盘面 = 服务端下发的步骤增量按序号合并（`display/replay.ts` 纯函数）——前端不推断任何规则。
 * 位置写进 URL hash（`?replay=<事件序号>`）：刷新 / 重连后按序号重建（票据矩阵行 7）。
 * 播放节拍是呈现态，与 D-0013 的对局配额无关（票据「决定与依据」）。
 */
import type { ReplayStepDto, ReplayViewDto, SeatDisplayNameDto } from '@/contracts/game'
import { labelOf } from '@/display/labels'
import {
  boardAt,
  markerLabelOf,
  markerTextOf,
  seatCardOf,
  seatNumbersOf,
  stepKindLabelOf,
} from '@/display/replay'
import ReplayCircle from '@/features/replay/ReplayCircle.vue'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

const props = defineProps<{
  /** 拉取一页复盘（服务端可见性闸：玩家面在结束批次之前会被拒绝）。 */
  fetchReplay: (afterSequence: number, pageSize: number) => Promise<ReplayViewDto>
  /** 玩家端没有席位名单：本人席位兜底盘面规模；说书人端可以不传。 */
  fallbackSeat?: number | null
}>()

const emit = defineEmits<{ close: [] }>()

const PAGE_SIZE = 200
const PLAY_INTERVAL_MS = 1200

const steps = ref<ReplayStepDto[]>([])
const seatNames = ref<SeatDisplayNameDto[]>([])
const cursor = ref(0)
const playing = ref(false)
const loading = ref(false)
const ended = ref(false)
const hasMore = ref(false)
const error = ref('')

let timer: number | null = null

const current = computed(() => steps.value[cursor.value] ?? null)
const board = computed(() => boardAt(steps.value, cursor.value))
const cards = computed(() =>
  seatNumbersOf(steps.value, props.fallbackSeat ?? null).map((seat) => seatCardOf(board.value, seat)),
)
const currentSeat = computed(() => {
  const step = current.value
  if (step === null) {
    return null
  }

  if (step.seats.length > 0) {
    return step.seats[0]?.seat ?? null
  }

  return step.markers.find((marker) => marker.seat !== null)?.seat ?? null
})
const progressText = computed(() =>
  steps.value.length === 0
    ? (loading.value ? '加载中…' : '还没有步骤')
    : `第 ${cursor.value + 1} / ${steps.value.length} 步 · 事件序号 ${current.value?.sequence ?? '—'}`,
)

async function loadMore(): Promise<boolean> {
  if (loading.value || (steps.value.length > 0 && !hasMore.value)) {
    return false
  }

  loading.value = true
  try {
    const last = steps.value[steps.value.length - 1]?.sequence ?? 0
    const page = await props.fetchReplay(last, PAGE_SIZE)
    for (const step of page.steps) {
      const previous = steps.value[steps.value.length - 1]?.sequence ?? 0
      if (step.sequence <= previous) {
        throw new Error(`复盘步骤序号不递增：${step.sequence} ≤ ${previous}`)
      }

      steps.value.push(step)
    }

    ended.value = page.ended
    hasMore.value = page.hasMore
    seatNames.value = page.seatNames
    error.value = ''
    return page.steps.length > 0
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : String(cause)
    stopPlaying()
    return false
  } finally {
    loading.value = false
  }
}

async function next(): Promise<void> {
  if (cursor.value + 1 < steps.value.length) {
    moveTo(cursor.value + 1)
    return
  }

  if (!hasMore.value) {
    stopPlaying()
    return
  }

  const loaded = await loadMore()
  if (loaded && cursor.value + 1 < steps.value.length) {
    moveTo(cursor.value + 1)
    return
  }

  stopPlaying()
}

function prev(): void {
  moveTo(Math.max(0, cursor.value - 1))
}

function backToStart(): void {
  stopPlaying()
  moveTo(0)
}

function togglePlay(): void {
  if (playing.value) {
    stopPlaying()
    return
  }

  if (steps.value.length === 0 || (!hasMore.value && cursor.value >= steps.value.length - 1)) {
    return
  }

  playing.value = true
  timer = window.setInterval(() => {
    void next()
  }, PLAY_INTERVAL_MS)
}

function stopPlaying(): void {
  playing.value = false
  if (timer !== null) {
    window.clearInterval(timer)
    timer = null
  }
}

async function refresh(): Promise<void> {
  stopPlaying()
  steps.value = []
  cursor.value = 0
  hasMore.value = false
  await loadMore()
}

function moveTo(index: number): void {
  cursor.value = Math.max(0, Math.min(index, Math.max(0, steps.value.length - 1)))
  syncHash()
}

/** 位置以事件序号写进 hash：刷新 / 重连后按序号重建（矩阵行 7）。 */
function syncHash(): void {
  if (typeof window === 'undefined') {
    return
  }

  const base = window.location.hash.split('?')[0]
  window.location.hash = `${base}?replay=${current.value?.sequence ?? 0}`
}

function positionFromHash(): number | null {
  if (typeof window === 'undefined') {
    return null
  }

  const match = /[?&]replay=(\d+)/.exec(window.location.hash)
  const captured = match?.[1]
  return captured === undefined ? null : Number.parseInt(captured, 10)
}

async function seekTo(sequence: number): Promise<void> {
  while (
    hasMore.value
    && (steps.value[steps.value.length - 1]?.sequence ?? 0) < sequence
  ) {
    const loaded = await loadMore()
    if (!loaded) {
      break
    }
  }

  let index = 0
  for (let candidate = 0; candidate < steps.value.length; candidate++) {
    const candidateStep = steps.value[candidate]
    if (candidateStep !== undefined && candidateStep.sequence <= sequence) {
      index = candidate
    }
  }

  cursor.value = index
}

onMounted(async () => {
  await loadMore()
  const wanted = positionFromHash()
  if (wanted !== null) {
    await seekTo(wanted)
  }
})

onBeforeUnmount(stopPlaying)
</script>

<template>
  <section class="replay panel" data-testid="replay-panel">
    <header class="head">
      <div>
        <h2>复盘</h2>
        <p class="hint" data-testid="replay-progress">{{ progressText }}</p>
      </div>
      <div class="row">
        <button type="button" data-testid="replay-refresh" @click="refresh()">刷新</button>
        <button type="button" data-testid="replay-close" @click="emit('close')">关闭</button>
      </div>
    </header>

    <p v-if="error" class="error" data-testid="replay-error">{{ error }}</p>

    <div class="body">
      <div class="board">
        <ReplayCircle
          :cards="cards"
          :markers="current?.markers ?? []"
          :current-seat="currentSeat"
          :seat-names="seatNames"
        />
      </div>

      <div class="side">
        <p class="meta">
          <span class="tag" data-testid="replay-step-kind">{{ current ? stepKindLabelOf(current.kind) : '—' }}</span>
          <span v-if="current?.phase" class="hint">{{ labelOf(current.phase) }}</span>
          <span v-if="loading" class="hint">加载中…</span>
        </p>
        <p class="summary" data-testid="replay-summary">{{ current?.summary ?? '（还没有步骤）' }}</p>
        <p v-if="current?.detail" class="detail" data-testid="replay-detail">{{ current.detail }}</p>

        <ul v-if="current && current.markers.length > 0" class="markers" data-testid="replay-marker-list">
          <li v-for="(marker, index) in current.markers" :key="index">
            {{ markerLabelOf(marker.kind) }}
            <template v-if="markerTextOf(marker, seatNames).length > 0"> · {{ markerTextOf(marker, seatNames) }}</template>
          </li>
        </ul>

        <div class="row controls">
          <button type="button" data-testid="replay-start" @click="backToStart()">回到开头</button>
          <button type="button" data-testid="replay-prev" @click="prev()">上一步</button>
          <button type="button" data-testid="replay-play" @click="togglePlay()">{{ playing ? '暂停' : '播放' }}</button>
          <button type="button" data-testid="replay-next" @click="next()">下一步</button>
        </div>

        <p class="hint" data-testid="replay-scope">
          {{ ended ? '终局揭示：结束批次之后的完整事实' : '说书人实时面：进行中的事实' }}
        </p>
      </div>
    </div>
  </section>
</template>

<style scoped>
.replay {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
  flex-wrap: wrap;
}

.head h2 {
  margin: 0;
}

.body {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(260px, 340px);
  gap: 10px;
  align-items: start;
}

@media (max-width: 980px) {
  .body {
    grid-template-columns: 1fr;
  }
}

.side {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.meta {
  display: flex;
  gap: 8px;
  align-items: center;
  margin: 0;
}

.summary {
  margin: 0;
  font-weight: 600;
}

.detail {
  margin: 0;
  color: var(--ink-soft, #555);
}

.markers {
  margin: 0;
  padding-left: 18px;
}

.controls {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.error {
  margin: 0;
  color: var(--warn, #b23);
}
</style>
