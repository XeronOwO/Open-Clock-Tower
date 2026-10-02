/**
 * 把服务端下发的 slug / 枚举名翻成中文呈现文案。
 *
 * 依据：
 * - 角色名与阵营：docs/standard/terminology.md §9（25 人花名册的权威来源）。
 *   枚举值本身来自 Kernel：LifeState / Alignment / DrunkState / PoisonState / MalfunctionKind。
 * - 未知取值**不猜**，原样回显：服务端加了新枚举而前端没跟上时，说书人看到的是
 *   清清楚楚的英文名，而不是被吞掉的空白（与「未观测 ≠ 默认值」同一姿态）。
 */

/** 角色档案：中文名 + 所属类型。 */
export interface CharacterProfile {
  readonly slug: string
  readonly name: string
  readonly type: string
}

/**
 * 首版花名册（《梦殒春宵》25 人）。
 * 顺序与 docs/standard/terminology.md §9 一致，便于人工核对。
 */
export const ROSTER: readonly CharacterProfile[] = [
  { slug: 'clockmaker', name: '钟表匠', type: '镇民' },
  { slug: 'dreamer', name: '筑梦师', type: '镇民' },
  { slug: 'snake-charmer', name: '舞蛇人', type: '镇民' },
  { slug: 'mathematician', name: '数学家', type: '镇民' },
  { slug: 'flowergirl', name: '卖花女孩', type: '镇民' },
  { slug: 'town-crier', name: '城镇公告员', type: '镇民' },
  { slug: 'oracle', name: '神谕者', type: '镇民' },
  { slug: 'savant', name: '博学者', type: '镇民' },
  { slug: 'seamstress', name: '女裁缝', type: '镇民' },
  { slug: 'philosopher', name: '哲学家', type: '镇民' },
  { slug: 'artist', name: '艺术家', type: '镇民' },
  { slug: 'juggler', name: '杂耍艺人', type: '镇民' },
  { slug: 'sage', name: '贤者', type: '镇民' },
  { slug: 'mutant', name: '畸形秀演员', type: '外来者' },
  { slug: 'sweetheart', name: '心上人', type: '外来者' },
  { slug: 'barber', name: '理发师', type: '外来者' },
  { slug: 'klutz', name: '呆瓜', type: '外来者' },
  { slug: 'evil-twin', name: '镜像双子', type: '爪牙' },
  { slug: 'witch', name: '女巫', type: '爪牙' },
  { slug: 'cerenovus', name: '洗脑师', type: '爪牙' },
  { slug: 'pit-hag', name: '麻脸巫婆', type: '爪牙' },
  { slug: 'fang-gu', name: '方古', type: '恶魔' },
  { slug: 'vigormortis', name: '亡骨魔', type: '恶魔' },
  { slug: 'no-dashii', name: '诺-达鲺', type: '恶魔' },
  { slug: 'vortox', name: '涡流', type: '恶魔' },
]

const ROSTER_BY_SLUG = new Map(ROSTER.map((profile) => [profile.slug, profile]))

const DIMENSION_LABELS: Readonly<Record<string, string>> = {
  Life: '生死',
  Character: '角色',
  Alignment: '阵营',
  Drunk: '醉酒',
  Poison: '中毒',
}

const VALUE_LABELS: Readonly<Record<string, string>> = {
  // 生死
  Alive: '存活',
  Dead: '死亡',
  // 阵营
  Good: '善良',
  Evil: '邪恶',
  // 醉酒
  Sober: '清醒',
  Drunk: '醉酒',
  // 中毒
  Healthy: '健康',
  Poisoned: '中毒',
  // 效果类型
  Persistent: '常驻',
  Instantaneous: '即时',
  // 终止原因
  SourceDied: '来源死亡',
  SourceLostAbility: '来源失去能力',
  StorytellerVoided: '说书人裁定作废',
  // 失效原因（Kernel MalfunctionKind ↔ rulings.md R-0004）
  Open: '未定（R-0004）',
  Jinx: '相克',
  Vortox: '涡流',
  Barista: '咖啡师',
  AbilityDesign: '能力自身设定',
  StorytellerRuling: '说书人裁定',
  // 控制模式
  Automatic: '自动',
  StorytellerTakeover: '说书人接管',
  // 大阶段（GamePhase；尚未开夜时服务端下发占位串 NotStarted）
  NotStarted: '未开始',
  FirstNight: '首夜',
  OtherNight: '夜晚',
  Night: '夜晚',
  Day: '白天',
  Resolving: '结算中',
  // 每步摘要：能力判定依据（SlotAbilityBasis）
  Settled: '已结算',
  Preview: '按当前账预览',
  Unknown: '无法判定',
  // 无合法选项时的行为（Kernel NoOptionBehavior ↔ R-0009）
  Skip: '跳过这一步（配额照走）',
  StorytellerDecides: '由说书人自由决定',
  BlockAndAlert: '阻塞并报警，等说书人处理',
  // 命令回执
  Accepted: '已受理',
  Rejected: '被拒绝',
  Duplicate: '重复投递（返回首次结果）',
  Failed: '失败',
  // 玩家可见事件
  PhaseStarted: '阶段开始',
  RequestIssued: '收到请求',
  RequestAnswered: '已作答',
  RequestVoided: '请求被作废',
  InformationResultIssued: '收到信息',
}

/** 未知取值原样回显，绝不吞掉。 */
export function labelOf(raw: string | null | undefined): string {
  if (raw === null || raw === undefined || raw === '') {
    return '—'
  }

  return VALUE_LABELS[raw] ?? raw
}

/**
 * 请求作废原因（Kernel OperationRequestVoidReason）→ 中文。
 * 与 ControlMode 的同名枚举值分开映射：`StorytellerTakeover` 在"控制模式"里是接管，
 * 在"作废原因"里是强推/接管切步了结，不能共用一张表。
 */
const VOID_REASON_LABELS: Readonly<Record<string, string>> = {
  StorytellerForce: '说书人强制作废',
  StorytellerTakeover: '强推 / 接管切步了结',
  DependencyViolated: '座位依赖不再满足',
  PhaseAdvanced: '阶段已推进',
  Superseded: '被上游新请求取代',
}

/** 作废原因 → 中文；未知原因原样回显。 */
export function voidReasonLabelOf(raw: string | null | undefined): string {
  if (raw === null || raw === undefined || raw === '') {
    return '—'
  }

  return VOID_REASON_LABELS[raw] ?? raw
}

/** 维度名 → 中文；未知维度原样回显。 */
export function dimensionLabelOf(dimension: string): string {
  return DIMENSION_LABELS[dimension] ?? dimension
}

/** 角色 slug → 中文名；不在花名册里的 slug 原样回显。 */
export function characterNameOf(slug: string | null | undefined): string {
  if (slug === null || slug === undefined || slug === '') {
    return '—'
  }

  return ROSTER_BY_SLUG.get(slug)?.name ?? slug
}

/** 角色 slug → 类型（镇民 / 外来者 / 爪牙 / 恶魔）；未知返回空串。 */
export function characterTypeOf(slug: string | null | undefined): string {
  if (slug === null || slug === undefined) {
    return ''
  }

  return ROSTER_BY_SLUG.get(slug)?.type ?? ''
}

/** 角色 slug → "中文名（英文 slug）"，说书人视图用它避免歧义。 */
export function characterLabelOf(slug: string | null | undefined): string {
  if (slug === null || slug === undefined || slug === '') {
    return '—'
  }

  const profile = ROSTER_BY_SLUG.get(slug)
  return profile === undefined ? slug : `${profile.name}（${profile.slug}）`
}
