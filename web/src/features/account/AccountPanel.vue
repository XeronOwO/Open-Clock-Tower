<script setup lang="ts">
/**
 * 已登录的账号区（D-0021 / D-0027）：一行摘要 + 改名 / 登出，收起来才不占地方。
 *
 * 它**只管已登录之后的事**：注册 / 登录 / 找回口令都在门上（`AccountGate.vue`）。
 * 拆开的理由正是需求方那句"到处硬塞"——此前这一张卡同时是注册表单、登录表单、
 * 找回表单、资料区与桌列表的邻居，用户看不出该先做哪一步。
 *
 * 秘密纪律不变：账号会话只活在 `accountSession` 的网关内存里，这里不落盘、不渲染凭据；
 * 一次性恢复码只给本人看一次，抄下即清。
 */
import { ref } from 'vue'
import * as session from '@/services/accountSession'

const profile = session.profile
const busy = session.busy
const notice = session.notice
const recoveryCode = session.recoveryCode

const folded = ref(true)
const renameTo = ref('')

async function submitRename(): Promise<void> {
  if (renameTo.value.trim().length === 0) {
    return
  }

  if (await session.rename(renameTo.value.trim())) {
    renameTo.value = ''
  }
}
</script>

<template>
  <section v-if="profile !== null" class="account" data-testid="account-panel">
    <p class="hint">
      已登录：
      <strong data-testid="account-profile">{{ profile.username }}（{{ profile.displayName }}）</strong>
      <button type="button" class="link" data-testid="account-fold-toggle" @click="folded = !folded">
        {{ folded ? '管理账号' : '收起' }}
      </button>
    </p>

    <div v-if="!folded" class="row">
      <input v-model="renameTo" data-testid="account-rename-input" placeholder="新的玩家名" spellcheck="false" />
      <button type="button" data-testid="account-rename" :disabled="busy" @click="submitRename()">
        改玩家名
      </button>
      <button type="button" data-testid="account-logout" :disabled="busy" @click="session.logout()">
        登出
      </button>
    </div>

    <p v-if="notice.length > 0" class="notice" data-testid="account-notice">{{ notice }}</p>
    <p v-if="recoveryCode.length > 0" class="recovery" data-testid="account-recovery-code">
      一次性恢复码（只显示这一次，请抄下）：<code>{{ recoveryCode }}</code>
      <button type="button" class="link" data-testid="account-recovery-confirm" @click="session.dismissRecoveryCode()">
        我已抄下
      </button>
    </p>
  </section>
</template>

<style scoped>
.account {
  display: grid;
  gap: 6px;
}

.row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.link {
  background: none;
  border: none;
  color: var(--accent);
  padding: 0;
  text-decoration: underline;
}

.notice {
  color: var(--danger, #b3261e);
}

.recovery code {
  user-select: all;
}
</style>
