<script setup lang="ts">
/**
 * 麻脸巫婆之夜的死亡裁量（说书人）：窗口开着时显示待定死亡，并接上两条命令。
 *
 * 依据 `docs/standard/rulings.md` R-0030：窗口内恶魔击杀**不直接致死**，由说书人裁定确认 / 阻止；
 * 说书人也可以在窗口内追加死亡（归因为麻脸巫婆）。收口由服务端在越过最后一个恶魔行动时完成，
 * 未裁定的按恶魔攻击的自然结果生效——所以这里只负责"把窗口里的选择发出去"，
 * 关不关窗、谁死谁活都由服务端判（前端不判规则，web/AGENTS §4）。
 */
import type { DeferredDeathDto, StorytellerViewDto } from '@/contracts/game'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  pitHagCasualty,
  resolveDeferredDeath,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { computed, ref } from 'vue'

const props = defineProps<{ view: StorytellerViewDto; sender: CommandSender }>()
const emit = defineEmits<{ outcome: [CommandOutcome] }>()

const busy = ref(false)
const casualtySeat = ref<number | null>(null)
const casualtyNote = ref('')

/** 窗口；null = 今晚没有这个窗口（此时整块不渲染）。 */
const night = computed(() => props.view.pitHagNight)

async function run(action: () => Promise<CommandOutcome>): Promise<void> {
  busy.value = true
  try {
    emit('outcome', await action())
  } finally {
    busy.value = false
  }
}

function resolve(deferred: DeferredDeathDto, killed: boolean): Promise<void> {
  return run(() =>
    resolveDeferredDeath(
      props.sender,
      deferred.target,
      killed,
      killed ? '说书人确认死亡' : '说书人阻止死亡（免死）',
      newIdempotencyKey('pithag-resolve'),
    ),
  )
}

function addCasualty(): Promise<void> {
  const seat = casualtySeat.value
  if (seat === null) {
    return Promise.resolve()
  }

  const note = casualtyNote.value.trim()
  return run(() =>
    pitHagCasualty(
      props.sender,
      seat,
      note === '' ? null : note,
      newIdempotencyKey('pithag-casualty'),
    ),
  )
}
</script>

<template>
  <section
    v-if="night"
    class="panel"
    data-testid="st-pit-hag-night"
    :data-source-seat="night.source"
    :data-closes-after-slot="night.closesAfterSlotIndex"
    :data-deferred-count="night.deferred.length"
  >
    <h2>麻脸巫婆之夜：死亡裁量</h2>
    <p class="hint">
      {{ night.source }} 号创造了恶魔：这一晚的死亡由你决定（rulings.md R-0030）。
      窗口在最后一个能造成死亡的恶魔行动之后关闭——<strong
        >关闭时仍未裁定的待定死亡按恶魔攻击的自然结果生效</strong
      >。
    </p>

    <h3>待定死亡（{{ night.deferred.length }}）</h3>
    <p v-if="night.deferred.length === 0" class="hint" data-testid="st-pit-hag-no-deferred">
      当前没有被恶魔攻击而待裁定的死亡。
    </p>
    <ul v-else class="rows">
      <li
        v-for="deferred in night.deferred"
        :key="deferred.target"
        :data-testid="`st-pit-hag-deferred-${deferred.target}`"
      >
        <span>{{ deferred.target }} 号</span>
        <span class="note">
          被 {{ deferred.source }} 号（{{ deferred.ability }}）攻击：{{ deferred.note }}
          <template v-if="deferred.transformation">
            ——「确认」= 按<strong>侵染</strong>结算：外来者变成新的邪恶方古、原方古死亡，
            被攻击者本身<strong>不死亡</strong>（rulings.md R-0034）。
          </template>
        </span>
        <button
          type="button"
          :data-testid="`st-pit-hag-kill-${deferred.target}`"
          :disabled="busy"
          @click="resolve(deferred, true)"
        >
          {{ deferred.transformation ? '确认侵染' : '确认死亡' }}
        </button>
        <button
          type="button"
          :data-testid="`st-pit-hag-prevent-${deferred.target}`"
          :disabled="busy"
          @click="resolve(deferred, false)"
        >
          阻止（免死）
        </button>
      </li>
    </ul>

    <h3>追加死亡</h3>
    <p class="hint">只能加不能减：追加的死亡归因为麻脸巫婆，不触发「被恶魔杀死」类能力。</p>
    <div class="row">
      <label>
        席位
        <input
          v-model.number="casualtySeat"
          type="number"
          min="1"
          data-testid="st-pit-hag-casualty-seat"
        />
      </label>
      <label>
        说明
        <input v-model="casualtyNote" type="text" data-testid="st-pit-hag-casualty-note" />
      </label>
      <button
        type="button"
        data-testid="st-pit-hag-casualty"
        :disabled="busy || casualtySeat === null"
        @click="addCasualty()"
      >
        让该玩家死亡
      </button>
    </div>
  </section>
</template>
