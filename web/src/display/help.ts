/**
 * 界面说明文案登记表（票据 `ui-layout-and-onboarding` 矩阵行 2）。
 *
 * 边界：只解释**既有**术语、标记与计数，不承载新知识、不做规则断言；
 * 涉及规则 / 口径的条目引用 `docs/standard/terminology.md` 或裁定号（`docs/standard/rulings.md`）。
 *
 * 说明文案进这里、不进组件：两端（说书人 / 玩家 / 复盘）共用同一份口径（矩阵行 7），
 * 前端单测直接对这张表断言（`help.spec.ts`）。
 */

/** 需要就近解释的条目 id；新增条目必须先在这里登记，再在组件里引用。 */
export type HelpTopicId =
  | 'phase'
  | 'control'
  | 'slot'
  | 'event-sequence'
  | 'plan'
  | 'pending'
  | 'decision'
  | 'blocked'
  | 'seat'
  | 'player-name'
  | 'status-ledger'
  | 'effect-chain'
  | 'madness'
  | 'execution'
  | 'votes'
  | 'information'
  | 'voided-request'
  | 'audit'
  | 'grimoire'
  | 'once-marker'
  | 'barber-night'
  | 'player-request'
  | 'player-character'
  | 'replay'

export interface HelpTopic {
  /** 气泡标题（也是 `?` 的无障碍名称）。 */
  readonly title: string
  /** 一到两句话，读一遍能懂；不写"详见文档"式的空话。 */
  readonly text: string
}

/**
 * 全部说明文案。类型是 `Record<HelpTopicId, HelpTopic>`：漏一条编译不过，
 * 所以组件里不会出现"没有说明的 `?`"。
 */
export const HELP_TOPICS: Readonly<Record<HelpTopicId, HelpTopic>> = {
  phase: {
    title: '阶段',
    text: '这一局现在走到哪一段：首夜 / 夜晚 / 白天。阶段由服务端推进，界面只显示当前值。',
  },
  control: {
    title: '控制',
    text: '谁在推着步骤走。「自动」= 服务端按顺序表往下走；「说书人接管」= 自动步进停住，由你强推（D-0014）。',
  },
  slot: {
    title: '槽位',
    text: '夜晚顺序表里的一格：一格 = 一个角色的行动窗口，走完一格进下一格。',
  },
  'event-sequence': {
    title: '事件序号',
    text: '服务端事件流的递增编号。重连补齐与复盘定位都用它：数字越大越新。',
  },
  plan: {
    title: '计划',
    text: '本夜按顺序表生成的行动清单。「已走完」= 这一夜的每一格都处理过了。',
  },
  pending: {
    title: '卡点',
    text: '某个席位还没作答，这一步停在那里等它。平台没有超时；你可以代填，或强推（D-0011 / D-0014）。',
  },
  decision: {
    title: '待裁定',
    text: '需要说书人拍板的裁定点（例如选择结果、死亡裁量）。',
  },
  blocked: {
    title: '阻塞',
    text: '这一格没有合法选项、按声明要等你处理；处理之前计划不前进（R-0009）。',
  },
  seat: {
    title: '席位',
    text: '玩家在桌上的位置（1..N），一局内固定。角色、阵营、生死这些状态属于席位（terminology §5）。',
  },
  'player-name': {
    title: '玩家名',
    text: '账号里的公开名字，显示成「3 号 · 小明」。名字只是称呼，不参与授权与任何判定（D-0021）。',
  },
  'status-ledger': {
    title: '状态账',
    text: '只记"已经被观测到"的维度事实与归因；没有记录的维度显示为未观测，而不是默认健康（D-0015）。',
  },
  'effect-chain': {
    title: '效果链',
    text: '能力的后果挂在谁身上、由谁施加、现在还在不在（含已终止——"为什么解毒"要查得到）。',
  },
  madness: {
    title: '疯狂',
    text: '说书人给玩家下的要求（例如不能说出某个词）。引擎不判定疯狂，只记录裁定（R-0003）。',
  },
  execution: {
    title: '处决',
    text: '白天最多一次。处决 ≠ 死亡：可以处决而不死（terminology §6）。',
  },
  votes: {
    title: '票数',
    text: '这一次提名当前收到的票数；计票之前还可能变。',
  },
  information: {
    title: '信息',
    text: '只有本人看得见的信息结果。信息可能是错的——说书人对醉酒 / 中毒玩家的信息有裁量权（D-0002）。',
  },
  'voided-request': {
    title: '请求作废',
    text: '这一步的请求被取消了（强推 / 阶段推进 / 本局结束等），不需要再作答。',
  },
  audit: {
    title: '审计',
    text: '服务端的事件与账本明细，用来核对"这一步为什么发生"；与主视图来自同一次推送。',
  },
  grimoire: {
    title: '魔典',
    text: '圆环只是呈现：席位顺序固定为 1..N、不能拖动重排；角色、生死、标记都来自服务端最新一次推送。',
  },
  'once-marker': {
    title: '限一次',
    text: '方古第一次成功杀死外来者后侵染对方，此后整局都带着这个标记（R-0034）。',
  },
  'barber-night': {
    title: '今晚理发',
    text: '理发师死亡的当夜：恶魔可以选择交换两名玩家的角色；窗口结清后标记消失（R-0033）。',
  },
  'player-request': {
    title: '请求',
    text: '轮到你了：服务端把这一步要做的选择发过来。没有请求就是还不用你出手；平台不会超时。',
  },
  'player-character': {
    title: '我的角色',
    text: '你这一局拿到的角色与阵营，只发给你自己。换角、换阵营之后这里会跟着变（R-0059）。',
  },
  replay: {
    title: '复盘',
    text: '按原子步骤回放这一局：每一步谁做了什么、局面发生了什么变化。',
  },
}
