<script setup lang="ts">
/**
 * 玩家白天操作区：提名 / 投票 / 公开结果。
 *
 * 白天是公开信息（百科《规则概要》三）：提名、票面、处决都公示；
 * 能不能动由服务端的权限位决定，前端只做使能提示——服务端仍会独立校验（D-0012）。
 */
import type { PlayerDayDto, SeatDisplayNameDto } from '@/contracts/game'
import { exileStatusTextOf, seatTextOf } from '@/display/format'
import HelpTip from '@/features/common/HelpTip.vue'
import VoteDial from '@/features/common/VoteDial.vue'
import { newIdempotencyKey } from '@/services/idempotency'
import { computed, ref } from 'vue'

const props = defineProps<{
  day: PlayerDayDto
  /** 接收者自己的席位：公开生死面上标出"你"，并判断要不要给自己的死亡横幅。 */
  seat: number
  /** 公开的「席位 → 玩家名」（D-0021）：提名 / 投票 / 公告共用同一份拼接口径。 */
  seatNames: SeatDisplayNameDto[]
  /** 提名（父组件把网关包成函数传入；这里不直接持有连接）。 */
  nominate: (seat: number, idempotencyKey: string) => Promise<unknown>
  /** 投票 / 撤回。 */
  vote: (nominationIndex: number, voted: boolean, idempotencyKey: string) => Promise<unknown>
  /** 发起流放提议（任意在局玩家含死者；目标必须是旅行者；R-0044 第 2 条）。 */
  proposeExile: (seat: number, idempotencyKey: string) => Promise<unknown>
  /** 流放表决举手 / 放下（含死者，不耗投票标记；R-0044 第 4 条）。 */
  castExileVote: (exileIndex: number, voted: boolean, idempotencyKey: string) => Promise<unknown>
  /** 屠夫窗口里的额外提名（仅窗口授予席位本人；R-0050）。 */
  nominateExtra: (seat: number, idempotencyKey: string) => Promise<unknown>
}>()
const emit = defineEmits<{ diagnostic: [string] }>()

/** 本面板的席位文本：统一口径（`seatTextOf`）+ 本组件持有的名字映射。 */
function seatText(seat: number | null | undefined): string {
  return seatTextOf(seat, props.seatNames)
}

/** 公开生死状态 → 人话；未知取值原样回显（不猜、不吞，web/AGENTS §4）。 */
function lifeLabel(state: string): string {
  return state === 'Alive' ? '存活' : state === 'Dead' ? '死亡' : state
}

/** 公告里的状态是**变化之后**的状态：Dead = 死亡、Alive = 复活（R-0022）。 */
function announcementLabel(state: string): string {
  return state === 'Alive' ? '复活' : state === 'Dead' ? '死亡' : state
}

/** 自己是否已死亡：只读服务端下发的公开生死面，不在前端推断规则。 */
const selfDead = computed(() =>
  props.day.lives.some((entry) => entry.seat === props.seat && entry.state === 'Dead'),
)

const busy = ref(false)
const nominee = ref<number | null>(null)
const exileTarget = ref<number | null>(null)
const extraNominee = ref<number | null>(null)

/** 当前开放的那一项提名（含钟盘收票呈现）。 */
const openNomination = computed(
  () =>
    props.day.publicView.nominations.find(
      (nomination) => nomination.index === props.day.publicView.openNominationIndex,
    ) ?? null,
)

/** 当前开放的那一条流放（含钟盘收票呈现）。 */
const openExile = computed(
  () =>
    props.day.publicView.exiles.find(
      (exile) => exile.index === props.day.publicView.openExileIndex,
    ) ?? null,
)

/** 钟盘席位号：公开生死面上的全部席位（未观测的席位不出现）。 */
const seatNumbers = computed(() =>
  props.day.lives.map((entry) => entry.seat).sort((left, right) => left - right),
)

