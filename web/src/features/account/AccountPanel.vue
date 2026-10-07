<script setup lang="ts">
/**
 * 已登录的账号区（D-0021 / D-0027）：一行摘要 + 改名 / 登出 / 注销，收起来才不占地方。
 *
 * 它**只管已登录之后的事**：注册 / 登录 / 找回口令都在门上（`AccountGate.vue`）。
 * 拆开的理由正是需求方那句"到处硬塞"——此前这一张卡同时是注册表单、登录表单、
 * 找回表单、资料区与桌列表的邻居，用户看不出该先做哪一步。
 *
 * 秘密纪律不变：账号会话只活在 `accountSession` 的网关内存里，这里不落盘、不渲染凭据；
 * 一次性恢复码只给本人看一次，抄下即清。
 *
 * 注销（M5 / G-A1-6）刻意做成**两步**：先点开、再输口令确认。它是全站唯一不可逆的自助动作，
 * 所以界面上要把"会删掉什么、会留下什么"先说清楚，再让人动手。
 */
import { ref } from 'vue'
import * as session from '@/services/accountSession'

const profile = session.profile
const busy = session.busy
const notice = session.notice
const recoveryCode = session.recoveryCode

const folded = ref(true)
const renameTo = ref('')
const deleting = ref(false)
const deletePassword = ref('')

async function submitRename(): Promise<void> {
  if (renameTo.value.trim().length === 0) {
    return
  }

  if (await session.rename(renameTo.value.trim())) {
    renameTo.value = ''
  }
}

async function submitDelete(): Promise<void> {
  if (deletePassword.value.length === 0) {
    return
  }

  if (await session.deleteAccount(deletePassword.value)) {
    deletePassword.value = ''
    deleting.value = false
  }
}

function cancelDelete(): void {
  deleting.value = false
  deletePassword.value = ''
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
      <button
        type="button"
        class="link danger"
        data-testid="account-delete-open"
        :disabled="busy"
        @click="deleting = !deleting"
      >
        注销账号
      </button>
    </div>

    <div v-if="!folded && deleting" class="danger-box" data-testid="account-delete-confirm-box">
      <p class="hint">
        注销<strong>不可撤销</strong>。会删掉：登录名、玩家名、口令与恢复码、你在所有桌的席位认领。
        你开的桌会留下（不再有人能进它的主持台）；对局记录里本来就不含玩家名。
      </p>
      <div class="row">
        <input
          v-model="deletePassword"
          type="password"
          data-testid="account-delete-password"
          placeholder="输入口令确认"
          autocomplete="current-password"
        />
        <button
          type="button"
          class="danger"
          data-testid="account-delete"
          :disabled="busy || deletePassword.length === 0"
          @click="submitDelete()"
        >
          确认注销
        </button>
        <button type="button" class="link" data-testid="account-delete-cancel" @click="cancelDelete()">
          取消
        </button>
      </div>
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

.danger {
  color: var(--danger, #b3261e);
}

.danger-box {
  border: 1px solid var(--danger, #b3261e);
  border-radius: 6px;
  display: grid;
  gap: 6px;
  padding: 8px;
}

.recovery code {
  user-select: all;
}
</style>
