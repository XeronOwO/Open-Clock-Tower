<script setup lang="ts">
/**
 * 角色图（运行期热链百科图片）：加载失败即撤下 `<img>`，由外层的角色名 + 阵营色环兜底。
 *
 * 只负责「图能不能显示」这一个呈现态；图片地址由 `display/character-art` 决定（纯映射）。
 * `referrerpolicy="no-referrer"`：不向第三方站点泄露本机 / 局域网地址（R-0006 实测两种方式均可加载）。
 * 席位换手（角色 slug 变化）时重置失败态，给新角色一次重新加载的机会。
 */
import { computed, ref, watch } from 'vue'
import { characterArtUrlOf } from '@/display/character-art'

const props = defineProps<{
  character: string | null
  /** 无障碍 / 悬停文案（与牌面角色文字一致）。 */
  label: string
}>()

const failed = ref(false)
const url = computed(() => characterArtUrlOf(props.character))

watch(url, () => {
  failed.value = false
})
</script>

<template>
  <img
    v-if="url !== null && !failed"
    class="character-art"
    :src="url"
    :alt="label"
    :title="label"
    loading="lazy"
    decoding="async"
    referrerpolicy="no-referrer"
    data-testid="character-art"
    @error="failed = true"
  />
</template>

<style scoped>
.character-art {
  width: 42px;
  height: 42px;
  border-radius: 50%;
  object-fit: cover;
  background: var(--paper);
}
</style>
