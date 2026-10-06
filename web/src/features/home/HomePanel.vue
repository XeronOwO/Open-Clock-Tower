<script setup lang="ts">
/**
 * 首页：这是什么、从哪进去，以及**现在的我是谁、我刚才在哪**（M1 / D-0029）。
 *
 * 它解决的问题是"打开站点直接怼一个登录框"——访客不知道这是干什么的、也不知道该点哪里。
 * 这里刻意**不做登录、也不列桌**：登录在各面自己的门上（`AccountGate`），
 * 桌列表在"加入一桌"那一面（未登录看别人的桌没有意义——坐下要先登录，D-0027）。
 *
 * M1 之后它多做一件事：**认出回来的人**。登录态由 `sessionStorage` 里的凭据恢复，
 * 恢复成功就在顶部显示身份，并给一张"回到我那一桌"的直达卡——
 * 刷新回来的人要的是"继续刚才那一局"，不是重新逛一遍大厅。
 *
 * **不放"回到这一页"这类自指链接**（需求方 2026-10-06 当面问"回到这一页是何意味"）：
 * 它指向的就是当前这一页，点了什么也不会发生；要回首页走顶栏的「首页」。
 */
import { computed } from 'vue'
import { PLAY_LINK, STORYTELLER_LINK } from '@/display/navigation'
import * as session from '@/services/accountSession'

const profile = session.profile
const activeTable = session.activeTable
const busy = session.busy
const notice = session.notice

/** "回到我那一桌"落到哪一面：位置记的是哪一面就回哪一面。 */
const backHref = computed(() =>
  activeTable.value?.surface === 'storyteller' ? STORYTELLER_LINK : PLAY_LINK,
)

/** 那一桌是哪一桌：位置里只有桌标识（要显示桌名得另查大厅，而首页刻意不列桌）。 */
const backHint = computed(() => {
  const table = activeTable.value
  if (table === null) {
    return ''
  }

  return table.surface === 'storyteller'
    ? `继续主持「${table.gameId}」这一桌`
    : `回到「${table.gameId}」的 ${table.seat ?? 0} 号席位`
})
</script>

<template>
  <div class="home">
    <!-- 认出回来的人（M1）：恢复成功后才有这一块；恢复中不显示——还没确认的事不能先说出口。 -->
    <section v-if="profile !== null" class="panel me" data-testid="home-identity">
      <p class="hint">
        已登录：<strong data-testid="home-profile">{{ profile.username }}（{{ profile.displayName }}）</strong>
        <button type="button" class="link" data-testid="home-logout" :disabled="busy" @click="session.logout()">
          登出
        </button>
      </p>
      <a v-if="activeTable !== null" class="entry back" :href="backHref" data-testid="home-back-to-table">
        <strong>回到我那一桌</strong>
        <span class="hint">{{ backHint }}</span>
      </a>
      <p v-if="notice.length > 0" class="notice" data-testid="home-notice">{{ notice }}</p>
    </section>

    <section class="panel intro">
      <h1>血染钟楼 · 线上平台</h1>
      <p>
        说书人主持一局，每名玩家用自己的设备入座；状态以服务端为准，掉线自动重连。
        平台负责<strong>记录 + 校验 + 推演</strong>，说书人保留全部自由裁量权。
      </p>
      <div class="entries">
        <a class="entry" :href="PLAY_LINK" data-testid="home-to-player">
          <strong>加入一桌</strong>
          <span class="hint">登录后，从在开的桌里挑一个空席位坐下</span>
        </a>
        <a class="entry" :href="STORYTELLER_LINK" data-testid="home-to-storyteller">
          <strong>主持一局</strong>
          <span class="hint">登录后开一桌，你就是这一桌的说书人</span>
        </a>
      </div>
      <p class="hint">
        账号是唯一的身份证：同一个账号可以在每一桌各坐一席；换台设备、清掉缓存，登录回来桌还在。
      </p>
    </section>
  </div>
</template>

<style scoped>
.home {
  display: grid;
  gap: 16px;
  max-width: 860px;
  margin: 0 auto;
  padding: 16px;
}

.me {
  display: grid;
  gap: 8px;
}

.intro h1 {
  margin: 0 0 8px;
  font-size: 22px;
}

.entries {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin: 12px 0;
}

.entry {
  flex: 1 1 240px;
  display: grid;
  gap: 4px;
  padding: 12px;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: var(--paper-raised);
  color: inherit;
  text-decoration: none;
}

.entry:hover {
  border-color: var(--accent);
}

/* "回到我那一桌"是回来的人的第一件事：给它一条强调边，别和下面的入口卡混成一样。 */
.back {
  border-color: var(--accent);
  background: var(--accent-soft);
}

.link {
  background: none;
  border: none;
  color: var(--accent);
  padding: 0 0 0 8px;
  text-decoration: underline;
}

.notice {
  color: var(--danger, #b3261e);
}
</style>
