<script setup lang="ts">
/**
 * 最近的状态变化（说书人上帝视角的时间线）：谁、因何原因、变成什么样、由谁导致。
 * 「变化」而不是「当前值」：这是归因链的入口，当前值在状态账里。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { clockTimeOf, causedByLabelOf, seatDisplayOf } from '@/display/format'
import { characterLabelOf, labelOf } from '@/display/labels'
import HelpTip from '@/features/common/HelpTip.vue'

defineProps<{ view: StorytellerViewDto }>()

/** 一条变化里出现的维度（只显示本次真正带上的维度——未观测不等于没变）。 */
function dimensionsOf(change: StorytellerViewDto['recentSeatChanges'][number]): string[] {
  const parts: string[] = []
  if (change.life !== null) parts.push(`生死 ${labelOf(change.life)}`)
  if (change.character !== null) parts.push(`角色 ${characterLabelOf(change.character)}`)
  if (change.alignment !== null) parts.push(`阵营 ${labelOf(change.alignment)}`)
  if (change.drunk !== null) parts.push(`醉酒 ${labelOf(change.drunk)}`)
  if (change.poison !== null) parts.push(`中毒 ${labelOf(change.poison)}`)
  return parts
}
</script>

<template>
  <section class="panel">
    <h2>最近状态变化（最新在后）<HelpTip topic="status-ledger" /></h2>
    <p class="block-question">谁、因为什么、变成了什么样；它是状态账的来路。</p>
    <div v-if="view.recentSeatChanges.length === 0" class="placeholder">还没有状态变化。</div>
    <table v-else>
      <thead>
        <tr>
          <th>席位</th>
          <th>变化</th>
          <th>原因</th>
          <th>归因</th>
          <th>效果</th>
          <th>时刻</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="change in view.recentSeatChanges" :key="`${change.sequence}-${change.seat}`">
          <td>{{ seatDisplayOf(change.seat, view.seatNames) }}</td>
          <td>{{ dimensionsOf(change).join('，') || '（无维度）' }}</td>
          <td>{{ change.reason }}</td>
          <td>{{ causedByLabelOf(change.causedBy) ?? '—' }}</td>
          <td class="mono">{{ change.effectId ?? '—' }}</td>
          <td class="mono">{{ clockTimeOf(change.recordedAt) }}</td>
        </tr>
      </tbody>
    </table>
  </section>
</template>
