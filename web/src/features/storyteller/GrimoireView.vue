<script setup lang="ts">
/**
 * 魔典主视图：以席位为中心的小镇圆环（窄屏退化为纵向席位列表）+ 选中席位的操作台。
 *
 * 边界（`docs/architecture/storyteller-presentation.md` §4）：
 * - 数据只来自同一份 `StorytellerViewDto`（主视图与下钻面板同源，矩阵行 6）；
 * - 席位顺序 = 服务端席位号 1..N，**不做拖拽重排**（不制造"本地顺序"这一第二事实来源）；
 * - 选中态是呈现态（本地 ref），不发命令、不改任何游戏数据。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { labelOf } from '@/display/labels'
import {
  attentionSeatOf,
  buildSeatCard,
  decisionSeatOf,
  ringPosition,
  seatNumbersOf,
} from '@/display/grimoire'
import type { CommandOutcome, CommandSender } from '@/services/storytellerCommands'
import { computed, ref } from 'vue'
import GrimoireSeatCard from '@/features/storyteller/GrimoireSeatCard.vue'
import GrimoireSeatConsole from '@/features/storyteller/GrimoireSeatConsole.vue'

const props = defineProps<{
  view: StorytellerViewDto
  sender: CommandSender
  seatCount: number
}>()

const emit = defineEmits<{ outcome: [CommandOutcome] }>()

const seats = computed(() => seatNumbersOf(props.view, props.seatCount))
const cards = computed(() => seats.value.map((seat) => buildSeatCard(props.view, seat)))
const decisionSeat = computed(() => decisionSeatOf(props.view))
const attentionSeat = computed(() => attentionSeatOf(props.view))

/** "定位到 N 号"按钮的说辞：说明这一席为什么需要处理。 */
const attentionReason = computed(() => {
  if (props.view.pending !== null) {
    return '卡点'
  }

  if (props.view.awaitingDecisionId !== null) {
    return '待裁定'
  }

  return '当前槽位'
})

/** 选中态是呈现态：默认跟随"当前要处理的席位"，用户点选之后以点选为准。 */
const picked = ref<number | null>(null)
const selected = computed(() => {
  if (picked.value !== null && seats.value.includes(picked.value)) {
    return picked.value
  }

  return attentionSeat.value ?? seats.value[0] ?? null
})

function pick(seat: number): void {
  picked.value = seat
}
</script>

<template>
  <div class="grimoire" data-testid="grimoire">
    <div class="ring-area">
      <div class="ring-head">
        <p class="hint">
          席位顺序按服务端席位号（1 号在正上方、顺时针）。圆环只是呈现——角色、生死、状态都来自同一次视图推送。
        </p>
        <button
          v-if="attentionSeat !== null && attentionSeat !== selected"
          type="button"
          @click="pick(attentionSeat)"
        >
          定位到 {{ attentionSeat }} 号（{{ attentionReason }}）
        </button>
      </div>

      <div class="ring">
        <div class="hub">
          <strong>{{ labelOf(view.phase) }}</strong>
          <span class="hint">
            {{ view.slotCount === 0 ? '尚未建计划' : `第 ${view.slotIndex + 1} / ${view.slotCount} 步` }}
          </span>
          <span class="hint">{{ seats.length }} 席</span>
        </div>

        <GrimoireSeatCard
          v-for="(card, index) in cards"
          :key="card.seat"
          :model="card"
          :position="ringPosition(index, cards.length)"
          :selected="card.seat === selected"
          :is-current-slot="view.currentSlotActor === card.seat"
          :has-pending="view.pending?.seat === card.seat"
          :has-decision="decisionSeat === card.seat && view.awaitingDecisionId !== null"
          @select="pick(card.seat)"
        />
      </div>
    </div>

    <GrimoireSeatConsole
      :view="view"
      :sender="sender"
      :seat="selected"
      :seat-count="seatCount"
      @outcome="emit('outcome', $event)"
      @engage="pick"
      @locate="pick"
    />
  </div>
</template>

<style scoped>
.grimoire {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(300px, 380px);
  gap: 10px;
  align-items: start;
}

@media (max-width: 1280px) {
  .grimoire {
    grid-template-columns: 1fr;
  }
}

.ring-area {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.ring-head {
  display: flex;
  gap: 10px;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
}

.ring-head p {
  margin: 0;
}

.ring {
  position: relative;
  width: 100%;
  max-width: 760px;
  margin: 0 auto;
  aspect-ratio: 1;
  border-radius: 50%;
  background: radial-gradient(circle at 50% 50%, #f3efe4 0%, #ece5d6 55%, #e3dbc9 100%);
  border: 1px solid var(--line);
  outline: 1px dashed var(--line);
  outline-offset: -8px;
}

.hub {
  position: absolute;
  left: 50%;
  top: 50%;
  transform: translate(-50%, -50%);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  padding: 12px 16px;
  border-radius: 50%;
  background: var(--paper-raised);
  border: 1px dashed var(--line);
  min-width: 132px;
  text-align: center;
}

@media (max-width: 760px) {
  .ring {
    aspect-ratio: auto;
    display: flex;
    flex-direction: column;
    gap: 10px;
    border-radius: 12px;
    outline: none;
    padding: 8px;
  }

  .ring :deep(.seat) {
    position: static;
    transform: none;
    width: 100%;
    flex-direction: row;
    flex-wrap: wrap;
    align-items: center;
    justify-content: flex-start;
    gap: 8px;
    text-align: left;
  }

  .ring :deep(.token) {
    width: 64px;
    height: 64px;
    flex: 0 0 auto;
  }

  .ring :deep(.marks) {
    max-width: none;
    justify-content: flex-start;
  }

  .hub {
    position: static;
    transform: none;
    border-radius: 10px;
    flex-direction: row;
    flex-wrap: wrap;
    gap: 10px;
    justify-content: center;
  }
}
</style>
