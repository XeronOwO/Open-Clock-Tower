<script setup lang="ts">
/**
 * 每步摘要（票据「说书人上帝视角」第 3 条）：这一槽位的行动者、他身上全部已观测状态及来源、
 * 能力是否生效、没有合法选项时的行为，以及最近一次请求为什么被作废。
 * 这里只呈现服务端已算好的摘要，前端不做任何推断（web/AGENTS §4）。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { causedByLabelOf, seatLabelOf } from '@/display/format'
import { characterLabelOf, dimensionLabelOf, labelOf, voidReasonLabelOf } from '@/display/labels'

defineProps<{ view: StorytellerViewDto }>()

/** 维度值 → 呈现文案：角色维度显示中文名，其它维度翻枚举。 */
function valueTextOf(dimension: string, value: string): string {
  return dimension === 'Character' ? characterLabelOf(value) : labelOf(value)
}

/** 能力判定 → 一句话结论；null（无法判定）不许写成"未生效"。 */
function effectivenessOf(effective: boolean | null): string {
  if (effective === null) {
    return '无法判定'
  }

  return effective ? '正常生效' : '未正常生效'
}
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
        <span v-if="view.stepDigest">{{ characterLabelOf(view.stepDigest.character) }}</span>
      </div>
      <p class="context">{{ view.currentSlotContext ?? '（服务端未提供上下文）' }}</p>

      <template v-if="view.stepDigest">
        <div class="digest-block">
          <div class="digest-title">行动者状态（已观测维度及来源）</div>
          <table v-if="view.stepDigest.state && view.stepDigest.state.facts.length > 0">
            <thead>
              <tr>
                <th>维度</th>
                <th>当前值</th>
                <th>来由</th>
                <th>归因</th>
                <th>效果</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="fact in view.stepDigest.state.facts" :key="fact.dimension">
                <td>{{ dimensionLabelOf(fact.dimension) }}</td>
                <td>{{ valueTextOf(fact.dimension, fact.value) }}</td>
                <td>{{ fact.reason }}</td>
                <td>{{ causedByLabelOf(fact.causedBy) ?? '—' }}</td>
                <td class="mono">{{ fact.effectId ?? '—' }}</td>
              </tr>
            </tbody>
          </table>
          <p v-else class="hint">该席位还没有可显示的状态账事实（未观测 ≠ 默认值）。</p>
          <p v-if="view.stepDigest.state && view.stepDigest.state.madnesses.length > 0" class="hint">
            疯狂要求：{{ view.stepDigest.state.madnesses.join('、') }}（只由裁定写入，引擎不判定疯狂——R-0003）
          </p>
        </div>

        <div class="digest-block">
          <div class="digest-title">能力是否生效</div>
          <p v-if="view.stepDigest.ability">
            <span class="tag">{{ labelOf(view.stepDigest.ability.basis) }}</span>
            <strong>{{ effectivenessOf(view.stepDigest.ability.effective) }}</strong>
            <span class="mono">{{ view.stepDigest.ability.ability ?? '（能力未知）' }}</span>
            <span v-if="view.stepDigest.ability.malfunction">
              · 原因：{{ labelOf(view.stepDigest.ability.malfunction) }}
            </span>
            <span v-if="view.stepDigest.ability.note" class="hint">
              （{{ view.stepDigest.ability.note }}）
            </span>
          </p>
          <p v-else class="hint">服务端没有给出本步的能力判定。</p>
        </div>

        <div class="digest-block">
          <div class="digest-title">选项与无合法选项时的行为</div>
          <p v-if="view.stepDigest.optionCount === null" class="hint">服务端没有给出选项数量。</p>
          <p v-else-if="view.stepDigest.optionCount > 0">
            合法选项 {{ view.stepDigest.optionCount }} 个；等待玩家选择后结算。
          </p>
          <p v-else>没有合法选项 → 按声明：{{ labelOf(view.stepDigest.onNoOption) }}（R-0009）。</p>
        </div>
      </template>
      <p v-else class="hint">服务端未提供本步摘要。</p>

      <p class="hint">引擎给出状态归因与生效判定；信息真假仍由说书人裁定（D-0002）。</p>
    </div>

    <div v-if="view.lastVoidedRequest" class="digest-block voided">
      <div class="digest-title">最近一次请求作废</div>
      <p>
        <span class="tag warn">{{ voidReasonLabelOf(view.lastVoidedRequest.reason) }}</span>
        <span class="mono">{{ view.lastVoidedRequest.requestId }}</span>
      </p>
      <p v-if="view.lastVoidedRequest.note" class="hint">{{ view.lastVoidedRequest.note }}</p>
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

.digest-block {
  border-top: 1px dashed var(--line);
  margin-top: 8px;
  padding-top: 6px;
}

.digest-title {
  font-size: 13px;
  color: var(--muted);
  margin-bottom: 4px;
}

.voided {
  background: var(--accent-soft);
}
</style>
