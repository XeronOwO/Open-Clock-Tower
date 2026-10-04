<script setup lang="ts">
/**
 * 状态账（五个可观测维度 + 疯狂要求另列）：
 * 每一格都带"怎么来的"与"哪条效果造成的"；**未观测的维度不出现**（不是默认值）。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { causedByLabelOf, seatDisplayOf } from '@/display/format'
import { characterLabelOf, dimensionLabelOf, labelOf } from '@/display/labels'
import HelpTip from '@/features/common/HelpTip.vue'

defineProps<{ view: StorytellerViewDto }>()

/** 维度值 → 呈现文案：角色维度显示中文名，其它维度翻枚举。 */
function valueTextOf(dimension: string, value: string): string {
  return dimension === 'Character' ? characterLabelOf(value) : labelOf(value)
}
</script>

<template>
  <section class="panel">
    <h2>状态账（已观测的维度；未观测不显示）<HelpTip topic="status-ledger" /></h2>
    <p class="block-question">谁现在带着什么状态，每一条是从哪来的。</p>
    <div v-if="view.seats.length === 0" class="placeholder">
      还没有任何座位被观测过——开局分配与说书人上报都会在这里留下事实。
    </div>
    <table v-else>
      <thead>
        <tr>
          <th>席位</th>
          <th>维度</th>
          <th>当前值</th>
          <th>来由</th>
          <th>归因</th>
          <th>效果</th>
        </tr>
      </thead>
      <tbody>
        <template v-for="seat in view.seats" :key="seat.seat">
          <tr v-for="fact in seat.facts" :key="`${seat.seat}-${fact.dimension}`">
            <td>{{ seatDisplayOf(seat.seat, view.seatNames) }}</td>
            <td>{{ dimensionLabelOf(fact.dimension) }}</td>
            <td>{{ valueTextOf(fact.dimension, fact.value) }}</td>
            <td>{{ fact.reason }}</td>
            <td>{{ causedByLabelOf(fact.causedBy) ?? '—' }}</td>
            <td class="mono">{{ fact.effectId ?? '—' }}</td>
          </tr>
          <tr v-if="seat.facts.length === 0" :key="`${seat.seat}-empty`">
            <td>{{ seatDisplayOf(seat.seat, view.seatNames) }}</td>
            <td colspan="5" class="hint">该席位已建账但还没有可显示的维度事实。</td>
          </tr>
          <tr v-if="seat.madnesses.length > 0" :key="`${seat.seat}-madness`">
            <td>{{ seatDisplayOf(seat.seat, view.seatNames) }}</td>
            <td>疯狂要求</td>
            <td colspan="4">
              <span v-for="madness in seat.madnesses" :key="madness" class="tag warn">{{ madness }}</span>
              <span class="hint">（只由裁定写入，引擎不判定疯狂——R-0003）</span>
            </td>
          </tr>
        </template>
      </tbody>
    </table>
  </section>
</template>

<style scoped>
.tag + .tag {
  margin-left: 6px;
}
</style>
