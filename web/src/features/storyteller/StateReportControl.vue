<script setup lang="ts">
/**
 * 说书人上报座位状态（上帝视角的写入入口之一）。
 *
 * 口径：只上报**本次真正观测到的维度**——不给的维度不参与判定、也不进状态账（D-0015）；
 * 五个状态维度相互独立（角色 / 阵营 / 生死 / 醉酒 / 中毒），一次只改被勾选的那些。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { ROSTER } from '@/display/labels'
import { newIdempotencyKey } from '@/services/idempotency'
import { reportSeatState, type CommandOutcome } from '@/services/storytellerCommands'
import type { HubConnection } from '@microsoft/signalr'
import { computed, ref } from 'vue'

const props = defineProps<{
  view: StorytellerViewDto
  connection: HubConnection
  seatCount: number
}>()
const emit = defineEmits<{ outcome: [CommandOutcome] }>()

const seat = ref(1)
const reason = ref('')
const causedBy = ref<number | ''>('')
const busy = ref(false)

const life = ref('')
const character = ref('')
const alignment = ref('')
const drunk = ref('')
const poison = ref('')
const useLife = ref(false)
const useCharacter = ref(false)
const useAlignment = ref(false)
const useDrunk = ref(false)
const usePoison = ref(false)

const seatNumbers = computed(() => {
  const seats = new Set<number>()
  for (let index = 1; index <= props.seatCount; index += 1) {
    seats.add(index)
  }

  for (const entry of props.view.seats) {
    seats.add(entry.seat)
  }

  return [...seats].sort((left, right) => left - right)
})

const observedCount = computed(
  () =>
    [useLife.value, useCharacter.value, useAlignment.value, useDrunk.value, usePoison.value].filter(
      Boolean,
    ).length,
)

async function submit(): Promise<void> {
  if (reason.value.trim().length === 0) {
    emit('outcome', { ok: false, kind: 'Rejected', sequence: null, message: '上报必须给出原因' })
    return
  }

  if (observedCount.value === 0) {
    emit('outcome', {
      ok: false,
      kind: 'Rejected',
      sequence: null,
      message: '至少勾选一个本次观测到的维度',
    })
    return
  }

  busy.value = true
  try {
    const outcome = await reportSeatState(
      props.connection,
      {
        seat: seat.value,
        life: useLife.value ? life.value : null,
        character: useCharacter.value ? character.value : null,
        alignment: useAlignment.value ? alignment.value : null,
        drunk: useDrunk.value ? drunk.value : null,
        poison: usePoison.value ? poison.value : null,
        reason: reason.value.trim(),
        causedBySeat: causedBy.value === '' ? null : causedBy.value,
      },
      newIdempotencyKey('report'),
    )
    emit('outcome', outcome)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="panel">
    <h2>上报座位状态（只报本次观测到的维度）</h2>
    <div class="grid">
      <label>
        席位
        <select v-model.number="seat">
          <option v-for="number in seatNumbers" :key="number" :value="number">{{ number }} 号</option>
        </select>
      </label>
      <label>
        归因到（谁造成的）
        <select v-model="causedBy">
          <option value="">（无人可归因）</option>
          <option v-for="number in seatNumbers" :key="number" :value="number">{{ number }} 号</option>
        </select>
      </label>
    </div>

    <div class="dimensions">
      <label>
        <input v-model="useLife" type="checkbox" />
        生死
        <select v-model="life" :disabled="!useLife">
          <option value="Alive">存活</option>
          <option value="Dead">死亡</option>
        </select>
      </label>
      <label>
        <input v-model="useCharacter" type="checkbox" />
        角色
        <select v-model="character" :disabled="!useCharacter">
          <option value="">（请选择角色）</option>
          <option v-for="profile in ROSTER" :key="profile.slug" :value="profile.slug">
            {{ profile.name }}（{{ profile.slug }}）
          </option>
        </select>
      </label>
      <label>
        <input v-model="useAlignment" type="checkbox" />
        阵营
        <select v-model="alignment" :disabled="!useAlignment">
          <option value="Good">善良</option>
          <option value="Evil">邪恶</option>
        </select>
      </label>
      <label>
        <input v-model="useDrunk" type="checkbox" />
        醉酒
        <select v-model="drunk" :disabled="!useDrunk">
          <option value="Sober">清醒</option>
          <option value="Drunk">醉酒</option>
        </select>
      </label>
      <label>
        <input v-model="usePoison" type="checkbox" />
        中毒
        <select v-model="poison" :disabled="!usePoison">
          <option value="Healthy">健康</option>
          <option value="Poisoned">中毒</option>
        </select>
      </label>
    </div>

    <input v-model="reason" placeholder="变化原因（必填，会随事件流记录）" />
    <div class="actions">
      <button type="button" class="primary" :disabled="busy" @click="submit()">上报</button>
      <span class="hint">
        同时中毒且醉酒 = 两种状态并存、互不抵消（《重要细节》三-3）——两个勾都打上即可。
      </span>
    </div>
  </section>
</template>

<style scoped>
.grid {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 6px;
}

.dimensions {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 14px;
  margin-bottom: 6px;
}

.dimensions label {
  display: flex;
  align-items: center;
  gap: 4px;
}

.actions {
  display: flex;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
}
</style>
