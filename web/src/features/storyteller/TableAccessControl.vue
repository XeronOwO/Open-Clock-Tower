<script setup lang="ts">
/**
 * 桌的访问模式开关（D-0037）：公开桌 ⇄ 邀请制桌。
 *
 * 公开桌 = 玩家在大厅点一个空席位就坐下；邀请制桌 = 必须凭邀请码（大厅里仍然列出，只是座位点不动）。
 * 平台只转达（D-0002）：能不能改、改成什么由服务端定，这里只做"别让你点空"的呈现（web/AGENTS §4）。
 *
 * 这个开关只管**这一条闸**：开局之后自助入座会自动关闭（迟到的旅行者用邀请码进来），
 * 那是另一条闸、与这里无关——文案要说清，免得说书人以为切回公开桌就能中途放人进来。
 */
import {
  localFailure,
  localSuccess,
  setTableInviteOnly,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { ref } from 'vue'

const props = defineProps<{ inviteOnly: boolean; sender: CommandSender }>()
// 两个出口各司其职：`outcome` 走既有的回执展示与刷新路径，`update:inviteOnly` 把**服务端确认的新值**
// 写回父组件的 ref（说书人拨完开关不一定有推送，界面读数不能押在"等推送"上）。
const emit = defineEmits<{ outcome: [CommandOutcome]; 'update:inviteOnly': [boolean] }>()

const busy = ref(false)

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
  </section>
</template>
