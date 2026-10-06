<script setup lang="ts">
/**
 * 账号门（D-0027）：**没登录时页面上只有这一张卡**。
 *
 * 它解决的问题是"没有个像样的注册登录界面逻辑，全是到处硬塞"：此前账号表单被复制到两个面，
 * 又和桌列表、票据输入框、开桌表单平铺在同一张卡里，用户看不出该先做哪一步。
 * 现在门就是门——一张卡、两个页签（登录 / 注册）、一个"忘记口令"折叠，其余什么都不放。
 *
 * 登录与注册是**同一件事的两半**（注册成功即登录），所以放在同一张卡上而不是两个入口。
 * 恢复码只显示一次：抄下即清（服务端只存哈希）。
 */
import { ref } from 'vue'
import * as session from '@/services/accountSession'

/** 模块级响应式状态的本地别名（模板里直接当 ref 用，不必写 `.value`）。 */
const busy = session.busy
const notice = session.notice

/**
 * 正在确认持久化凭据（M1 / D-0029）。
 *
 * 这一态**不能和"未登录"合并**：否则每次刷新都会先闪一张"先登录"，再跳回原来的面；
 * 而"要不要重新登录"必须等 `Resume` 的答复，本地猜不得。
 */
const restoring = session.restoring

/** 当前页签：登录 / 注册。默认登录——已经有账号的人比新来的人多。 */
const tab = ref<'login' | 'register'>('login')
const username = ref('')
const displayName = ref('')
const password = ref('')
const resetVisible = ref(false)
const resetUsername = ref('')
const resetCode = ref('')
const resetPasswordValue = ref('')

function switchTab(next: 'login' | 'register'): void {
  tab.value = next
  session.clearNotice()
}

async function submitRegister(): Promise<void> {
  if (await session.register(username.value.trim(), displayName.value.trim(), password.value)) {
    password.value = ''
  }
}

async function submitLogin(): Promise<void> {
  if (await session.login(username.value.trim(), password.value)) {
    password.value = ''
  }
}

async function submitReset(): Promise<void> {
  const ok = await session.resetPassword(
    resetUsername.value.trim(),
    resetCode.value.trim(),
    resetPasswordValue.value,
  )
  if (ok) {
    resetVisible.value = false
    resetCode.value = ''
    resetPasswordValue.value = ''
  }
}
</script>

<template>
  <!-- 恢复登录状态期间**不画登录卡**：先闪一张"先登录"再跳回原来的面，正是 M1 要消掉的那种跳变。 -->
  <section v-if="restoring" class="panel gate" data-testid="account-restoring">
    <h1>正在恢复登录状态…</h1>
    <p class="hint">正在向服务端确认这台设备上保存的登录凭据。</p>
  </section>

  <section v-else class="panel gate" data-testid="account-gate">
    <h1>先登录</h1>
    <p class="hint">
      账号就是你的身份证：换台设备、清掉缓存，登录同一个账号就还认得你。
    </p>

    <div class="tabs" role="tablist">
      <button
        type="button"
        role="tab"
        data-testid="account-tab-login"
        :class="{ current: tab === 'login' }"
        :aria-selected="tab === 'login'"
        @click="switchTab('login')"
      >
        登录
      </button>
      <button
        type="button"
        role="tab"
        data-testid="account-tab-register"
        :class="{ current: tab === 'register' }"
        :aria-selected="tab === 'register'"
        @click="switchTab('register')"
      >
        注册
      </button>
    </div>

    <template v-if="tab === 'register'">
      <p class="hint">第一次来？起个登录名就行——玩家名是同桌人看到的名字，随时能改。</p>
      <label class="field">
        <span>登录名</span>
        <input v-model="username" data-testid="account-username" placeholder="登录名" spellcheck="false" />
      </label>
      <label class="field">
        <span>玩家名</span>
        <input
          v-model="displayName"
          data-testid="account-display-name"
          placeholder="玩家名（大家看到的名字）"
          spellcheck="false"
        />
      </label>
      <label class="field">
        <span>口令</span>
        <input
          v-model="password"
          type="password"
          data-testid="account-password"
          placeholder="口令（至少 8 位）"
          @keyup.enter="submitRegister()"
        />
      </label>
      <button type="button" class="primary" :disabled="busy" data-testid="account-register" @click="submitRegister()">
        注册并登录
      </button>
    </template>

    <template v-else>
      <label class="field">
        <span>登录名</span>
        <input v-model="username" data-testid="account-username" placeholder="登录名" spellcheck="false" />
      </label>
      <label class="field">
        <span>口令</span>
        <input
          v-model="password"
          type="password"
          data-testid="account-password"
          placeholder="口令"
          @keyup.enter="submitLogin()"
        />
      </label>
      <button type="button" class="primary" :disabled="busy" data-testid="account-login" @click="submitLogin()">
        登录
      </button>
    </template>

    <p class="hint">
      <button type="button" class="link" data-testid="account-reset-toggle" @click="resetVisible = !resetVisible">
        忘记口令
      </button>
    </p>

    <div v-if="resetVisible" class="reset" data-testid="account-reset">
      <p class="hint">用注册时抄下的一次性恢复码换一个新口令；换完旧登录全部失效。</p>
      <input v-model="resetUsername" data-testid="account-reset-username" placeholder="登录名" spellcheck="false" />
      <input v-model="resetCode" data-testid="account-reset-code" placeholder="一次性恢复码" spellcheck="false" />
      <input
        v-model="resetPasswordValue"
        type="password"
        data-testid="account-reset-password"
        placeholder="新口令（至少 8 位）"
      />
      <button type="button" :disabled="busy" data-testid="account-reset-submit" @click="submitReset()">
        重置口令
      </button>
    </div>

    <p v-if="notice.length > 0" class="notice" data-testid="account-notice">
      {{ notice }}
    </p>
  </section>
</template>

<style scoped>
.gate {
  max-width: 420px;
  margin: 24px auto;
  display: grid;
  gap: 10px;
}

.tabs {
  display: flex;
  gap: 6px;
}

.tabs button {
  flex: 1;
  padding: 6px 10px;
}

.tabs button.current {
  background: var(--accent-soft);
  border-color: var(--accent);
  font-weight: 600;
}

.field {
  display: grid;
  gap: 4px;
}

.field span {
  color: var(--ink-soft);
  font-size: 12px;
}

.reset {
  display: grid;
  gap: 6px;
  padding: 8px;
  border: 1px solid var(--line);
  border-radius: 8px;
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
</style>
