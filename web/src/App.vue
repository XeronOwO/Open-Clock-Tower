<script setup lang="ts">
/**
 * 单 SPA 两套视图（D-0004）：说书人端与玩家端是同一份构建的两个入口视图。
 * 入口由 URL hash 决定：#player 进玩家视图，其余进说书人视图。
 * 两者从不共享视图数据——玩家视图里没有、也不该有说书人专属字段。
 */
import StorytellerPanel from '@/features/storyteller/StorytellerPanel.vue'
import PlayerPanel from '@/features/player/PlayerPanel.vue'
import { computed, ref } from 'vue'

const hash = ref(typeof window === 'undefined' ? '' : window.location.hash)
const isPlayer = computed(() => hash.value.toLowerCase().includes('player'))
</script>

<template>
  <PlayerPanel v-if="isPlayer" />
  <StorytellerPanel v-else />
</template>
