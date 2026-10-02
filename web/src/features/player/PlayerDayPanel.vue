<script setup lang="ts">
/**
 * 玩家白天操作区：提名 / 投票 / 公开结果。
 *
 * 白天是公开信息（百科《规则概要》三）：提名、票面、处决都公示；
 * 能不能动由服务端的权限位决定，前端只做使能提示——服务端仍会独立校验（D-0012）。
 */
import type { PlayerDayDto } from '@/contracts/game'
import { seatLabelOf } from '@/display/format'
import { newIdempotencyKey } from '@/services/idempotency'
import { ref } from 'vue'

const props = defineProps<{
  day: PlayerDayDto
  /** 提名（父组件把网关包成函数传入；这里不直接持有连接）。 */
  nominate: (seat: number, idempotencyKey: string) => Promise<unknown>
  /** 投票 / 撤回。 */
  vote: (nominationIndex: number, voted: boolean, idempotencyKey: string) => Promise<unknown>
}>()
const emit = defineEmits<{ diagnostic: [string] }>()

const busy = ref(false)
const nominee = ref<number | null>(null)

/** 服务端回执 → 人话；成功返回 null。 */
function outcomeProblem(raw: unknown): string | null {
  if (raw === null || typeof raw !== 'object') {
    return '回执形状不可识别'
  }

  const result = raw as Record<string, unknown>
  const kind = typeof result['kind'] === 'string' ? (result['kind'] as string) : ''
  if (kind === 'Accepted' || kind === 'Duplicate') {
    return null
  }

  const code = typeof result['rejectionCode'] === 'string' ? (result['rejectionCode'] as string) : ''
  const message = typeof result['rejectionMessage'] === 'string' ? (result['rejectionMessage'] as string) : ''
  return `命令未成功：${code}${message.length > 0 ? ` ${message}` : ''}`
}

async function submitNomination(): Promise<void> {
  if (nominee.value === null) {
    return
  }

  busy.value = true
  try {
    const problem = outcomeProblem(await props.nominate(nominee.value, newIdempotencyKey('nominate')))
    if (problem !== null) {
      emit('diagnostic', problem)
    } else {
      nominee.value = null
    }
  } catch (error) {
    emit('diagnostic', `提名失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    busy.value = false
  }
}

async function castVote(voted: boolean): Promise<void> {
  const index = props.day.publicFacts.openNominationIndex
  if (index === null) {
    return
  }

  busy.value = true
  try {
    const problem = outcomeProblem(await props.vote(index, voted, newIdempotencyKey('vote')))
    if (problem !== null) {
      emit('diagnostic', problem)
    }
  } catch (error) {
    emit('diagnostic', `投票失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section
    class="panel"
    data-testid="player-day"
    :data-day-number="day.publicFacts.dayNumber"
    :data-day-status="day.publicFacts.status"
  >
    <h2>白天 · 第 {{ day.publicFacts.dayNumber }} 天</h2>
    <p class="hint">提名与投票都是公开信息；能不能行动由服务端判定，这里的按钮只是使能提示。</p>

    <div v-if="day.publicFacts.status === 'Open'" class="row">
      <template v-if="day.canNominate">
        <select v-model.number="nominee" data-testid="player-nominee-select">
          <option :value="null" disabled>选择要提名的席位</option>
          <option v-for="seat in day.candidates" :key="seat" :value="seat">{{ seatLabelOf(seat) }}</option>
        </select>
        <button
          type="button"
          class="primary"
          data-testid="player-nominate"
          :disabled="busy || nominee === null"
          @click="submitNomination()"
        >
          提名
        </button>
      </template>
      <template v-else-if="day.publicFacts.openNominationIndex !== null">
        <span class="hint" data-testid="player-vote-state">
          {{ day.voted ? '你已投赞成' : '你还没投票' }}
        </span>
        <button
          type="button"
          data-testid="player-vote-yes"
          :disabled="busy || !day.canVote || day.voted"
          @click="castVote(true)"
        >
          投赞成
        </button>
        <button
          type="button"
          data-testid="player-vote-withdraw"
          :disabled="busy || !day.canVote || !day.voted"
          @click="castVote(false)"
        >
          撤回
        </button>
      </template>
      <span v-else class="hint" data-testid="player-day-waiting">现在没有开放投票的提名。</span>
    </div>
    <p v-else class="hint" data-testid="player-day-closed">白天已结束。</p>

    <p
      v-if="day.publicFacts.aboutToBeExecuted !== null"
      data-testid="player-about-to-be-executed"
      :data-seat="day.publicFacts.aboutToBeExecuted"
    >
      即将被处决：{{ seatLabelOf(day.publicFacts.aboutToBeExecuted) }}
    </p>
    <p v-if="day.publicFacts.executed !== null" data-testid="player-executed" :data-seat="day.publicFacts.executed">
      已处决：{{ seatLabelOf(day.publicFacts.executed) }}
    </p>

    <ul
      class="nominations"
      data-testid="player-day-nominations"
      :data-nomination-count="day.publicFacts.nominations.length"
    >
      <li
        v-for="nomination in day.publicFacts.nominations"
        :key="nomination.index"
        :data-nomination-index="nomination.index"
        :data-nomination-status="nomination.status"
        :data-nomination-votes="nomination.votes"
      >
        {{ seatLabelOf(nomination.nominator) }} 提名 {{ seatLabelOf(nomination.nominee) }} ——
        {{ nomination.votes }} 票（{{ nomination.status === 'Counted' ? '已计票' : '投票中' }}）
      </li>
    </ul>
  </section>
</template>

<style scoped>
.row {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
  margin-bottom: 6px;
}

.nominations {
  margin: 4px 0 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
</style>
