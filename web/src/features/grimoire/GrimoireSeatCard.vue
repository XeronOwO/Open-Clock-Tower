<script setup lang="ts">
/**
 * 一个席位牌：角色 / 阵营色环 / 生死（帷幕）/ 状态与提示标记。
 *
 * 只呈现 `buildSeatCard` 派生好的模型——不读原始视图、不做任何领域判断；
 * 点击只更新"选中态"（呈现态），不发命令、不改任何游戏数据。
 */
import type { SeatCardModel } from '@/display/grimoire'
import {
  ALIGNMENT_EVIL,
  ALIGNMENT_GOOD,
  LIFE_ALIVE,
  LIFE_DEAD,
  seatTitleOf,
} from '@/display/grimoire'
import { annotationTokenTextOf } from '@/display/format'
import { characterNameOf, characterTypeOf, labelOf } from '@/display/labels'

const props = defineProps<{
  model: SeatCardModel
  position: { xPercent: number; yPercent: number } | null
  selected: boolean
  isCurrentSlot: boolean
  hasPending: boolean
  hasDecision: boolean
}>()

const emit = defineEmits<{ select: [] }>()

function styleOf(): Record<string, string> {
  if (props.position === null) {
    return {}
  }

  return {
    '--seat-x': `${props.position.xPercent}%`,
    '--seat-y': `${props.position.yPercent}%`,
  }
}

/** 阵营色环：阵营是独立维度，未观测时保持中性灰——不拿角色类型冒充阵营。 */
function alignmentClass(): string {
  if (props.model.alignment === ALIGNMENT_GOOD) {
    return 'alignment-good'
  }

  if (props.model.alignment === ALIGNMENT_EVIL) {
    return 'alignment-evil'
  }

  return 'alignment-unknown'
}

function alignmentText(): string {
  return props.model.alignment === null ? '阵营未观测' : labelOf(props.model.alignment)
}

function lifeText(): string {
  if (props.model.life === LIFE_ALIVE) {
    return '存活'
  }

  if (props.model.life === LIFE_DEAD) {
    return '死亡'
  }

  if (props.model.life === null) {
    return '生死未观测'
  }

  return labelOf(props.model.life)
}

const characterText = () =>
  props.model.character === null ? '角色未观测' : characterNameOf(props.model.character)

/** 类型文案三态：未观测 / 已知类型 / 角色已知但类型未知（未知取值原样回显，不吞）。 */
function typeText(): string {
  if (props.model.character === null) {
    return '类型未观测'
  }

  const type = characterTypeOf(props.model.character)
  return type.length > 0 ? type : `类型未知：${props.model.character}`
}
</script>

<template>
  <button
    type="button"
    class="seat"
    :class="[
      alignmentClass(),
      {
        'is-selected': selected,
        'is-current': isCurrentSlot,
        'is-pending': hasPending,
        'is-decision': hasDecision,
        'is-dead': model.life === LIFE_DEAD,
      },
    ]"
    :style="styleOf()"
    :aria-pressed="selected"
    :aria-label="seatTitleOf(model)"
    :title="seatTitleOf(model)"
    data-testid="grimoire-seat"
    :data-seat="model.seat"
    :data-life="model.life ?? 'unknown'"
    :data-character="model.character ?? ''"
    :data-current-slot="isCurrentSlot ? 'true' : 'false'"
    :data-pending="hasPending ? 'true' : 'false'"
    :data-decision="hasDecision ? 'true' : 'false'"
    @click="emit('select')"
  >
    <span class="chips">
      <span v-if="isCurrentSlot" class="chip current">当前槽位</span>
      <span v-if="hasPending" class="chip pending">卡点</span>
      <span v-if="hasDecision" class="chip decision">待裁定</span>
    </span>

    <span class="token" :class="alignmentClass()">
      <span class="role">{{ characterText() }}</span>
      <span class="type">{{ typeText() }}</span>
      <span v-if="model.life === LIFE_DEAD" class="shroud" aria-hidden="true">帷幕</span>
    </span>

    <span class="nameplate">
      <span class="seat-no">{{ model.seat }} 号</span>
      <span class="align">{{ alignmentText() }}</span>
    </span>

    <span class="life">{{ lifeText() }}</span>

    <span v-if="model.marks.length > 0" class="marks">
      <span
        v-for="(mark, index) in model.marks"
        :key="`${mark.kind}-${index}`"
        class="mark"
        :class="`mark-${mark.kind}`"
        :title="mark.detail ?? mark.label"
      >
        {{ mark.label
        }}<template v-if="mark.kind === 'effect' && mark.detail !== null">·{{ mark.detail }}</template>
      </span>
    </span>
    <span v-else class="marks"><span class="mark empty">无标记</span></span>

    <span v-if="model.annotations.length > 0" class="notes" data-testid="seat-notes">
      <span
        v-for="annotation in model.annotations"
        :key="annotation.id"
        class="mark mark-note"
        :data-note-id="annotation.id"
        :title="annotation.text"
      >
        {{ annotationTokenTextOf(annotation.text) }}
      </span>
    </span>
  </button>
