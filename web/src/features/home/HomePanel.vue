<script setup lang="ts">
/**
 * 首页：这是一个什么样的项目、从哪进去。
 *
 * 它解决的问题是"打开根路径直接怼一个说书人登录框"——访客不知道这是干什么的、
 * 也不知道该点哪里，而且各面之间没有跳转关系（原状态：只有 URL 里的一个 hash 开关）。
 *
 * 这里刻意**不做登录**：账号与入座都在各自的页面里（玩家端），首页只负责"说清楚 + 指路"，
 * 避免同一件事有两处实现。
 */
import { AccountGateway, type LobbyTable } from '@/services/accountGateway'
import { HOME_LINK, PLAY_LINK, STORYTELLER_LINK } from '@/display/navigation'
import { onBeforeUnmount, onMounted, ref } from 'vue'

/** 在开的桌（公开信息；未登录也能看）。 */
const tables = ref<LobbyTable[]>([])
const busy = ref(false)
const notice = ref('')
let gateway: AccountGateway | null = null

async function refresh(): Promise<void> {
  busy.value = true
  try {
    gateway ??= new AccountGateway()
    tables.value = await gateway.listTables()
    notice.value = ''
  } catch (error) {
    notice.value = `读取桌列表失败：${error instanceof Error ? error.message : String(error)}`
  } finally {
    busy.value = false
  }
}

onMounted(() => {
  void refresh()
})

onBeforeUnmount(() => {
  void gateway?.stop()
})
</script>

<template>
  <div class="home">
    <section class="panel intro">
      <h1>血染钟楼 · 线上平台</h1>
      <p>
        说书人端 + 每名玩家各自的设备端，服务端权威状态，断线自动重连。
        平台负责<strong>记录 + 校验 + 推演</strong>，说书人保留全部自由裁量权。
      </p>
      <div class="entries">
        <a class="entry" :href="PLAY_LINK" data-testid="home-to-player">
          <strong>我是玩家</strong>
          <span class="hint">注册 / 登录后，从桌列表里挑一个空席位坐下</span>
        </a>
        <a class="entry" :href="STORYTELLER_LINK" data-testid="home-to-storyteller">
          <strong>我是说书人</strong>
          <span class="hint">用说书人票据进入主持台</span>
        </a>
      </div>
      <p class="hint">
        账号是跨桌的：同一个账号可以在每一桌各坐一席，互不影响。席位票据仍然可用
        （邀请朋友 / 换设备兜底）。
      </p>
    </section>

    <section class="panel">
      <div class="row">
        <h2>在开的桌</h2>
        <button type="button" :disabled="busy" @click="refresh()">刷新</button>
        <span class="hint">共 {{ tables.length }} 桌</span>
      </div>
      <p v-if="notice.length > 0" class="hint">{{ notice }}</p>
      <ul v-if="tables.length > 0" class="tables">
        <li v-for="table in tables" :key="table.gameId">
          <strong>{{ table.name.length > 0 ? table.name : table.gameId }}</strong>
          <span class="hint">
            {{ table.takenSeatCount }} / {{ table.seatCapacity }} 人 ·
            {{ table.started ? '已开局' : '等人' }} · {{ table.locked ? '已锁定' : '可入座' }}
          </span>
        </li>
      </ul>
      <p v-else class="hint">
        还没有开桌。进「我是说书人」登录后可以自己开一桌（开完你就是这一桌的说书人），
        也可以直接用说书人票据进入主持台。
      </p>
      <p class="hint">
        要坐下请去 <a :href="PLAY_LINK">玩家端</a>；从那里可以直接选席位。
      </p>
    </section>

    <p class="hint foot"><a :href="HOME_LINK">首页</a> · 本页不登录，登录在各端进行。</p>
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

.tables {
  display: grid;
  gap: 8px;
  margin: 8px 0;
  padding-left: 18px;
}

.foot {
  text-align: center;
}
</style>
