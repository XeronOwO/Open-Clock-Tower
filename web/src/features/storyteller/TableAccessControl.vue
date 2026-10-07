<script setup lang="ts">
/**
 * 桌的访问模式开关（D-0037）+ **席位邀请码**（D-0038）：这一桌怎么进人。
 *
 * 公开桌 = 玩家在大厅点一个空席位就坐下；邀请制桌 = 必须凭邀请码（大厅里仍然列出，只是座位点不动）。
 * 平台只转达（D-0002）：能不能改、改成什么由服务端定，这里只做"别让你点空"的呈现（web/AGENTS §4）。
 *
 * 这个开关只管**这一条闸**：开局之后自助入座会自动关闭（迟到的旅行者用邀请码进来），
 * 那是另一条闸、与这里无关——文案要说清，免得说书人以为切回公开桌就能中途放人进来。
 *
 * 邀请码那一段是 D-0038 补上的**唯一签发入口**：此前除了"加入旅行者"时顺带签发，
 * 固定席位根本拿不到码（邀请制桌因此进不了人）。明文只回这一次——
 * 服务端只留哈希，**没有任何地方能把它再读回来**，所以必须当场显示、当场转交。
 */
import { expiryTextOf } from '@/display/format'
import {
  issueSeatInvitation,
  localFailure,
  localSuccess,
  setTableInviteOnly,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { computed, ref } from 'vue'

const props = defineProps<{ inviteOnly: boolean; sender: CommandSender }>()
// 两个出口各司其职：`outcome` 走既有的回执展示与刷新路径，`update:inviteOnly` 把**服务端确认的新值**
// 写回父组件的 ref（说书人拨完开关不一定有推送，界面读数不能押在"等推送"上）。
const emit = defineEmits<{ outcome: [CommandOutcome]; 'update:inviteOnly': [boolean] }>()

const busy = ref(false)

/**
 * 邀请码：席位号由说书人填（面板拿不到完整席位名单——席位账只记"已观测到"的那些）。
 *
 * 席位合不合法由服务端按会话席位名单判（D-0012：客户端声明不可信），填错会拿到一句人话拒绝。
 */
const inviteSeat = ref('')
const issued = ref<{ seat: number; inviteCode: string; expiresAt: string } | null>(null)

const inviteExpiry = computed(() => expiryTextOf(issued.value?.expiresAt))

async function toggle(): Promise<void> {
  const target = !props.inviteOnly
  busy.value = true
  try {
    const applied = await setTableInviteOnly(props.sender, target)
    if (applied === null) {
      // 没拿到确认就不动界面上的读数：宁可显示"没确认"，也不假装切换成功。
      emit('outcome', localFailure('没能切换访问模式：服务端没有确认（凭据失效或连接中断）', 'Failed'))
      return
    }

    emit('update:inviteOnly', applied)
    emit('outcome', localSuccess(`本桌已改为${applied ? '邀请制' : '公开桌'}`))
  } finally {
    busy.value = false
  }
}

/** 为填写的席位签发（或轮换）邀请码：**重复点就是轮换**，上一枚当场作废。 */
async function issue(): Promise<void> {
  const parsed = Number.parseInt(inviteSeat.value.trim(), 10)
  if (!Number.isInteger(parsed) || parsed < 1) {
    emit('outcome', localFailure('席位号必须是正整数'))
    return
  }

  busy.value = true
  try {
    const invitation = await issueSeatInvitation(props.sender, parsed)
    if (invitation === null) {
      emit('outcome', localFailure('没能签发邀请码：服务端没有确认（席位不在名单里、凭据失效或连接中断）', 'Failed'))
      return
    }

    issued.value = invitation
    emit('outcome', localSuccess(`${invitation.seat} 号席的邀请码已签发（上一枚若有则当场作废）`))
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="panel" data-testid="table-access" :data-invite-only="String(inviteOnly)">
    <h2>访问模式</h2>
    <p class="block-question" data-testid="table-access-summary">
      现在{{
        inviteOnly ? '是邀请制桌：必须凭邀请码才能入座。' : '是公开桌：玩家在大厅点一个空席位就能坐下。'
      }}
    </p>
    <button
      type="button"
      class="primary"
      :disabled="busy"
      data-testid="table-access-toggle"
      @click="toggle()"
    >
      {{ inviteOnly ? '改成公开桌' : '改成邀请制' }}
    </button>
    <p class="hint">
      开局之后自助入座会自动关闭（迟到的旅行者用邀请码进来）——那是另一条闸，与这个开关无关。
    </p>

    <h3>席位邀请码</h3>
    <p class="hint">
      邀请制桌、以及开局之后中途到场的人都凭它入座。填一个席位号点签发即可——
      **每一席只有一枚有效邀请码**，重新签发会让上一枚当场作废（码发错人时就用这一下收场）。
    </p>
    <div class="row">
      <label>
        席位
        <input
          v-model="inviteSeat"
          inputmode="numeric"
          placeholder="席位号"
          data-testid="invite-seat"
        />
      </label>
      <button type="button" :disabled="busy" data-testid="invite-issue" @click="issue()">
        签发邀请码
      </button>
    </div>
    <p v-if="issued" class="ticket" data-testid="invite-issued" :data-seat="issued.seat">
      <strong>{{ issued.seat }}</strong> 号席的邀请码（请立即转交；**刷新后不再显示**）——
      玩家在「加入一桌」那一面的「有邀请码？」里粘这一串即可：
      <span class="mono">{{ issued.inviteCode }}</span>
    </p>
    <p v-if="issued" class="hint" data-testid="invite-expiry">
      这一枚到 {{ inviteExpiry }} 之前有效；服务端只存它的哈希，所以没有任何地方能把它再读回来。
    </p>
  </section>
</template>
