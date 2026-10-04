<script setup lang="ts">
/**
 * 「?」说明圆圈（票据 `ui-layout-and-onboarding` 矩阵行 2）。
 *
 * - 悬停（鼠标）显示、移开隐藏；点按（触屏）打开、键盘（Tab + Enter / Space）也能打开；
 *   `Esc`、失焦或点组件外面关闭。
 * - 文案只从 `display/help.ts` 的登记表取：组件里不散写说明，两端口径一致（矩阵行 7）。
 * - 不承载规则：它只解释界面上已经出现的词与数（`help.ts` 头部注释）。
 */
import type { HelpTopicId } from '@/display/help'
import { HELP_TOPICS } from '@/display/help'
import { computed, onBeforeUnmount, ref, watch } from 'vue'

const props = defineProps<{ topic: HelpTopicId }>()

const entry = computed(() => HELP_TOPICS[props.topic])
const open = ref(false)
const root = ref<HTMLElement | null>(null)

function show(): void {
  open.value = true
}

function hide(): void {
  open.value = false
}

/**
 * 点按 / 点击：**只开不关**。触屏上浏览器会先补一个 mouseenter 再发 click，
 * 用 toggle 会在同一次点按里一开一关；关闭交给移开鼠标、失焦、Esc 或点组件外面。
 */
function openBubble(): void {
  open.value = true
}

function onDocumentPointerDown(event: PointerEvent): void {
  if (root.value !== null && !root.value.contains(event.target as Node)) {
    hide()
  }
}

watch(open, (isOpen) => {
  if (isOpen) {
    document.addEventListener('pointerdown', onDocumentPointerDown)
  } else {
    document.removeEventListener('pointerdown', onDocumentPointerDown)
  }
})

onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocumentPointerDown)
})
</script>

<template>
  <span
    ref="root"
    class="help"
    @mouseenter="show"
    @mouseleave="hide"
  >
    <button
      type="button"
      class="help-button"
      :aria-label="`说明：${entry.title}`"
      :aria-expanded="open ? 'true' : 'false'"
      @click="openBubble()"
      @blur="hide"
      @keydown.esc="hide"
    >
      ?
    </button>
    <span v-if="open" class="help-bubble" role="tooltip">
      <strong>{{ entry.title }}</strong>
      <span class="help-text">{{ entry.text }}</span>
    </span>
  </span>
</template>

<style scoped>
.help {
  position: relative;
  display: inline-block;
  vertical-align: middle;
  margin-left: 4px;
}

.help-button {
  width: 16px;
  height: 16px;
  padding: 0;
  border-radius: 50%;
  border: 1px solid var(--line);
  background: var(--paper);
  color: var(--ink-soft);
  font-size: 11px;
  line-height: 14px;
  text-align: center;
  cursor: help;
}

.help-button:hover,
.help-button:focus-visible {
  border-color: var(--accent);
  color: var(--accent);
}

.help-bubble {
  position: absolute;
  left: 0;
  top: calc(100% + 4px);
  z-index: 20;
  width: max-content;
  max-width: 280px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 6px 8px;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: var(--paper-raised);
  box-shadow: 0 4px 14px rgb(0 0 0 / 16%);
  font-size: 12px;
  line-height: 1.45;
  color: var(--ink);
  text-align: left;
  white-space: normal;
}

.help-text {
  color: var(--ink-soft);
}
</style>
