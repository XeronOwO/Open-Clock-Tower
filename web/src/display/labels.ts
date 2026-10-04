/**
 * 把服务端下发的 slug / 枚举名翻成中文呈现文案。
 *
 * 依据：
 * - 角色名与阵营：docs/standard/terminology.md §9（30 人花名册的权威来源：25 非旅行者 + 5 旅行者）。
 *   枚举值本身来自 Kernel：LifeState / Alignment / DrunkState / PoisonState / MalfunctionKind。
 * - 未知取值**不猜**，原样回显：服务端加了新枚举而前端没跟上时，说书人看到的是
 *   清清楚楚的英文名，而不是被吞掉的空白（与「未观测 ≠ 默认值」同一姿态）。
 */

/** 角色档案：中文名 + 所属类型 + 可选阵型修正（`[...]` 设置调整，仅初始设置生效）。 */
export interface CharacterProfile {
  readonly slug: string
  readonly name: string
  readonly type: string
  /**
   * 阵型修正（`docs/standard/terminology.md` §7 `setup-modifier`）。
   *
   * 服务端权威数据在 `SectsAndVioletsRoster`（设置调整参与净分布，口径见 `rulings.md` R-0042）；
   * 这里保留的是**呈现文案**：方括号片段（如 `[+1 外来者]`）必须与权威数据逐字一致——
   * 两侧对账由规范门禁 `RosterMirrorGateTests` 强制，漂移会红。
   */
  readonly setupModifier?: string
}

/**
 * 首版花名册（《梦殒春宵》30 人 = 25 非旅行者 + 5 旅行者）。
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
  { slug: 'fang-gu', name: '方古', type: '恶魔', setupModifier: '[+1 外来者]：初始设置时用一个外来者角色标记替换一个镇民角色标记' },
  { slug: 'vigormortis', name: '亡骨魔', type: '恶魔', setupModifier: '[-1 外来者]：初始设置时用一个镇民角色标记替换一个外来者角色标记；没有可移除的外来者时不作调整' },
  { slug: 'no-dashii', name: '诺-达鲺', type: '恶魔' },
  { slug: 'vortox', name: '涡流', type: '恶魔' },
  // 旅行者（D-0022 首版纳入；阵营由说书人私下裁定，不参与配板，R-0046）。
  { slug: 'deviant', name: '怪咖', type: '旅行者' },
  { slug: 'bone-collector', name: '集骨者', type: '旅行者' },
  { slug: 'barista', name: '咖啡师', type: '旅行者' },
  { slug: 'harlot', name: '流莺', type: '旅行者' },
  { slug: 'butcher', name: '屠夫', type: '旅行者' },
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
  NoLongerApplies: '条件不再满足',
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
  GameEnded: '本局已结束',
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

/** 角色 slug → 类型（镇民 / 外来者 / 爪牙 / 恶魔 / 旅行者）；未知返回空串。 */
export function characterTypeOf(slug: string | null | undefined): string {
  if (slug === null || slug === undefined) {
    return ''
  }

  return ROSTER_BY_SLUG.get(slug)?.type ?? ''
}

const TYPE_LABELS: Readonly<Record<string, string>> = {
  Townsfolk: '镇民',
  Outsider: '外来者',
  Minion: '爪牙',
  Demon: '恶魔',
  Traveller: '旅行者',
}

/** 角色类型枚举名（Kernel，如 `Townsfolk`）→ 中文；未知取值原样回显，不猜。 */
export function typeLabelOf(raw: string | null | undefined): string {
  if (raw === null || raw === undefined || raw === '') {
    return '—'
  }

  return TYPE_LABELS[raw] ?? raw
}

/**
 * 效果窗口分类（`EffectWindowKind`）→ 中文。
 *
 * 窗口是「直到下个黄昏」这类跨阶段事实的可见载体：咖啡师两个效果（R-0047 / R-0052）与
 * 集骨者的重获能力（R-0054）。未知取值原样回显——服务端加了新窗口而前端没跟上时，
 * 说书人看到的是清楚的英文名，不是被吞掉的空白。
 */
const WINDOW_LABELS: Readonly<Record<string, string>> = {
  AfflictionImmunity: '清醒且健康（免疫窗口）',
  SecondAction: '行动两次',
  RegainedAbility: '重获能力（直到下个黄昏）',
}

/** 效果窗口分类 → 中文；null / 空串 = 普通效果（调用方据此不显示这一行）。 */
export function windowLabelOf(window: string | null | undefined): string | null {
  if (window === null || window === undefined || window === '') {
    return null
  }

  return WINDOW_LABELS[window] ?? window
}

/**
 * 已选角色的阵型修正提示（`[...]` 设置调整）：按花名册顺序返回带修正的角色档案。
 * 空数组 = 没有需要提示的修正——前端只显示，不替服务端判规则。
 */
export function setupModifiersOf(slugs: readonly string[]): readonly CharacterProfile[] {
  const selected = new Set(slugs)
  return ROSTER.filter(
    (profile) => selected.has(profile.slug) && profile.setupModifier !== undefined,
  )
}

/** 角色 slug → "中文名（英文 slug）"，说书人视图用它避免歧义。 */
export function characterLabelOf(slug: string | null | undefined): string {
  if (slug === null || slug === undefined || slug === '') {
    return '—'
  }

  const profile = ROSTER_BY_SLUG.get(slug)
  return profile === undefined ? slug : `${profile.name}（${profile.slug}）`
}

/**
 * 能力 slug → 它在魔典牌面上留下的**提示标记名**（百科口径）。
 * 未登记的能力**不在这里编名字**：由调用方退回「施加时的来源角色」，未知取值原样回显。
 */
const EFFECT_MARK_LABELS: Readonly<Record<string, string>> = {
  // 百科《女巫》· 2026-10-01 抓取 · 提示标记：「被诅咒」（放置在女巫要诅咒的玩家角色标记旁）
  'witch.curse': '被诅咒',
  // 百科《哲学家》· 2026-10-01 抓取 · 提示标记「醉酒」：被选角色在场时放在它的持有者角色标记旁（R-0036）
  'philosopher.grant.drunk': '醉酒',
  // 「获得能力」是哲学家那次授予本身（R-0036：账上一条常驻事实）；放在他自己的牌面上
  'philosopher.grant': '获得能力',
}

/** 能力 slug → 提示标记名；未登记或空值返回 null。 */
export function effectMarkNameOf(ability: string | null | undefined): string | null {
  if (ability === null || ability === undefined || ability === '') {
    return null
  }

  return EFFECT_MARK_LABELS[ability] ?? null
}
