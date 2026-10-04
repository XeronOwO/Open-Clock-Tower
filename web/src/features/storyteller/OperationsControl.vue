<script setup lang="ts">
/**
 * 说书人兜底操作（D-0014）：开夜、强推、接管 / 交还、重建房间。
 * 每个动作都要求一句原因——原因会进事件流，事后能回答"这一步为什么被强推"。
 * 命令必须带连接级凭据（D-0012）。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  forceAdvance,
  rebuildRoom,
  releaseControl,
  startNight,
  takeOver,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { ref } from 'vue'

defineProps<{ view: StorytellerViewDto; sender: CommandSender }>()
const emit = defineEmits<{ outcome: [CommandOutcome] }>()

const nightNumber = ref(1)
const variant = ref('Recommended')
const reason = ref('')
const busy = ref(false)

async function run(action: () => Promise<CommandOutcome>): Promise<void> {
  busy.value = true
  try {
    emit('outcome', await action())
  } finally {
    busy.value = false
  }
}

function reasonOrFallback(): string {
  return reason.value.trim().length > 0 ? reason.value.trim() : '说书人兜底操作'
}
</script>

<template>
  <section class="panel">
    <h2>兜底与推进</h2>
    <p class="block-question">开夜、强推、接管与重建——卡住时的兜底入口。</p>
    <div class="row">
      <label>
        第几夜
        <input v-model.number="nightNumber" type="number" min="1" max="99" class="narrow" />
      </label>
      <label>
        口径
        <select v-model="variant">
          <option value="Original">Original（原始顺序）</option>
          <option value="Recommended">Recommended（推荐顺序）</option>
        </select>
      </label>
      <button
        type="button"
        class="primary"
        :disabled="busy"
        @click="run(() => startNight(sender, nightNumber, variant, newIdempotencyKey('night')))"
      >
        开夜（服务端按顺序表建表）
      </button>
    </div>

    <input v-model="reason" placeholder="原因（强推 / 接管 / 交还 / 重建必填，会记进事件流）" />
    <div class="row">
      <button
        type="button"
        :disabled="busy"
        @click="run(() => forceAdvance(sender, reasonOrFallback(), newIdempotencyKey('force')))"
      >
        强推当前槽位
      </button>
      <button
        type="button"
        :disabled="busy"
        @click="run(() => takeOver(sender, reasonOrFallback(), newIdempotencyKey('takeover')))"
      >
        接管
      </button>
      <button
        type="button"
        :disabled="busy || view.control !== 'StorytellerTakeover'"
        @click="run(() => releaseControl(sender, reasonOrFallback(), newIdempotencyKey('release')))"
      >
        交还自动化
      </button>
      <button
        type="button"
        :disabled="busy"
        @click="run(() => rebuildRoom(sender, reasonOrFallback(), newIdempotencyKey('rebuild')))"
      >
        重建房间
      </button>
    </div>
    <p class="hint">
      重建会按事件日志重放，并与内存状态 / 快照对比；结果在下方回执里。
    </p>
  </section>
</template>

<style scoped>
.row {
  display: flex;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
  margin-bottom: 6px;
}

.narrow {
  width: 70px;
}

label {
  display: flex;
  gap: 6px;
  align-items: center;
}
</style>
