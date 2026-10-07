<script setup lang="ts">
/**
 * 旅行者管理（D1 / D7 / D-0037）：说书人把旅行者加入本局（任意时刻，含开局前与阶段中）或移出，
 * 并**裁定**玩家自己提出的离场申请（D-0037：玩家发起 → 说书人裁定）。
 *
 * 加入 = 新席位：`seat` 留空时服务端**追加席位**并签发新票据（回执里的 issuedSeat /
 * issuedSeatTicket，说书人转交给新到场的玩家）；指定席位 = 落在本局尚未分配的高号席。
 * 阵营由说书人私下裁定（不进任何公开投影）；邪恶旅行者的揭示目标由说书人指定（一名或全部）。
 * 平台只校验与转达（D-0002）；真正的拒绝在服务端，这里只做"别让你点空"的呈现（web/AGENTS §4）。
 *
 * 「移出」是**始终保留**的直接通道：玩家申请是给玩家的路，不是收走说书人的权限——
 * 训练有素的旅行者要临时走人时，说书人不必先等谁提申请。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { seatTextOf } from '@/display/format'
import { characterLabelOf, ROSTER } from '@/display/labels'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  joinTraveller,
  localFailure,
  removeTraveller,
  resolveTravellerDeparture,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { computed, ref } from 'vue'

const props = defineProps<{ view: StorytellerViewDto; sender: CommandSender; gameId?: string }>()
const emit = defineEmits<{ outcome: [CommandOutcome] }>()

/**
 * 邀请码：**桌标识 + 席位票据**。
 *
 * 光有票据，玩家那一面不知道该连哪一桌（桌标识属于连接，多桌 D-0024）；
 * 说书人把这一串交出去，玩家在「有邀请码？」里粘一次就能入座——中途到场的旅行者走的就是这条路。
 */
const inviteCode = computed(() =>
  props.gameId === undefined || props.gameId.length === 0
    ? (issued.value?.ticket ?? '')
    : `${props.gameId}:${issued.value?.ticket ?? ''}`,
)

/** 首版五名旅行者（术语表 §9 的镜像；提交后由服务端按花名册复核，选错会被显式拒绝）。 */
const travellers = ROSTER.filter((profile) => profile.type === '旅行者')

const seatInput = ref('')
const character = ref(travellers[0]?.slug ?? 'deviant')
const alignment = ref('Good')
const revealSeats = ref<number[]>([])
const removeSeat = ref<number | null>(null)
const removeNote = ref('')
const busy = ref(false)

/** 加入成功后要转交的票据：只在本组件内存里，刷新 / 重连后不再显示（它是入场凭据）。 */
const issued = ref<{ seat: number; ticket: string } | null>(null)

/** 账上已观测的席位（含角色名）：揭示范围与移出目标都从这里选，服务端再复核。 */
const seats = computed(() =>
  props.view.seats
    .map((entry) => ({
      seat: entry.seat,
      character: entry.facts.find((fact) => fact.dimension === 'Character')?.value ?? null,
    }))
    .sort((left, right) => left.seat - right.seat),
)

const isEvil = computed(() => alignment.value === 'Evil')

function seatText(seat: number): string {
  return seatTextOf(seat, props.view.seatNames)
}

async function run(action: () => Promise<CommandOutcome>): Promise<CommandOutcome> {
  busy.value = true
  try {
    const outcome = await action()
    emit('outcome', outcome)
    return outcome
  } finally {
    busy.value = false
  }
}

async function join(): Promise<void> {
  const raw = seatInput.value.trim()
  const parsed = raw.length === 0 ? null : Number.parseInt(raw, 10)
  if (parsed !== null && (!Number.isInteger(parsed) || parsed < 1)) {
    emit('outcome', localFailure('席位号必须是正整数（留空 = 服务端追加新席位）'))
    return
  }

  const outcome = await run(() =>
    joinTraveller(
      props.sender,
      parsed,
      character.value,
      alignment.value,
      isEvil.value ? [...revealSeats.value] : null,
      newIdempotencyKey('join-traveller'),
    ),
  )

  if (outcome.ok && outcome.issuedSeat !== null && outcome.issuedSeatTicket !== null) {
    issued.value = { seat: outcome.issuedSeat, ticket: outcome.issuedSeatTicket }
  }
}

async function remove(): Promise<void> {
  if (removeSeat.value === null) {
    emit('outcome', localFailure('先选择要移出的席位'))
    return
  }

  const seat = removeSeat.value
  await run(() =>
    removeTraveller(props.sender, seat, removeNote.value.trim() || null, newIdempotencyKey('remove-traveller')),
  )
}

/**
 * 待批离场申请（D-0037），按提出顺序；没有申请时整块不渲染（不留空壳标题）。
 * 理由只说书人与申请人本人可见——它不在任何公开投影里（D-0012 §4.3）。
 */
const departureRequests = computed(() => props.view.departureRequests)

/** 逐行的裁定说明（可选，会进事件流）：按席位分开存，免得相邻两行的说明串台。 */
const rulingNotes = ref<Record<number, string>>({})

