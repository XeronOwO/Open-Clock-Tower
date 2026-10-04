<script setup lang="ts">
/**
 * 开局分配：手动逐席选角色，或先「一键配板（随机）」再手改。
 *
 * 一键配板走只读建议查询（`ProposeSetup`，口径见 R-0041 / R-0042）：建议**不落账**——
 * 重摇 / 手改后仍由本组件的「提交分配」走既有命令面（D-0017）。建议里的净分布与钳制说明
 * 照原样展示；失败是服务端的显式结论，不静默吞掉。
 *
 * 注意：只有已经实现夜间契约的角色才能开夜成功——未实现角色在场时，服务端会用
 * plan.contract_missing 显式拒绝，页面把它当作正常结果展示，不掩饰、不静默跳过。
 * 命令必须带连接级凭据（D-0012）：`sender` 把连接与凭据绑在一起，组件拿不到"裸连接"。
 */
import type { SetupProposalDto, StorytellerViewDto } from '@/contracts/game'
import { seatNumbersOf } from '@/display/grimoire'
import { ROSTER, setupModifiersOf, typeLabelOf } from '@/display/labels'
import { seatDisplayOf } from '@/display/format'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  assignCharacters,
  localFailure,
  proposeSetup,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { computed, ref } from 'vue'

const props = defineProps<{
  view: StorytellerViewDto
  sender: CommandSender
  /** 席位名单是会话信息（服务端持有）；这里由宿主配置传入，客户端不猜。 */
  seatCount: number
}>()
const emit = defineEmits<{ outcome: [CommandOutcome] }>()

const note = ref('')
const busy = ref(false)
const proposing = ref(false)
/** 开局后分配不可再用：整块默认收起，需要核对时才展开（默认态只留一行）。 */
const open = ref(false)

/** 是否仍处于"首个阶段开始前"（服务端事实：未开夜就是 NotStarted）。 */
const usable = computed(() => props.view.phase === 'NotStarted')

/** 席位号 → 已选角色 slug。 */
const selection = ref<Record<number, string>>({})

/** 最近一次建议：种子 / 净分布 / 说明；空 = 还没要过建议。 */
const proposalSeed = ref('')
const proposalSummary = ref('')
const proposalNotes = ref<string[]>([])
const proposalFailure = ref('')

/** 可分配席位 = 配置席位名单 ∪ 状态账里已观测到的席位（分配后就常驻在账里）；与魔典圆环同一份口径。 */
const seatNumbers = computed(() => seatNumbersOf(props.view, props.seatCount))

/**
 * 已选角色触发的阵型修正（`[...]` 设置调整，如方古的 `[+1 外来者]`）。
 *
 * 设置调整现在参与服务端的净分布计算（R-0042）；这里把已选的修正摆给说书人看，
 * 避免"静默缺失"。方括号文案与服务端权威数据的对账由规范门禁强制。
 */
const setupNotes = computed(() => setupModifiersOf(Object.values(selection.value)))

/** 要一批建议（种子由服务端生成并回传）；重摇 = 再要一次。 */
async function randomize(): Promise<void> {
  proposing.value = true
  try {
    applyProposal(await proposeSetup(props.sender, null))
  } finally {
    proposing.value = false
  }
}

/** 建议 → 选择表；失败照原样显示（`ok:false` 也是服务端的显式结论）。 */
function applyProposal(proposal: SetupProposalDto): void {
  if (!proposal.ok) {
    proposalFailure.value = proposal.failureMessage ?? proposal.failureCode ?? '配板失败'
    proposalSummary.value = ''
    proposalNotes.value = []
    return
  }

  const next: Record<number, string> = {}
  for (const assignment of proposal.assignments) {
    next[assignment.seat] = assignment.character
  }

  selection.value = next
  proposalSeed.value = proposal.seed
  proposalSummary.value = proposal.distribution
    .map((item) => `${typeLabelOf(item.type)} ${item.count}`)
    .join(' / ')
  proposalNotes.value = [...proposal.notes]
  proposalFailure.value = ''
}

