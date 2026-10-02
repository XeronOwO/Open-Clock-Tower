<script setup lang="ts">
/**
 * 数据与审计下钻：默认收拢，展开后是原有表格面板与当前步骤摘要。
 *
 * 它们是同一份视图的另一条呈现路径（矩阵行 6 的"同源"），不是第二份数据；
 * 收拢只是布局选择，展开/收起是本地呈现态。
 */
import { ref } from 'vue'

const open = ref(false)
</script>

<template>
  <div class="panel drawer">
    <button
      type="button"
      class="toggle"
      data-testid="data-drawer-toggle"
      :aria-expanded="open ? 'true' : 'false'"
      @click="open = !open"
    >
      <span>数据与审计（当前步骤 / 状态账 / 最近变化 / 效果链 / 两本账）</span>
      <span class="hint">{{ open ? '收起' : '展开' }}</span>
    </button>
    <div v-show="open" class="drawer-body" data-testid="data-drawer-body">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.drawer {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.toggle {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  width: 100%;
  cursor: pointer;
  text-align: left;
}

.drawer-body {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
</style>
