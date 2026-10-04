<script setup lang="ts">
/**
 * 复盘圆盘：复用实时魔典的**席位牌原语**（`features/grimoire/GrimoireSeatCard`）+ 击杀箭头叠层。
 *
 * 边界（票据矩阵行 3 / 行 9）：
 * - 席位牌只渲染 `display/replay.ts` 折好的 `SeatCardModel`——不读原始步骤、不做领域判断；
 * - 击杀箭头只认 `kill-arrow` 标记（服务端已按 R-0038 同源判据给出），前端不做归因推断。
 */
import type { ReplayMarkerDto, SeatDisplayNameDto } from '@/contracts/game'
import { ringPosition, type SeatCardModel } from '@/display/grimoire'
import { markerLabelOf, markerTextOf } from '@/display/replay'
import GrimoireSeatCard from '@/features/grimoire/GrimoireSeatCard.vue'
import { computed } from 'vue'

const props = defineProps<{
  cards: SeatCardModel[]
  markers: ReplayMarkerDto[]
  currentSeat: number | null
  /** 公开的「席位 → 玩家名」（D-0021）：标记文案与实时魔典共用同一份口径。 */
  seatNames: SeatDisplayNameDto[]
}>()

interface Arrow {
  from: { xPercent: number; yPercent: number }
  to: { xPercent: number; yPercent: number }
}

const positioned = computed(() =>
  props.cards.map((card, index) => ({ card, position: ringPosition(index, props.cards.length) })),
)

const arrows = computed<Arrow[]>(() => {
  const bySeat = new Map(positioned.value.map((item) => [item.card.seat, item.position]))
  const result: Arrow[] = []
  for (const marker of props.markers) {
    if (marker.kind !== 'kill-arrow' || marker.from === null || marker.to === null) {
      continue
    }

    const from = bySeat.get(marker.from)
    const to = bySeat.get(marker.to)
    if (from !== null && from !== undefined && to !== null && to !== undefined) {
      result.push({ from, to })
    }
  }

  return result
})

const legend = computed(() =>
  props.markers.map((marker) => ({
    label: markerLabelOf(marker.kind),
    text: markerTextOf(marker, props.seatNames),
  })),
)
</script>

<template>
  <div class="replay-ring" data-testid="replay-ring">
    <svg class="arrows" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
      <defs>
        <marker id="replay-arrow-head" markerWidth="6" markerHeight="6" refX="5" refY="3" orient="auto">
          <path d="M0,0 L6,3 L0,6 Z" fill="#c0392b" />
        </marker>
      </defs>
      <line
        v-for="(arrow, index) in arrows"
        :key="index"
        :x1="arrow.from.xPercent"
        :y1="arrow.from.yPercent"
        :x2="arrow.to.xPercent"
        :y2="arrow.to.yPercent"
        stroke="#c0392b"
        stroke-width="0.8"
        marker-end="url(#replay-arrow-head)"
      />
    </svg>

    <GrimoireSeatCard
      v-for="item in positioned"
      :key="item.card.seat"
      :model="item.card"
      :position="item.position"
      :selected="false"
      :is-current-slot="item.card.seat === currentSeat"
      :has-pending="false"
      :has-decision="false"
    />
  </div>

  <ul v-if="legend.length > 0" class="legend" data-testid="replay-markers">
    <li v-for="(marker, index) in legend" :key="index">
      {{ marker.label }}<template v-if="marker.text.length > 0"> · {{ marker.text }}</template>
    </li>
  </ul>
</template>

<style scoped>
.replay-ring {
  position: relative;
  width: 100%;
  max-width: 460px;
  margin: 0 auto;
  aspect-ratio: 1;
  border-radius: 50%;
  background: radial-gradient(circle at 50% 50%, #f3efe4 0%, #ece5d6 55%, #e3dbc9 100%);
  border: 1px solid var(--line);
  outline: 1px dashed var(--line);
  outline-offset: -8px;
}

.arrows {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  pointer-events: none;
}

.legend {
  list-style: none;
  margin: 6px 0 0;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 4px 10px;
  font-size: 12px;
}

@media (max-width: 760px) {
  .replay-ring {
    aspect-ratio: auto;
    display: flex;
    flex-direction: column;
    gap: 8px;
    border-radius: 12px;
    outline: none;
    padding: 8px;
  }

  .replay-ring :deep(.seat) {
    position: static;
    transform: none;
    width: 100%;
  }

  .arrows {
    display: none;
  }
}
</style>
