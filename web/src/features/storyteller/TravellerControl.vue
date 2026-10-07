<script setup lang="ts">
/**
 * 旅行者管理（D1 / D7 / D-0037）：说书人把旅行者加入本局（任意时刻，含开局前与阶段中）或移出，
 * 并**裁定**玩家自己提出的离场申请（D-0037：玩家发起 → 说书人裁定）。
 *
 * 加入 = 新席位：`seat` 留空时服务端**追加席位**（回执里的 issuedSeat）；
 * **邀请码不在那条回执里**（D-0038）——说书人随后为那一席签发一枚（本组件在加入成功后自动做这一步，
 * 说书人的手感仍是一次点击），明文只出现这一次、只在本组件内存里，刷新后不再显示。
 * 阵营由说书人私下裁定（不进任何公开投影）；邪恶旅行者的揭示目标由说书人指定（一名或全部）。
 * 平台只校验与转达（D-0002）；真正的拒绝在服务端，这里只做"别让你点空"的呈现（web/AGENTS §4）。
 *
 * 「移出」是**始终保留**的直接通道：玩家申请是给玩家的路，不是收走说书人的权限——
 * 训练有素的旅行者要临时走人时，说书人不必先等谁提申请。
 */
import type { StorytellerViewDto } from '@/contracts/game'
import { expiryTextOf, seatTextOf } from '@/display/format'
import { characterLabelOf, ROSTER } from '@/display/labels'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  issueSeatInvitation,
  joinTraveller,
  localFailure,
  localSuccess,
  removeTraveller,
  resolveTravellerDeparture,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { computed, ref } from 'vue'

const props = defineProps<{ view: StorytellerViewDto; sender: CommandSender }>()
const emit = defineEmits<{ outcome: [CommandOutcome] }>()

/** 首版五名旅行者（术语表 §9 的镜像；提交后由服务端按花名册复核，选错会被显式拒绝）。 */
const travellers = ROSTER.filter((profile) => profile.type === '旅行者')

const seatInput = ref('')
const character = ref(travellers[0]?.slug ?? 'deviant')
const alignment = ref('Good')
const revealSeats = ref<number[]>([])
const removeSeat = ref<number | null>(null)
const removeNote = ref('')
const busy = ref(false)

/**
 * 刚签发的邀请码（**明文只在这一刻存在**，服务端只留哈希）。
 *
 * 所以它只活在本组件内存里：刷新 / 重连之后拿不回来——这不是"忘了显示"，是凭据形态本身。
 */
const issued = ref<{ seat: number; inviteCode: string; expiresAt: string } | null>(null)

/** 到期时刻的人话读数（坏值只降级这一行，不白屏——web/AGENTS §4）。 */
const invitedUntil = computed(() => expiryTextOf(issued.value?.expiresAt))

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

  if (outcome.ok && outcome.issuedSeat !== null) {
    // 加入成功 = 席位已经追加好了；凭据另外签发一次（D-0038）。
    // 签不出来就**如实说**：席位已经进去了，只是这一枚码没拿到——再点一次"重新生成"即可。
    const invitation = await issueSeatInvitation(props.sender, outcome.issuedSeat)
    if (invitation === null) {
      emit(
        'outcome',
        localFailure(`已加入 ${outcome.issuedSeat} 号席，但邀请码没签出来：请再点一次「重新生成邀请码」`, 'Failed'),
      )
      return
    }

    issued.value = invitation
  }
}

/** 为当前这一席**重新签发**（轮换）：旧的那一枚当场失效（D-0038）。 */
async function reissue(): Promise<void> {
  if (issued.value === null) {
    return
  }

  busy.value = true
  try {
    const invitation = await issueSeatInvitation(props.sender, issued.value.seat)
    if (invitation === null) {
      emit('outcome', localFailure('没能重新签发邀请码：服务端没有确认（凭据失效或连接中断）', 'Failed'))
      return
    }

    issued.value = invitation
    emit('outcome', localSuccess(`${invitation.seat} 号席的邀请码已重新签发：上一枚当场作废`))
  } finally {
    busy.value = false
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
      新席位 <strong>{{ issued.seat }}</strong> 的邀请码（请立即转交给新到场的玩家；**刷新后不再显示**）——
      他在「加入一桌」那一面的「有邀请码？」里粘这一串即可：
      <span class="mono">{{ issued.inviteCode }}</span>
    </p>
    <!-- 「重新生成」刻意留在这一段**外面**：`traveller-issued` 的正文必须**以邀请码收尾**
         （装置按"最后一段"取码，按钮文案混进去会把码读坏——实测咬到过一次）。 -->
    <p v-if="issued" class="hint" data-testid="traveller-invite-expiry">
      这一枚到 {{ invitedUntil }} 之前有效；重新生成会让上一枚**当场作废**（服务端只存它的哈希，
      所以没有任何地方能把它再读回来）。
      <button type="button" :disabled="busy" data-testid="traveller-reissue" @click="reissue()">
        重新生成邀请码
      </button>
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
    <p class="hint">
      移出保留**席位与账号认领**（重连照样回得去，R-0044 第 6 条），但不再计入任何人数口径
      （流放分母 / 胜负 / 投票）；要让这一席再进人，重新签一枚邀请码即可。
    </p>

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