/** 举手区的状态文案：只说服务端下发的相位与冻结结论，不自己推断。 */
const voteStateText = computed(() => {
  if (props.day.seatCollected) {
    return props.day.voted ? '本席已收票：你投了赞成' : '本席已收票：你没有举手'
  }

  switch (openNomination.value?.sweep?.phase) {
    case 'Countdown':
      return '倒计时中：举手 = 投这一票'
    case 'Collecting':
      return props.day.voted ? '你举着手：轮到你之前都可以放下' : '收票进行中：轮到你之前都可以举手'
    case 'Interrupted':
      return '收票已中断，等待说书人继续'
    case 'AwaitingCount':
      return '收票已走完，等待说书人计票'
    default:
      return '等待说书人点「开始收票」'
  }
})

/** 流放举手区的状态文案：与提名同款口径（冻结结论优先，其次服务端下发的相位）。 */
const exileVoteStateText = computed(() => {
  if (props.day.exileSeatCollected) {
    return props.day.exileVoted ? '本席已被收票：你投了赞成' : '本席已被收票：你没有举手'
  }

  switch (openExile.value?.sweep?.phase) {
    case 'Countdown':
      return '流放倒计时中：举手 = 投这一票'
    case 'Collecting':
      return props.day.exileVoted
        ? '你举着手：轮到你之前都可以放下'
        : '流放收票中：轮到你之前都可以举手'
    case 'Interrupted':
      return '流放收票已中断，等待说书人继续'
    case 'AwaitingCount':
      return '流放收票已走完，等待说书人计票'
    default:
      return '等待说书人点「开始流放收票」'
  }
})

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
  const index = props.day.publicView.openNominationIndex
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
    emit('diagnostic', `举手失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    busy.value = false
  }
}

/** 发起流放提议：目标由服务端复核（必须是本局在局旅行者、今天还没被提议过）。 */
async function submitExile(): Promise<void> {
  if (exileTarget.value === null) {
    return
  }

  busy.value = true
  try {
    const problem = outcomeProblem(
      await props.proposeExile(exileTarget.value, newIdempotencyKey('exile')),
    )
    if (problem !== null) {
      emit('diagnostic', problem)
    } else {
      exileTarget.value = null
    }
  } catch (error) {
    emit('diagnostic', `流放提议失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    busy.value = false
  }
}

