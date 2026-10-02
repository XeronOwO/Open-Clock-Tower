/**
 * 装置段落执行器 + 断言检查器 —— 主装置 verify-storyteller-panel.mjs 的分段与档位过滤。
 *
 * 为什么是"前缀执行 + 判定过滤"，而不是"跳段执行"：
 *   装置是一条单会话线性剧本（加入 → 分配 → 开夜 → 白天 → 重建）。跳过中间段就没有可断言的
 *   状态，所以诚实的语义是：
 *     --only X   执行到 X 为止（前面的段作为必要前置照样跑），只有 X 的断言计入判定；
 *     --from X   全程照跑，但只有 X 及之后的断言计入判定；
 *   两者互斥。段落名同时出现在用法头、运行时段头（begin 打印）与报告的按段统计里。
 *
 * 断言档位过滤（check 的第 4 个参数）：
 *   check(label, pass, detail, { slowPacer: true })    该断言依赖 ≥1 秒/槽的节拍（观察中间槽位、拦截挂起请求）
 *   check(label, pass, detail, { screenshots: true })  该断言依赖截图落盘（"证据截图都已落盘"）
 * 迭代档（快节拍 / 不落盘）下它们记 SKIP 并汇总，而不是误红——这是票据 P0-2 的显式口径。
 */

/**
 * 创建段执行器。sections 是 [{ id, title }] 的有序数组。
 * begin(id) 返回 true = 继续执行本段；返回 false = 操作范围已到（调用方立即 return）。
 */
export function createSectionRunner(sections, { only = [], from = undefined, log = console.log } = {}) {
  const indexById = new Map(sections.map((section, index) => [section.id, index]))
  const available = sections.map((section) => section.id).join(', ')

  for (const id of only) {
    if (!indexById.has(id)) {
      throw new Error(`--only 未知段：${id}（可用段：${available}）`)
    }
  }

  if (from !== undefined && !indexById.has(from)) {
    throw new Error(`--from 未知段：${from}（可用段：${available}）`)
  }

  const judged = new Set()
  let operationEndIndex = sections.length - 1
  if (only.length > 0) {
    for (const id of only) {
      judged.add(id)
    }

    operationEndIndex = Math.max(...only.map((id) => indexById.get(id)))
  } else if (from !== undefined) {
    for (let index = indexById.get(from); index < sections.length; index += 1) {
      judged.add(sections[index].id)
    }
  } else {
    for (const section of sections) {
      judged.add(section.id)
    }
  }

  let currentId = null
  let currentStartedAt = 0
  let stopped = false
  const timings = new Map()

  /** 结算当前段的用时（幂等：结算后 currentStartedAt 归零）。 */
  function settleTiming() {
    if (currentId !== null && currentStartedAt > 0) {
      timings.set(currentId, (timings.get(currentId) ?? 0) + (Date.now() - currentStartedAt))
      currentStartedAt = 0
    }
  }

  return {
    sections,
    get currentId() {
      return currentId
    },
    isJudged(id) {
      return judged.has(id)
    },
    selectionSummary() {
      if (stopped) {
        return `操作执行到 [${currentId}] 为止；判定=${[...judged].join(',') || '（空）'}`
      }

      return only.length > 0
        ? `--only ${[...judged].join(',')}（执行到 [${sections[operationEndIndex].id}] 为止）`
        : from !== undefined
          ? `--from ${from}（全程执行；判定从该段起）`
          : '全部段'
    },
    /** 各段耗时（秒，保留一位小数），按段序返回 [[id, seconds], ...]。 */
    timingRows() {
      settleTiming()
      return sections
        .filter((section) => timings.has(section.id))
        .map((section) => [section.id, Number((timings.get(section.id) / 1000).toFixed(1))])
    },
    reportTimings() {
      const rows = this.timingRows()
      if (rows.length === 0) {
        return
      }

      const total = rows.reduce((sum, [, seconds]) => sum + seconds, 0)
      log('\n=== 各段耗时 ===')
      for (const [id, seconds] of rows) {
        log(`[${id}] ${seconds}s`)
      }

      log(`合计 ${total.toFixed(1)}s（不含收尾清理）`)
    },
    begin(id) {
      const index = indexById.get(id)
      if (index === undefined) {
        throw new Error(`未知段：${id}`)
      }

      if (index > operationEndIndex) {
        stopped = true
        settleTiming()
        log(`\n=== [${id}] ${sections[index].title} —— 超出本次操作范围，跳过 ===`)
        return false
      }

      settleTiming()
      currentId = id
      currentStartedAt = Date.now()
      const isJudged = judged.has(id)
      log(`\n=== [${id}] ${sections[index].title}${isJudged ? '' : ' · 只作前置（判定跳过）'} ===`)
      return true
    },
  }
}

