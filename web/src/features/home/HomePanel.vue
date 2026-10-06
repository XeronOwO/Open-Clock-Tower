<script setup lang="ts">
/**
 * 首页：这是什么、从哪进去。
 *
 * 它解决的问题是"打开站点直接怼一个登录框"——访客不知道这是干什么的、也不知道该点哪里。
 * 这里刻意**不做登录、也不列桌**：登录在各面自己的门上（`AccountGate`），
 * 桌列表在"加入一桌"那一面（未登录看别人的桌没有意义——坐下要先登录，D-0027）。
 * 首页只做一件事：说清楚这是什么，然后指两个方向。
 *
 * **不放"回到这一页"这类自指链接**（需求方 2026-10-06 当面问"回到这一页是何意味"）：
 * 它指向的就是当前这一页，点了什么也不会发生；要回首页走顶栏的「首页」。
 */
import { PLAY_LINK, STORYTELLER_LINK } from '@/display/navigation'
</script>

<template>
  <div class="home">
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
</style>
