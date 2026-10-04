<script setup lang="ts">
/**
 * 账号面板（D-0021）：注册 / 登录 / 登出 / 改玩家名 / 恢复码重置。
 *
 * 纯呈现 + 本地表单态：秘密（账号会话）只由父级 `AccountGateway` 在内存里持有，
 * 这里不落盘、不渲染凭据；一次性恢复码只给本人看一次。
 */
import type { AccountProfile } from '@/services/accountGateway'
import { ref } from 'vue'

const props = defineProps<{
  /** 已登录资料；null = 未登录。 */
  profile: AccountProfile | null
  /** 请求进行中：按钮禁用。 */
  busy: boolean
  /** 中性提示（成功 / 失败 / 校验）；空串 = 不显示。 */
  notice: string
  /** 一次性恢复码明文（注册 / 重置后显示）；空串 = 不显示。 */
  recoveryCode: string
  /** 紧凑模式：已连接后的账号设置区。 */
  compact?: boolean
}>()

const emit = defineEmits<{
  register: [username: string, displayName: string, password: string]
  login: [username: string, password: string]
  logout: []
  rename: [displayName: string]
  reset: [username: string, recoveryCode: string, newPassword: string]
}>()

const username = ref('')
const displayName = ref('')
const password = ref('')
const renameTo = ref('')
const resetVisible = ref(false)
const resetUsername = ref('')
const resetCode = ref('')
const resetPassword = ref('')

function submitRegister(): void {
  emit('register', username.value.trim(), displayName.value.trim(), password.value)
}

function submitLogin(): void {
  emit('login', username.value.trim(), password.value)
}

function submitReset(): void {
  emit('reset', resetUsername.value.trim(), resetCode.value.trim(), resetPassword.value)
}

function submitRename(): void {
  emit('rename', renameTo.value.trim())
}
</script>

<template>
  <section class="account" data-testid="account-panel">
    <template v-if="props.profile === null">
      <h2 v-if="!props.compact">账号（可选）</h2>
      <p v-if="!props.compact" class="hint">
        登录后凭席位票据认领，同桌就能看到你的玩家名；不登录也能以游客身份加入（只显示席位号）。
      </p>
      <div class="fields">
        <input
          v-model="username"
          data-testid="account-username"
          placeholder="登录名"
          spellcheck="false"
          autocomplete="username"
        />
        <input
          v-model="displayName"
          data-testid="account-display-name"
          placeholder="玩家名（注册用）"
          spellcheck="false"
        />
        <input
          v-model="password"
          data-testid="account-password"
          type="password"
          placeholder="口令（至少 8 位）"
          autocomplete="current-password"
        />
      </div>
      <div class="row">
        <button type="button" data-testid="account-register" :disabled="props.busy" @click="submitRegister()">
          注册并登录
        </button>
        <button type="button" data-testid="account-login" :disabled="props.busy" @click="submitLogin()">
          登录
        </button>
        <button type="button" data-testid="account-reset-toggle" @click="resetVisible = !resetVisible">
          用恢复码重置口令
        </button>
      </div>
      <div v-if="resetVisible" class="reset" data-testid="account-reset">
        <input v-model="resetUsername" data-testid="account-reset-username" placeholder="登录名" spellcheck="false" />
        <input v-model="resetCode" data-testid="account-reset-code" placeholder="一次性恢复码" spellcheck="false" />
        <input
          v-model="resetPassword"
          data-testid="account-reset-password"
          type="password"
          placeholder="新口令（至少 8 位）"
        />
        <button type="button" data-testid="account-reset-submit" :disabled="props.busy" @click="submitReset()">
          重置并登录
        </button>
      </div>
    </template>

    <template v-else>
      <p class="hint">
        已登录：
        <strong data-testid="account-profile">{{ props.profile.username }}（{{ props.profile.displayName }}）</strong>
      </p>
      <div class="row">
        <input v-model="renameTo" data-testid="account-rename-input" placeholder="新的玩家名" spellcheck="false" />
        <button type="button" data-testid="account-rename" :disabled="props.busy" @click="submitRename()">
          改玩家名
        </button>
        <button type="button" data-testid="account-logout" :disabled="props.busy" @click="emit('logout')">
          登出
        </button>
      </div>
    </template>

    <p v-if="props.notice.length > 0" class="notice" data-testid="account-notice">{{ props.notice }}</p>
    <p v-if="props.recoveryCode.length > 0" class="recovery" data-testid="account-recovery-code">
      一次性恢复码（只显示这一次，请抄下）：<code>{{ props.recoveryCode }}</code>
    </p>
  </section>
</template>

<style scoped>
.account {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.fields,
.reset {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
}

.hint {
  margin: 0;
  color: #666;
  font-size: 13px;
}

.notice {
  margin: 0;
  color: #8a4b08;
  font-size: 13px;
}

.recovery code {
  user-select: all;
}
</style>