/** 流放表决举手 / 放下（先举也算、过时不候；本席收票后由服务端锁死）。 */
async function submitExileVote(voted: boolean): Promise<void> {
  const index = props.day.publicView.openExileIndex
  if (index === null) {
    return
  }

  busy.value = true
  try {
    const problem = outcomeProblem(
      await props.castExileVote(index, voted, newIdempotencyKey('exile-vote')),
    )
    if (problem !== null) {
      emit('diagnostic', problem)
    }
  } catch (error) {
    emit('diagnostic', `流放举手失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    busy.value = false
  }
}

/** 屠夫窗口里的额外提名（不占当日提名次数、可提名今天已被提名过的人；R-0050）。 */
async function submitExtraNomination(): Promise<void> {
  if (extraNominee.value === null) {
    return
  }

  busy.value = true
  try {
    const problem = outcomeProblem(
      await props.nominateExtra(extraNominee.value, newIdempotencyKey('extra-nominate')),
    )
    if (problem !== null) {
      emit('diagnostic', problem)
    } else {
      extraNominee.value = null
    }
  } catch (error) {
    emit('diagnostic', `额外提名失败：${error instanceof Error ? error.message : String(error)}`)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section
    class="panel"
    data-testid="player-day"
    :data-day-number="day.publicView.dayNumber"
    :data-day-status="day.publicView.status"
  >
    <h2>白天 · 第 {{ day.publicView.dayNumber }} 天<HelpTip topic="execution" /></h2>
    <p class="block-question">提名与投票都是公开信息；能不能行动由服务端判定。</p>

    <p v-if="selfDead" class="dead-note" data-testid="player-self-dead" :data-seat="seat">
      你已死亡：不能发起提名{{ day.canVote ? '；你仍有投票标记，本白天还能再投一次票' : '；投票标记已经用完' }}；
      流放不受生死限制——你仍可发起流放，也可在流放表决里举手。
    </p>

    <div v-if="day.publicView.status === 'Open'" class="row">
      <template v-if="day.canNominate">
        <select v-model.number="nominee" data-testid="player-nominee-select">
          <option :value="null" disabled>选择要提名的席位</option>
          <option v-for="candidate in day.candidates" :key="candidate" :value="candidate">
            {{ seatText(candidate) }}
          </option>
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
      <template v-else-if="day.publicView.openNominationIndex !== null">
        <VoteDial
          v-if="openNomination"
          :seat-numbers="seatNumbers"
          :nominator="openNomination.nominator"
          :nominee="openNomination.nominee"
          :current-seat="openNomination.sweep?.currentSeat ?? null"
          :collected="openNomination.sweep?.collected ?? []"
          :hands-raised="openNomination.handsRaised"
          :phase="openNomination.sweep?.phase ?? null"
          :next-beat-milliseconds="openNomination.sweep?.nextBeatMilliseconds ?? null"
        />
        <span class="hint" data-testid="player-vote-state">
          {{ voteStateText }}
        </span>
        <button
          type="button"
          class="primary"
          data-testid="player-vote-yes"
          :disabled="busy || !day.canVote || day.voted"
          @click="castVote(true)"
        >
          举手（投这一票）
        </button>
        <button
          type="button"
          data-testid="player-vote-withdraw"
          :disabled="busy || !day.canVote || !day.voted"
          @click="castVote(false)"
        >
          放下手
        </button>
      </template>
      <span v-else class="hint" data-testid="player-day-waiting">现在没有开放投票的提名。</span>
    </div>

    <!-- 旅行者与流放（R-0044）：发起 / 举手都是公开流程；能不能动由服务端的权限位决定。 -->
    <div v-if="day.publicView.status === 'Open'" class="row" data-testid="player-exile-row">
      <template v-if="day.canProposeExile && day.exileCandidates.length > 0">
        <select v-model.number="exileTarget" data-testid="player-exile-select">
          <option :value="null" disabled>选择要流放的旅行者</option>
          <option v-for="candidate in day.exileCandidates" :key="candidate" :value="candidate">
            {{ seatText(candidate) }}
          </option>
        </select>
        <button
          type="button"
          class="primary"
          data-testid="player-propose-exile"
          :disabled="busy || exileTarget === null"
          @click="submitExile()"
        >
          发起流放
        </button>
      </template>
      <span v-else class="hint" data-testid="player-exile-hint">
        {{ day.canProposeExile ? '今天没有可提议流放的旅行者。' : '现在不能发起流放（已有未结清的流放，或你不在局）。' }}
      </span>
    </div>

    <div v-if="openExile" class="row" data-testid="player-exile" :data-exile-index="openExile.index">
      <VoteDial
        dial-kind="exile"
        :seat-numbers="seatNumbers"
        :nominator="openExile.proposer"
        :nominee="openExile.target"
        :current-seat="openExile.sweep?.currentSeat ?? null"
        :collected="openExile.sweep?.collected ?? []"
        :hands-raised="openExile.handsRaised"
        :phase="openExile.sweep?.phase ?? null"
        :next-beat-milliseconds="openExile.sweep?.nextBeatMilliseconds ?? null"
      />
      <span class="hint" data-testid="player-exile-vote-state">{{ exileVoteStateText }}</span>
      <button
        type="button"
        class="primary"
        data-testid="player-exile-vote-yes"
        :disabled="busy || !day.canVoteExile || day.exileVoted"
        @click="submitExileVote(true)"
      >
        举手（赞成流放）
      </button>
      <button
        type="button"
        data-testid="player-exile-vote-withdraw"
        :disabled="busy || !day.canVoteExile || !day.exileVoted"
        @click="submitExileVote(false)"
      >
        放下手
      </button>
    </div>

    <div v-if="day.canNominateExtra" class="row" data-testid="player-extra-nomination">
      <span class="hint">屠夫窗口：你可以再发起一次提名（可提名今天已被提名过的人，R-0050）。</span>
      <select v-model.number="extraNominee" data-testid="player-extra-nominee-select">
        <option :value="null" disabled>选择要提名的席位</option>
        <option v-for="candidate in day.extraNominationCandidates" :key="candidate" :value="candidate">
          {{ seatText(candidate) }}
        </option>
      </select>
      <button
        type="button"
        class="primary"
        data-testid="player-nominate-extra"
        :disabled="busy || extraNominee === null"
        @click="submitExtraNomination()"
      >
        额外提名
      </button>
    </div>

    <p v-if="day.publicView.status !== 'Open'" class="hint" data-testid="player-day-closed">白天已结束。</p>

    <p
      v-if="day.publicView.aboutToBeExecuted !== null"
      data-testid="player-about-to-be-executed"
      :data-seat="day.publicView.aboutToBeExecuted"
    >
      即将被处决：{{ seatText(day.publicView.aboutToBeExecuted) }}
    </p>
    <p v-if="day.publicView.executed !== null" data-testid="player-executed" :data-seat="day.publicView.executed">
      已处决：{{ seatText(day.publicView.executed) }}
    </p>

    <h3 v-if="day.publicView.nominations.length > 0" class="sub-title">提名记录<HelpTip topic="votes" /></h3>
    <ul
      class="nominations"
      data-testid="player-day-nominations"
      :data-nomination-count="day.publicView.nominations.length"
    >
      <li
        v-for="nomination in day.publicView.nominations"
        :key="nomination.index"
        :data-nomination-index="nomination.index"
        :data-nomination-status="nomination.status"
        :data-nomination-votes="nomination.votes"
      >
        {{ seatText(nomination.nominator) }} 提名 {{ seatText(nomination.nominee) }} ——
        {{ nomination.votes }} 票（{{ nomination.status === 'Counted' ? '已计票' : '投票中' }}）
      </li>
    </ul>

    <h3 v-if="day.publicView.exiles.length > 0" class="sub-title">流放记录</h3>
    <ul
      class="nominations"
      data-testid="player-day-exiles"
      :data-exile-count="day.publicView.exiles.length"
    >
      <li
        v-for="exile in day.publicView.exiles"
        :key="exile.index"
        :data-exile-index="exile.index"
        :data-exile-status="exile.status"
        :data-exile-votes="exile.votes"
        :data-exile-conclusion="exile.conclusion ?? ''"
      >
        {{ seatText(exile.proposer) }} 提议流放 {{ seatText(exile.target) }} ——
        {{ exile.votes }} 票（{{ exileStatusTextOf(exile) }}）
      </li>
    </ul>

    <section class="board" data-testid="player-lives" :data-life-count="day.lives.length">
      <h3>小镇生死</h3>
      <ul class="board-list">
        <li
          v-for="entry in day.lives"
          :key="entry.seat"
          :data-seat="entry.seat"
          :data-life="entry.state"
          :class="{ 'is-self': entry.seat === seat }"
        >
          {{ seatText(entry.seat) }}{{ entry.seat === seat ? '（你）' : '' }} —— {{ lifeLabel(entry.state) }}
        </li>
      </ul>
      <p v-if="day.lives.length === 0" class="hint">还没有公开的生死记录。</p>
    </section>

    <section
      class="announcements"
      data-testid="player-life-announcements"
      :data-announcement-count="day.announcements.length"
    >
      <h3>生死公告 · 第 {{ day.publicView.dayNumber }} 天</h3>
      <ul v-if="day.announcements.length > 0" class="announcements-list">
        <li
          v-for="(entry, index) in day.announcements"
          :key="`${index}-${entry.seat}`"
          :data-seat="entry.seat"
          :data-state="entry.state"
        >
          {{ seatText(entry.seat) }} {{ announcementLabel(entry.state) }}
        </li>
      </ul>
      <p v-else class="hint">本日还没有死亡或复活公告。</p>
    </section>
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

.sub-title {
  margin: 6px 0 2px;
  font-size: 12px;
  color: var(--ink-soft);
}

.dead-note {
  margin: 6px 0;
  padding: 6px 8px;
  border: 1px solid var(--warn);
  border-radius: 6px;
  color: var(--warn);
  font-size: 13px;
}

.board,
.announcements {
  margin-top: 10px;
}

.board h3,
.announcements h3 {
  margin: 0 0 4px;
  font-size: 14px;
}

.board-list,
.announcements-list {
  margin: 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.board-list .is-self {
  font-weight: 600;
}
</style>