/** 批准 = 执行离场（席位与票据保留，不再计入任何人数口径）；驳回 = 本局继续。 */
async function resolveDeparture(seat: number, approved: boolean): Promise<void> {
  const note = (rulingNotes.value[seat] ?? '').trim()
  const outcome = await run(() =>
    resolveTravellerDeparture(
      props.sender,
      seat,
      approved,
      note.length > 0 ? note : null,
      newIdempotencyKey('resolve-departure'),
    ),
  )

  if (outcome.ok) {
    // 这条申请已经有结论了：清掉这一行的字，免得下一条申请沿用上一行的说明。
    delete rulingNotes.value[seat]
  }
}
</script>

<template>
  <section class="panel" data-testid="st-traveller">
    <h2>旅行者</h2>
    <p class="block-question">
      任意时刻可加入 / 移出：加入会公开宣告「席位 + 角色 + 能力」，阵营不公开（说书人私下裁定）。
    </p>

    <div class="row">
      <label>
        角色
        <select v-model="character" data-testid="traveller-character">
          <option v-for="profile in travellers" :key="profile.slug" :value="profile.slug">
            {{ profile.name }}
          </option>
        </select>
      </label>
      <label>
        阵营
        <select v-model="alignment" data-testid="traveller-alignment">
          <option value="Good">善良</option>
          <option value="Evil">邪恶</option>
        </select>
      </label>
      <label>
        席位
        <input
          v-model="seatInput"
          type="number"
          min="1"
          max="1000"
          placeholder="留空 = 追加"
          class="narrow"
          data-testid="traveller-seat"
        />
      </label>
      <button type="button" class="primary" :disabled="busy" data-testid="traveller-join" @click="join()">
        加入
      </button>
    </div>

    <div v-if="isEvil" class="reveal" data-testid="traveller-reveal">
      <span class="hint">邪恶旅行者要告知的存活恶魔（一名或全部；服务端会校验）：</span>
      <label v-for="entry in seats" :key="entry.seat" class="choice">
        <input v-model="revealSeats" type="checkbox" :value="entry.seat" :data-seat="entry.seat" />
        {{ seatText(entry.seat) }}
        <span v-if="entry.character" class="hint">（{{ characterLabelOf(entry.character) }}）</span>
      </label>
    </div>

    <p v-if="issued" class="ticket" data-testid="traveller-issued" :data-seat="issued.seat">
      新席位 <strong>{{ issued.seat }}</strong> 的邀请码（请立即转交给新到场的玩家；刷新后不再显示）——
      他在「加入一桌」那一面的「有邀请码？」里粘这一串即可：
      <span class="mono">{{ inviteCode }}</span>
    </p>

    <div class="row">
      <label>
        移出
        <select v-model.number="removeSeat" data-testid="traveller-remove-seat">
          <option :value="null">— 请选择 —</option>
          <option v-for="entry in seats" :key="entry.seat" :value="entry.seat">
            {{ seatText(entry.seat) }}
          </option>
        </select>
      </label>
      <input v-model="removeNote" placeholder="移出说明（可选，会进事件流）" />
      <button type="button" :disabled="busy" data-testid="traveller-remove" @click="remove()">移出</button>
    </div>
    <p class="hint">移出保留席位与票据，但不再计入任何人数口径（流放分母 / 胜负 / 投票；R-0044 第 6 条）。</p>

    <!-- 待批离场申请（D-0037）：玩家发起、说书人裁定。没有申请时整块不渲染。 -->
    <div v-if="departureRequests.length > 0" class="departures" data-testid="traveller-departures">
      <p class="hint">
        有人申请离场：批准即执行离场（席位与票据保留，不再计入任何人数口径），驳回则本局继续。
      </p>
      <div
        v-for="request in departureRequests"
        :key="request.seat"
        class="departure"
        :data-departure-seat="request.seat"
      >
        <span class="who">{{ seatText(request.seat) }}</span>
        <span v-if="request.note !== null" class="reason">理由：{{ request.note }}</span>
        <span v-else class="hint">（没有写理由）</span>
        <input
          v-model="rulingNotes[request.seat]"
          placeholder="裁定说明（可选，会进事件流）"
          data-testid="departure-ruling-note"
        />
        <button
          type="button"
          :disabled="busy"
          data-testid="departure-approve"
          @click="resolveDeparture(request.seat, true)"
        >
          批准
        </button>
        <button
          type="button"
          :disabled="busy"
          data-testid="departure-reject"
          @click="resolveDeparture(request.seat, false)"
        >
          驳回
        </button>
      </div>
    </div>
  </section>
</template>

<style scoped>
.reveal {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin: 6px 0;
}

.reveal .choice {
  display: inline-flex;
  gap: 4px;
  align-items: center;
}

.ticket {
  margin: 6px 0;
  padding: 6px 8px;
  border: 1px solid var(--line);
  border-radius: 6px;
  background: #fffbe6;
}

.ticket .mono {
  word-break: break-all;
}

/* 待批离场申请：一行一条，说明与两个裁定按钮同排（说书人要一眼看全"谁、为什么、怎么办"）。 */
.departures {
  margin-top: 8px;
  padding-top: 6px;
  border-top: 1px solid var(--line);
}

.departure {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin: 6px 0;
}

.departure .who {
  font-weight: 600;
}

.departure .reason {
  font-size: 13px;
  color: var(--ink-soft);
}
</style>