/**
 * 创建断言检查器。check 与旧的 `check(label, pass, detail)` 兼容，多出第 4 个档位参数。
 * results 里的 outcome 是 'pass' | 'fail' | 'skip'；退出码只看 fail。
 */
export function createChecker({
  sections = [],
  isJudged = () => true,
  currentSection = () => null,
  slowPacer = true,
  screenshots = true,
  log = console.log,
} = {}) {
  const results = []

  function check(label, pass, detail = '', options = {}) {
    const section = currentSection()

    // 取证档专属：迭代档下**按设计**跳过（不是失败）。
    if (options.slowPacer && !slowPacer) {
      results.push({ section, label, outcome: 'skip', reason: '取证档专属：需要 ≥1 秒/槽的节拍（迭代档跳过）' })
      log(`  [SKIP] ${label} → 取证档专属：需要 ≥1 秒/槽的节拍（迭代档跳过）`)
      return
    }

    if (options.screenshots && !screenshots) {
      results.push({ section, label, outcome: 'skip', reason: '取证档专属：需要截图落盘（迭代档跳过）' })
      log(`  [SKIP] ${label} → 取证档专属：需要截图落盘（迭代档跳过）`)
      return
    }

    // 未计入判定的前置段：通过记 SKIP；**失败必须照记**——
    // "只跑一节"可以省略判定，但不能把前置段的真实红吞成"全绿"（对抗自检 P1）。
    if (!isJudged(section)) {
      if (pass) {
        results.push({ section, label, outcome: 'skip', reason: `段 [${section ?? '未定'}] 未计入本次判定（前置段）` })
        return
      }

      const gatedDetail = `${detail ? `${detail}；` : ''}来自未选中的前置段 [${section ?? '未定'}]——真实失败仍计入`
      results.push({ section, label, outcome: 'fail', detail: gatedDetail })
      log(`  [FAIL·前置] ${label} → ${gatedDetail}`)
      return
    }

    results.push({ section, label, outcome: pass ? 'pass' : 'fail', detail })
    log(`  ${pass ? '[PASS]' : '[FAIL]'} ${label}${detail ? ` → ${detail}` : ''}`)
  }

  function report() {
    const counts = new Map(sections.map((section) => [section.id, { pass: 0, fail: 0, skip: 0 }]))
    const fails = []
    const skipReasons = new Map()
    for (const result of results) {
      const bucket = counts.get(result.section) ?? { pass: 0, fail: 0, skip: 0 }
      if (result.outcome === 'fail') {
        bucket.fail += 1
        fails.push(result)
      } else if (result.outcome === 'skip') {
        bucket.skip += 1
        skipReasons.set(result.reason, (skipReasons.get(result.reason) ?? 0) + 1)
      } else {
        bucket.pass += 1
      }

      counts.set(result.section, bucket)
    }

    console.log('\n=== 结果（按段）===')
    for (const section of sections) {
      const bucket = counts.get(section.id)
      if (bucket === undefined || bucket.pass + bucket.fail + bucket.skip === 0) {
        continue
      }

      console.log(`[${section.id}] 通过 ${bucket.pass} · 失败 ${bucket.fail} · 跳过 ${bucket.skip}`)
    }

    if (fails.length > 0) {
      console.log('\n—— 失败明细 ——')
      for (const fail of fails) {
        console.log(`FAIL  [${fail.section}] ${fail.label}${fail.detail ? ` → ${fail.detail}` : ''}`)
      }
    }

    if (skipReasons.size > 0) {
      console.log('\n—— 跳过汇总（不算失败）——')
      for (const [reason, count] of skipReasons) {
        console.log(`跳过 ${count} 项：${reason}`)
      }
    }

    const passed = results.filter((result) => result.outcome === 'pass').length
    const skipped = results.filter((result) => result.outcome === 'skip').length
    console.log(
      fails.length === 0
        ? `\n全部通过（判定 ${passed} 项 · 跳过 ${skipped} 项）`
        : `\n失败 ${fails.length} 项（判定 ${passed} 通过 · 跳过 ${skipped}）`,
    )
  }

  return { check, report, results }
}