async function submit(): Promise<void> {
  const assignments = Object.entries(selection.value)
    .filter(([, character]) => character.length > 0)
    .map(([seat, character]) => ({ seat: Number(seat), character }))

  if (assignments.length === 0) {
    emit('outcome', localFailure('还没有选择任何角色'))
    return
  }

  busy.value = true
  try {
    const outcome = await assignCharacters(props.sender, assignments, newIdempotencyKey('assign'))
    emit('outcome', outcome)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="panel">
    <h2>开局分配（仅在首个阶段开始前可用）</h2>
    <p class="block-question">开局前把角色分到每一席；也可以先一键配板，再手动改。</p>
    <button
      v-if="!usable"
      type="button"
      class="toggle"
      data-testid="st-assignment-toggle"
      :aria-expanded="open ? 'true' : 'false'"
      @click="open = !open"
    >
      <span>已开局：分配已提交</span>
      <span class="hint">{{ open ? '收起' : '展开查看' }}</span>
    </button>
    <div v-show="usable || open" class="body">
      <div v-if="seatNumbers.length === 0" class="placeholder">
        还没有任何席位可分配。席位名单由服务端的会话信息持有，前端不会凭空造席位。
      </div>
      <template v-else>
        <table>
          <thead>
            <tr>
              <th>席位</th>
              <th>角色</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="seat in seatNumbers" :key="seat">
              <td>{{ seatDisplayOf(seat, view.seatNames) }}</td>
              <td>
                <select v-model="selection[seat]" :data-seat="seat">
                  <option value="">（未选择）</option>
                  <option v-for="profile in ROSTER" :key="profile.slug" :value="profile.slug">
                    {{ profile.name }}（{{ profile.slug }}，{{ profile.type }}）
                  </option>
                </select>
              </td>
            </tr>
          </tbody>
        </table>
        <input v-model="note" placeholder="分配备注（可选）" />
        <ul v-if="setupNotes.length > 0" class="setup-notes" data-testid="st-assignment-setup-notes">
          <li v-for="profile in setupNotes" :key="profile.slug" :data-slug="profile.slug">
            <strong>{{ profile.name }}：</strong>{{ profile.setupModifier }}
          </li>
        </ul>
        <p v-if="proposalSummary.length > 0" class="proposal-summary" data-testid="st-assignment-distribution">
          净分布：{{ proposalSummary }}
          <span v-if="proposalSeed.length > 0" class="proposal-seed">（种子 {{ proposalSeed }}）</span>
        </p>
        <ul v-if="proposalNotes.length > 0" class="proposal-notes" data-testid="st-assignment-proposal-notes">
          <li v-for="(item, index) in proposalNotes" :key="index">{{ item }}</li>
        </ul>
        <p v-if="proposalFailure.length > 0" class="proposal-failure" data-testid="st-assignment-failure">
          {{ proposalFailure }}
        </p>
        <div class="actions">
          <button
            type="button"
            :disabled="busy || proposing"
            data-testid="st-assignment-randomize"
            @click="randomize()"
          >
            {{ proposalSeed.length > 0 ? '重摇' : '一键配板（随机）' }}
          </button>
          <button type="button" class="primary" :disabled="busy || proposing" @click="submit()">提交分配</button>
          <span class="hint">
            提示：未实现夜间契约的角色一旦在场，开夜会被服务端显式拒绝——这是能力边界，不是故障。
          </span>
        </div>
      </template>
    </div>
  </section>
</template>

<style scoped>
.toggle {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  width: 100%;
  text-align: left;
}

.actions {
  display: flex;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
}

.actions .primary {
  margin-top: 6px;
}

.proposal-summary {
  margin: 6px 0 0;
}

.proposal-seed {
  color: #6b7280;
}

.proposal-notes {
  margin: 4px 0 0;
  padding-left: 18px;
}

.proposal-failure {
  margin: 6px 0 0;
  color: #b91c1c;
}
</style>
