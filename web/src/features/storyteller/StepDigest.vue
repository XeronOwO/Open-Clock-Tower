<script setup lang="ts">
/**
 * 每步摘要（票据第 3 条）：这一槽位的行动者、为什么需要这个选择、以及"现在轮到谁"。
 * 这里只呈现服务端已算好的上下文，前端不做任何推断。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { labelOf } from '@/display/labels'
import { seatLabelOf } from '@/display/format'

defineProps<{ view: StorytellerViewDto }>()
</script>

<template>
  <section class="panel">
    <h2>当前步骤</h2>
    <div v-if="view.planCompleted" class="placeholder">
      本计划已走完。{{ view.control === 'StorytellerTakeover' ? '当前处于说书人接管。' : '' }}
    </div>
    <div v-else-if="view.currentSlotActor === null" class="placeholder">
      当前槽位不是角色行动（{{ labelOf(view.currentSlotId) }}）——消耗配额，不产生请求。
    </div>
    <div v-else class="actor">
      <div class="actor-line">
        <span class="tag">行动者</span>
        <strong>{{ seatLabelOf(view.currentSlotActor) }}</strong>
      </div>
      <p class="context">{{ view.currentSlotContext ?? '（服务端未提供上下文）' }}</p>
      <p class="hint">
        引擎只给出"轮到谁、要选什么"；能力是否生效与信息真假由结算引擎与说书人各自负责。
      </p>
    </div>
  </section>
</template>

<style scoped>
.actor-line {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 4px;
}

.context {
  margin: 0 0 4px;
  font-size: 15px;
}
</style>
