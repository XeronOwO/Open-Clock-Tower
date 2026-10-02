<script setup lang="ts">
/**
 * 两本账（说书人视角）与最近一次结算结论：
 * - 能力使用账本：这个能力用过没有、生效过没有；
 * - 失效账本：未正常生效及其原因分类（R-0004；R-0004 未闭合的路径记「未定」）；
 * - 最近一次结算：生效 / 未生效 + 原因（这是"能力是否正常生效"的权威结论，不由前端推断）。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { seatLabelOf } from '@/display/format'
import { labelOf } from '@/display/labels'

defineProps<{ view: StorytellerViewDto }>()

/** 生效结论 → 文案（`Effective` 契约上必有；这里只做一处映射，不推断）。 */
function effectiveText(effective: boolean): string {
  return effective ? '正常生效' : '未正常生效'
}

function resolutionClass(effective: boolean): string {
  return effective ? 'good' : 'evil'
}
</script>

<template>
  <section class="panel">
    <h2>账本与结算结论</h2>

    <div class="block">
      <h3>最近一次结算</h3>
      <div v-if="view.lastResolution === null" class="placeholder">还没有结算过能力。</div>
      <div v-else class="resolution">
        <span class="tag" :class="resolutionClass(view.lastResolution.effective)">
          {{ effectiveText(view.lastResolution.effective) }}
        </span>
        <span>{{ seatLabelOf(view.lastResolution.seat) }} 的 {{ view.lastResolution.ability }}</span>
        <span v-if="view.lastResolution.malfunctions.length > 0" class="warn-text">
          原因：{{ view.lastResolution.malfunctions.map(labelOf).join('、') }}
        </span>
        <span v-if="view.lastResolution.note" class="hint">{{ view.lastResolution.note }}</span>
        <span class="mono">序号 {{ view.lastResolution.sequence }}</span>
      </div>
    </div>

    <div class="block">
      <h3>能力使用账本</h3>
      <div v-if="view.abilityUses.length === 0" class="placeholder">还没有能力被使用过。</div>
      <table v-else>
        <thead>
          <tr>
            <th>席位</th>
            <th>能力</th>
            <th>是否生效</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="use in view.abilityUses" :key="`${use.seat}-${use.ability}`">
            <td>{{ seatLabelOf(use.seat) }}</td>
            <td>{{ use.ability }}</td>
            <td>
              <span class="tag" :class="resolutionClass(use.effective)">{{ effectiveText(use.effective) }}</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="block">
      <h3>失效账本</h3>
      <div v-if="view.malfunctions.length === 0" class="placeholder">没有未正常生效或受干扰的记录。</div>
      <table v-else>
        <thead>
          <tr>
            <th>席位</th>
            <th>能力</th>
            <th>原因分类</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(malfunction, index) in view.malfunctions" :key="`${index}-${malfunction.seat}-${malfunction.ability}-${malfunction.kind}`">
            <td>{{ seatLabelOf(malfunction.seat) }}</td>
            <td>{{ malfunction.ability }}</td>
            <td>{{ labelOf(malfunction.kind) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>

<style scoped>
.block + .block {
  margin-top: 10px;
  border-top: 1px dashed var(--line);
  padding-top: 8px;
}

h3 {
  margin: 0 0 4px;
  font-size: 13px;
}

.resolution {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.warn-text {
  color: var(--warn);
}
</style>
