<script setup lang="ts">
/**
 * 开局分配：把席位与首版花名册里的角色绑起来（服务端会按会话席位名单与花名册重新校验）。
 *
 * 注意：只有已经实现夜间契约的角色才能开夜成功——未实现角色在场时，服务端会用
 * plan.contract_missing 显式拒绝，页面把它当作正常结果展示，不掩饰、不静默跳过。
 * 命令必须带连接级凭据（D-0012）：`sender` 把连接与凭据绑在一起，组件拿不到"裸连接"。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { seatNumbersOf } from '@/display/grimoire'
import { ROSTER } from '@/display/labels'
import { seatLabelOf } from '@/display/format'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  assignCharacters,
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

/** 席位号 → 已选角色 slug。 */
const selection = ref<Record<number, string>>({})

/** 可分配席位 = 配置席位名单 ∪ 状态账里已观测到的席位（分配后就常驻在账里）；与魔典圆环同一份口径。 */
const seatNumbers = computed(() => seatNumbersOf(props.view, props.seatCount))

async function submit(): Promise<void> {
  const assignments = Object.entries(selection.value)
    .filter(([, character]) => character.length > 0)
    .map(([seat, character]) => ({ seat: Number(seat), character }))

  if (assignments.length === 0) {
    emit('outcome', { ok: false, kind: 'Rejected', sequence: null, message: '还没有选择任何角色' })
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
            <td>{{ seatLabelOf(seat) }}</td>
            <td>
              <select v-model="selection[seat]">
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
      <div class="actions">
        <button type="button" class="primary" :disabled="busy" @click="submit()">提交分配</button>
        <span class="hint">
          提示：未实现夜间契约的角色一旦在场，开夜会被服务端显式拒绝——这是能力边界，不是故障。
        </span>
      </div>
    </template>
  </section>
</template>

<style scoped>
.actions {
  display: flex;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
}

.actions .primary {
  margin-top: 6px;
}
</style>