</template>

<style scoped>
.seat {
  position: absolute;
  left: var(--seat-x, 50%);
  top: var(--seat-y, 50%);
  transform: translate(-50%, -50%);
  width: 132px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 3px;
  padding: 4px;
  background: transparent;
  border: none;
  border-radius: 12px;
  cursor: pointer;
  color: var(--ink);
  font: inherit;
  text-align: center;
}

.seat:focus-visible {
  outline: 3px solid var(--accent);
  outline-offset: 2px;
}

.chips {
  display: flex;
  gap: 4px;
  min-height: 16px;
  flex-wrap: wrap;
  justify-content: center;
}

.chip {
  font-size: 10px;
  border-radius: 999px;
  padding: 0 6px;
  border: 1px solid var(--line);
  background: var(--paper);
  color: var(--ink-soft);
}

.chip.current {
  border-color: var(--accent);
  background: var(--accent-soft);
  color: var(--accent);
  font-weight: 700;
}

.chip.pending {
  border-color: var(--warn);
  background: #f7ead2;
  color: var(--warn);
  font-weight: 700;
}

.chip.decision {
  border-color: var(--night);
  background: #e2e7f2;
  color: var(--night);
  font-weight: 700;
}

.token {
  position: relative;
  width: 92px;
  height: 92px;
  border-radius: 50%;
  border: 4px solid var(--line);
  background: var(--paper-raised);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 2px;
  overflow: hidden;
  box-shadow: 0 2px 6px rgb(0 0 0 / 12%);
}

.alignment-good .token {
  border-color: var(--good);
}

.alignment-evil .token {
  border-color: var(--evil);
}

.role {
  font-weight: 700;
  font-size: 13px;
  line-height: 1.2;
  padding: 0 6px;
  overflow-wrap: anywhere;
}

.type {
  font-size: 11px;
  color: var(--ink-soft);
}

.shroud {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  font-weight: 700;
  letter-spacing: 3px;
  background: repeating-linear-gradient(
    45deg,
    rgb(24 24 32 / 82%),
    rgb(24 24 32 / 82%) 6px,
    rgb(43 43 56 / 82%) 6px,
    rgb(43 43 56 / 82%) 12px
  );
}

.nameplate {
  display: flex;
  flex-direction: column;
  line-height: 1.25;
}

.seat-no {
  font-weight: 700;
}

.align {
  font-size: 11px;
  color: var(--ink-soft);
}

.life {
  font-size: 11px;
  color: var(--ink-soft);
}

.is-dead .life {
  color: var(--evil);
  font-weight: 700;
}

.marks {
  display: flex;
  flex-wrap: wrap;
  gap: 3px;
  justify-content: center;
  max-width: 132px;
}

.mark {
  font-size: 10px;
  border-radius: 999px;
  padding: 0 5px;
  border: 1px solid var(--line);
  background: var(--paper);
  color: var(--ink-soft);
  /* 牌面只放"是什么"：长的来由 / 效果 id 放 title 与操作台，不让标记把牌面撑爆。 */
  max-width: 122px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.mark-poison {
  border-color: #5b3a8c;
  background: #ece5f7;
  color: #4a2f73;
}

.mark-drunk {
  border-color: var(--warn);
  background: #f7ead2;
  color: var(--warn);
}

.mark-madness {
  border-color: var(--evil);
  background: var(--accent-soft);
  color: var(--evil);
}

.mark-effect {
  border-color: var(--night);
  background: #e2e7f2;
  color: var(--night);
}

/* 失去能力（R-0040）：限次能力用尽的一次性标记，刻意低调（不是当场效果） */
.mark-exhausted {
  border-color: #6f6f6f;
  background: #ececec;
  color: #4a4a4a;
}

/* 说书人注记（D-0019）：自由文本 token，颜色与派生标记区分开 */
.mark-note {
  border-color: #8a6d3b;
  background: #fbf3dc;
  color: #6b4f1d;
}

.notes {
  display: flex;
  flex-wrap: wrap;
  gap: 3px;
  justify-content: center;
  max-width: 132px;
}

.mark.empty {
  opacity: 0.6;
}

.is-selected {
  outline: 3px solid var(--accent);
  outline-offset: 3px;
  border-radius: 12px;
  background: rgb(140 59 46 / 6%);
}

.is-current .token {
  box-shadow: 0 0 0 3px var(--accent-soft), 0 2px 8px rgb(0 0 0 / 18%);
}

.is-pending .token {
  box-shadow: 0 0 0 3px #f0d9ab, 0 2px 8px rgb(0 0 0 / 18%);
}

.is-decision .token {
  box-shadow: 0 0 0 3px #c8d1e6, 0 2px 8px rgb(0 0 0 / 18%);
}
</style>
