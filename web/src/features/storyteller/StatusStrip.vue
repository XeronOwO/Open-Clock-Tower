<script setup lang="ts">
/** 顶部状态条：说书人一眼看到「这局现在在哪、卡在哪」。 */
import type { StorytellerViewDto } from '@/contracts/game'
import { seatDisplayOf, waitingSecondsTextOf } from '@/display/format'
import { slotCounterTextOf } from '@/display/grimoire'
import { labelOf } from '@/display/labels'
import HelpTip from '@/features/common/HelpTip.vue'

defineProps<{ view: StorytellerViewDto }>()
</script>

<template>
  <header class="strip panel">
    <div class="cell">
      <span class="caption">阶段</span>
      <strong>{{ labelOf(view.phase) }}</strong>
    </div>
    <div class="cell">
      <!-- 说明按钮与 `.caption` 同级：caption 的文本必须保持纯标签（验收装置按 textContent 取锚点）。 -->
      <span class="caption-row"><span class="caption">控制</span><HelpTip topic="control" /></span>
      <strong>{{ labelOf(view.control) }}</strong>
    </div>
    <div class="cell">
      <span class="caption-row"><span class="caption">槽位</span><HelpTip topic="slot" /></span>
      <strong>{{ slotCounterTextOf(view) }}</strong>
      <span class="mono">{{ view.currentSlotId ?? '' }}</span>
    </div>
    <div class="cell">
      <span class="caption-row"><span class="caption">事件序号</span><HelpTip topic="event-sequence" /></span>
      <strong class="mono">{{ view.sequence }}</strong>
    </div>
    <div class="cell">
      <span class="caption-row"><span class="caption">计划</span><HelpTip topic="plan" /></span>
      <strong>{{ view.planCompleted ? '已走完' : '进行中' }}</strong>
    </div>
    <div v-if="view.pending" class="cell alert">
      <span class="caption-row"><span class="caption">卡点</span><HelpTip topic="pending" /></span>
      <strong>{{ seatDisplayOf(view.pending.seat, view.seatNames) }} 尚未作答</strong>
      <span v-if="view.pending.triggerReason" class="mono">{{ view.pending.triggerReason }}</span>
      <span v-if="waitingSecondsTextOf(view.pending.waitingSeconds)" class="mono">
        已等待 {{ waitingSecondsTextOf(view.pending.waitingSeconds) }}
      </span>
    </div>
    <div v-if="view.awaitingDecisionId" class="cell alert">
      <span class="caption-row"><span class="caption">待裁定</span><HelpTip topic="decision" /></span>
      <strong>{{ view.awaitingDecisionId }}</strong>
    </div>
    <div v-if="view.blockedReason" class="cell blocked">
      <span class="caption-row"><span class="caption">阻塞</span><HelpTip topic="blocked" /></span>
      <strong>{{ view.blockedReason }}</strong>
    </div>
  </header>
</template>

<style scoped>
.strip {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 18px;
  align-items: baseline;
}

.cell {
  display: flex;
  flex-direction: column;
  min-width: 72px;
}

.caption {
  font-size: 11px;
  color: var(--ink-soft);
}

.caption-row {
  display: flex;
  align-items: center;
}

.alert strong {
  color: var(--warn);
}

.blocked strong {
  color: var(--evil);
}
</style>
