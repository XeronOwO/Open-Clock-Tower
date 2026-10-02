<script setup lang="ts">
/**
 * 效果归因链：谁用哪个能力、作用于谁、现在是不是还活着（终止 = 不可逆）。
 * 已终止的效果不隐藏——"因为什么解毒"必须能在这里查到（票据矩阵 5）。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { causedByLabelOf, seatLabelOf } from '@/display/format'
import { characterLabelOf, labelOf } from '@/display/labels'

defineProps<{ view: StorytellerViewDto }>()
</script>

<template>
  <section class="panel">
    <h2>效果归因链（含已终止）</h2>
    <div v-if="view.effects.length === 0" class="placeholder">还没有施加过任何效果。</div>
    <table v-else>
      <thead>
        <tr>
          <th>效果</th>
          <th>类型</th>
          <th>能力</th>
          <th>施加者</th>
          <th>对象</th>
          <th>状态</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="effect in view.effects" :key="effect.effectId">
          <td class="mono">{{ effect.effectId }}</td>
          <td>{{ labelOf(effect.kind) }}</td>
          <td>
            {{ effect.ability }}
            <span v-if="effect.sourceCharacter" class="hint">
              （施加时来源角色：{{ characterLabelOf(effect.sourceCharacter) }}）
            </span>
          </td>
          <td>{{ seatLabelOf(effect.source) }}</td>
          <td>{{ seatLabelOf(effect.target) }}</td>
          <td>
            <span v-if="!effect.terminated" class="tag good">生效中</span>
            <span v-else class="tag evil">已终止</span>
            <template v-if="effect.terminated">
              <span>{{ labelOf(effect.terminationKind) }}</span>
              <span v-if="effect.terminationReason" class="hint">：{{ effect.terminationReason }}</span>
              <span v-if="causedByLabelOf(effect.terminationCausedBy)" class="hint">
                （由 {{ causedByLabelOf(effect.terminationCausedBy) }} 导致）
              </span>
            </template>
          </td>
        </tr>
      </tbody>
    </table>
  </section>
</template>
