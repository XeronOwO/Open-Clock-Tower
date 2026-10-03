<script setup lang="ts">
/**
 * 席位注记编辑：列出 / 新增 / 修改 / 删除某席的自由文本注记（D-0019）。
 *
 * 边界：
 * - 注记**不参与任何规则判定**，也不改游戏状态；命令入口只有 `sender` 一条（D-0012）；
 * - 本地只做"明显不合规不进网"的预检，服务端仍是权威（客户端输入不可信）；
 * - 牌面上的 token 由 `GrimoireSeatCard` 渲染（截断 + 全文 title），这里显示全文与操作。
 */
import type { SeatAnnotationDto, StorytellerViewDto } from '@/contracts/game'
import {
  MAX_ANNOTATION_LENGTH,
  MAX_ANNOTATIONS_PER_SEAT,
  replaceControlCharacters,
  seatLabelOf,
} from '@/display/format'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  addSeatAnnotation,
  removeSeatAnnotation,
  updateSeatAnnotation,
  type CommandOutcome,
  type CommandSender,
} from '@/services/storytellerCommands'
import { computed, ref, watch } from 'vue'

const props = defineProps<{
  view: StorytellerViewDto
  sender: CommandSender
  /** 目标席位；null = 还没选。 */
  seat: number | null
}>()

const emit = defineEmits<{ outcome: [CommandOutcome]; engage: [] }>()

/** 该席现有的注记，按发生顺序。 */
const notes = computed(() =>
  props.seat === null
    ? []
    : props.view.annotations.filter((annotation) => annotation.seat === props.seat),
)

const draft = ref('')
const editingId = ref<number | null>(null)
const editingText = ref('')
const busy = ref(false)

const limitReached = computed(() => notes.value.length >= MAX_ANNOTATIONS_PER_SEAT)

/** 换席整表复位：上一次的草稿与编辑态不许静默带到下一席（与状态上报表单同族，H-1）。 */
watch(
  () => props.seat,
  () => {
    draft.value = ''
    editingId.value = null
    editingText.value = ''
  },
)

function reject(message: string): void {
  emit('outcome', { ok: false, kind: 'Rejected', sequence: null, message, rebuild: null })
}

async function run(action: () => Promise<CommandOutcome>): Promise<CommandOutcome> {
  busy.value = true
  try {
    const outcome = await action()
    emit('outcome', outcome)
    return outcome
  } finally {
    busy.value = false
  }
}

/** 与服务端同一口径的归一化长度：折叠空白后再比上限。 */
function normalizedLengthOf(text: string): number {
  return replaceControlCharacters(text).replace(/\s+/g, ' ').trim().length
}

/** 本地预检（服务端还会再校验一遍，前端不是安全边界）。 */
function validate(text: string): string | null {
  if (text.trim().length === 0) {
    return '注记不能为空'
  }

  return normalizedLengthOf(text) > MAX_ANNOTATION_LENGTH
    ? `注记最多 ${MAX_ANNOTATION_LENGTH} 个字符`
    : null
}

async function add(): Promise<void> {
  const target = props.seat
  if (target === null) {
    reject('还没有选中席位')
    return
  }

  const problem = validate(draft.value)
  if (problem !== null) {
    reject(problem)
    return
  }

  if (limitReached.value) {
    reject(`每席最多 ${MAX_ANNOTATIONS_PER_SEAT} 条注记`)
    return
  }

  const outcome = await run(() =>
    addSeatAnnotation(props.sender, target, draft.value, newIdempotencyKey('annotation-add')),
  )
  if (outcome.ok) {
    draft.value = ''
  }
}

function startEdit(annotation: SeatAnnotationDto): void {
  editingId.value = annotation.id
  editingText.value = annotation.text
}

function cancelEdit(): void {
  editingId.value = null
  editingText.value = ''
}

async function saveEdit(): Promise<void> {
  const id = editingId.value
  if (id === null) {
    return
  }

  const problem = validate(editingText.value)
  if (problem !== null) {
    reject(problem)
    return
  }

  const outcome = await run(() =>
    updateSeatAnnotation(props.sender, id, editingText.value, newIdempotencyKey('annotation-update')),
  )
  if (outcome.ok) {
    cancelEdit()
  }
}

async function remove(annotation: SeatAnnotationDto): Promise<void> {
  await run(() =>
    removeSeatAnnotation(props.sender, annotation.id, newIdempotencyKey('annotation-remove')),
  )
}
</script>

<template>
  <div class="annotations" data-testid="annotation-control" @focusin="emit('engage')">
    <div class="line">
      <span class="tag">注记</span>
      <strong>{{ seatLabelOf(seat) }}</strong>
      <span class="hint">{{ notes.length }}/{{ MAX_ANNOTATIONS_PER_SEAT }}</span>
      <span class="hint">自由文本：不进状态账、玩家不可见（D-0019）</span>
    </div>

    <ul v-if="notes.length > 0" class="list" data-testid="annotation-list">
      <li
        v-for="annotation in notes"
        :key="annotation.id"
        class="note"
        data-testid="annotation-item"
        :data-note-id="annotation.id"
      >
        <template v-if="editingId === annotation.id">
          <input
            v-model="editingText"
            :maxlength="MAX_ANNOTATION_LENGTH"
            data-testid="annotation-edit-input"
          />
          <button type="button" :disabled="busy" data-testid="annotation-save" @click="saveEdit()">
            保存
          </button>
          <button type="button" :disabled="busy" data-testid="annotation-cancel" @click="cancelEdit()">
            取消
          </button>
        </template>
        <template v-else>
          <span class="text">{{ annotation.text }}</span>
          <button
            type="button"
            :disabled="busy"
            data-testid="annotation-edit"
            @click="startEdit(annotation)"
          >
            改
          </button>
          <button
            type="button"
            :disabled="busy"
            data-testid="annotation-delete"
            @click="remove(annotation)"
          >
            删
          </button>
        </template>
      </li>
    </ul>
    <p v-else class="hint" data-testid="annotation-empty">该席还没有注记。</p>

    <div class="free">
      <input
        v-model="draft"
        :maxlength="MAX_ANNOTATION_LENGTH"
        placeholder="写一条注记（示例：18 不共边）"
        data-testid="annotation-input"
      />
      <button
        type="button"
        class="primary"
        :disabled="busy || limitReached"
        data-testid="annotation-submit"
        @click="add()"
      >
        添加
      </button>
    </div>
  </div>
</template>

<style scoped>
.annotations {
  border-top: 1px dashed var(--line);
  padding-top: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.line {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}

.list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.note {
  display: flex;
  gap: 6px;
  align-items: center;
  flex-wrap: wrap;
  font-size: 12px;
}

.note .text {
  flex: 1 1 auto;
  min-width: 0;
  overflow-wrap: anywhere;
}

.free {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}
</style>
