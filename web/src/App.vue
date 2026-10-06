<script setup lang="ts">
/**
 * 单 SPA 多面（D-0004 / D-0018）：首页、玩家端、说书人端是同一份构建的三个面，
 * 由**地址路径**决定（解析规则见 `display/navigation.ts`，地址怎么变见 `display/routing.ts`）。
 *
 * 这里同时负责**导航**：此前各面互相孤立——想换一面只能手改地址栏，也没有首页。
 * 顶栏把这三个面互相连起来，**空地址（根路径）就是首页**。
 *
 * 点顶栏是**拦截 + `pushState`**，不是整页跳转：整页跳转要把面板状态整个重来一遍，
 * "换个面还得再登一次"最早就是被这么修掉的；M1 / D-0029 之后刷新不会再掉登录，
 * 但无谓的重载仍然会丢掉面板里的呈现态（滚动、展开、回执），所以这条路保持拦截。
 *
 * 旧的 `#player` 这类井号地址仍进得去：`main.ts` 在挂载前把它们就地改写成新路径。
 */
import StorytellerPanel from '@/features/storyteller/StorytellerPanel.vue'
import PlayerPanel from '@/features/player/PlayerPanel.vue'
import HomePanel from '@/features/home/HomePanel.vue'
import { HOME_LINK, PLAY_LINK, routeFromPath, routeLabel, type AppRoute, STORYTELLER_LINK } from '@/display/navigation'
import { interceptLinkClick, subscribeRoute } from '@/display/routing'
import { onBeforeUnmount, onMounted, ref } from 'vue'

// 首帧就按**当前地址**画，而不是先画一个默认面再被订阅纠正过来——否则打开首页时会闪一下说书人面。
const route = ref<AppRoute>(routeFromPath(window.location.pathname))
let unsubscribe: (() => void) | null = null

/** 顶栏链接：当前面高亮，其余可点。文案照界面口径（D-0027）：加入一桌 / 主持一局。 */
const links = [
  { label: '首页', href: HOME_LINK, route: 'home' as const, testId: 'nav-home' },
  { label: '加入一桌', href: PLAY_LINK, route: 'player' as const, testId: 'nav-player' },
  { label: '主持一局', href: STORYTELLER_LINK, route: 'storyteller' as const, testId: 'nav-storyteller' },
]

/** 站内链接一律走这里：接管成功就不重载文档；中键 / 修饰键点击原样交给浏览器。 */
function onClick(event: MouseEvent): void {
  interceptLinkClick(event)
}

onMounted(() => {
  unsubscribe = subscribeRoute((next) => {
    route.value = next
  })
})

onBeforeUnmount(() => {
  unsubscribe?.()
  unsubscribe = null
})
</script>

<template>
  <!-- 点击监听挂在根节点上（不是只挂在顶栏）：首页的入口卡片也是站内链接，
       漏掉它们就会在点「加入一桌」时整页重载——那正是这一轮要消掉的"换个面还要再登一次"。 -->
  <div class="app" @click="onClick">
    <!-- 顶栏：各面之间的跳转入口（此前没有，只能手改地址栏）。 -->
    <nav class="topnav" data-testid="top-nav">
      <span class="brand">OpenClockTower</span>
      <a
        v-for="link in links"
        :key="link.href"
        class="navlink"
        :class="{ current: route === link.route }"
        :href="link.href"
        :data-testid="link.testId"
        :aria-current="route === link.route ? 'page' : undefined"
      >
        {{ link.label }}
      </a>
      <span class="hint here" data-testid="nav-current">当前位置：{{ routeLabel(route) }}</span>
    </nav>

    <HomePanel v-if="route === 'home'" />
    <PlayerPanel v-else-if="route === 'player'" />
    <StorytellerPanel v-else />
  </div>
</template>

<style scoped>
.app {
  min-height: 100vh;
}

.topnav {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 16px;
  border-bottom: 1px solid var(--line);
  background: var(--paper-raised);
  flex-wrap: wrap;
}

.brand {
  font-weight: 600;
  letter-spacing: 0.02em;
}

.navlink {
  color: var(--ink-soft);
  text-decoration: none;
  padding: 2px 6px;
  border-radius: 6px;
}

.navlink:hover {
  color: var(--accent);
}

.navlink.current {
  color: var(--accent);
  background: var(--accent-soft);
  font-weight: 600;
}

.here {
  margin-left: auto;
}
</style>
