/**
 * 装置档位与构建准备 —— 五个 verify-*.mjs 共用的命令行前台。
 *
 * 回答两个问题：
 *   1) 这次按什么档跑？（迭代档 = 默认；取证档 = 显式开关）
 *   2) 宿主产物能直接用吗，还是必须先重建？
 *
 * 口径（票据 docs/backlog/done/device-verification-fast-lane.md）：
 *   - 迭代档（默认）：节拍 0.3 秒/槽、截图不落盘、复用已构建产物——把"改一行跑几分钟"压成几十秒的回路；
 *   - 取证档（显式）：--quota 2 --screenshots-all（必要时 --build）——一批只跑一次，只对冻结版本跑；
 *   - 构建默认"自动"：产物不存在、或 src/ 下源码比产物新，就重建；--build 强制重建，--skip-build 显式复用（产物缺失即报错）。
 *
 * 这里只做命令行与构建两件事：断言的档位过滤在 verify-sections.mjs，流程编排在各装置自己。
 */
import { spawn } from 'node:child_process'
import { existsSync, readdirSync, statSync } from 'node:fs'
import path from 'node:path'

/** 提取档位开关后剩余的 argv 交给各装置自己的参数解析。 */
export function extractProfileFlags(argv) {
  const flags = {
    quotaSeconds: undefined,
    build: false,
    skipBuild: false,
    screenshotsAll: false,
    noScreenshots: false,
    only: [],
    from: undefined,
    listSections: false,
  }
  const rest = []
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index]
    const value = () => argv[index + 1]
    switch (argument) {
      case '--quota':
        flags.quotaSeconds = Number.parseFloat(value() ?? '')
        index += 1
        break
      case '--build':
        flags.build = true
        break
      case '--skip-build':
        flags.skipBuild = true
        break
      case '--screenshots-all':
        flags.screenshotsAll = true
        break
      case '--no-screenshots':
        flags.noScreenshots = true
        break
      case '--only':
        flags.only.push(
          ...String(value() ?? '')
            .split(',')
            .map((id) => id.trim())
            .filter(Boolean),
        )
        index += 1
        break
      case '--from':
        flags.from = String(value() ?? '').trim() || undefined
        index += 1
        break
      case '--list-sections':
        flags.listSections = true
        break
      default:
        rest.push(argument)
    }
  }

  return { flags, rest }
}

/**
 * 归一成运行配置。defaults：{ quotaSeconds, screenshots, buildMode, slowPacerThreshold }。
 * slowPacer 是给断言用的档位事实：节拍 ≥ 1 秒/槽才有"观察中间槽位推进 / 拦截挂起请求"这类窗口。
 */
export function resolveProfile(flags, defaults = {}) {
  const quotaSeconds = flags.quotaSeconds ?? defaults.quotaSeconds ?? 0.3
  if (!Number.isFinite(quotaSeconds) || quotaSeconds <= 0) {
    throw new Error(`--quota 非法：${flags.quotaSeconds}（应为正数秒）`)
  }

  if (flags.build && flags.skipBuild) {
    throw new Error('--build 与 --skip-build 互斥：只能选一种')
  }

  if (flags.screenshotsAll && flags.noScreenshots) {
    throw new Error('--screenshots-all 与 --no-screenshots 互斥：只能选一种')
  }

  if (flags.only.length > 0 && flags.from !== undefined) {
    throw new Error('--only 与 --from 互斥：--from 已表示"从该段起全部"')
  }

  const screenshots = flags.screenshotsAll
    ? true
    : flags.noScreenshots
      ? false
      : Boolean(defaults.screenshots)
  const buildMode = flags.build
    ? 'force'
    : flags.skipBuild
      ? 'skip'
      : (defaults.buildMode ?? 'auto')

  return {
    quotaSeconds,
    screenshots,
    buildMode,
    only: flags.only,
    from: flags.from,
    listSections: flags.listSections,
    slowPacer: quotaSeconds >= (defaults.slowPacerThreshold ?? 1),
  }
}

/** 人类可读的一行档位摘要（每次运行都打印，便于取证记录里核对参数）。 */
export function describeProfile(config) {
  const evidence = config.screenshots && config.slowPacer
  const iterating = !config.screenshots && !config.slowPacer && config.buildMode === 'auto'
  const mode = evidence ? '取证档' : iterating ? '迭代档（默认）' : '自定义档'
  const build = { auto: '自动（复用；源码变则重建）', force: '强制构建', skip: '显式复用' }[config.buildMode]
  const section = config.from !== undefined
    ? `从 ${config.from} 起`
    : config.only.length > 0
      ? config.only.join(',')
      : '全部'
  const hint = iterating ? '；取证请显式：--quota 2 --screenshots-all' : ''
  return `${mode}：节拍 ${config.quotaSeconds}s/槽 · 截图${config.screenshots ? '落盘' : '不落盘'} · 构建=${build} · 段=${section}${hint}`
}

/** 宿主 Release 产物路径（外部耦合，见 web/AGENTS.md §3.1）。 */
export function serverArtifactPath(repositoryRoot) {
  const suffix = process.platform === 'win32' ? '.exe' : ''
  return path.join(
    repositoryRoot,
    'src',
    'OpenClockTower.Server',
    'bin',
    'Release',
    'net10.0',
    `OpenClockTower.Server${suffix}`,
  )
}

/**
 * 保证宿主产物可用：按 buildMode 决定"复用 / 自动判断 / 强制构建"。
 * 返回 { artifact, built }，调用方可以据此在输出里说明这次跑的是不是刚构建的产物。
 */
export async function ensureServerArtifacts({ repositoryRoot, buildMode = 'auto', log = console.log }) {
  const artifact = serverArtifactPath(repositoryRoot)
  const present = existsSync(artifact)

  if (buildMode === 'skip') {
    if (!present) {
      throw new Error(`--skip-build 但宿主产物不存在：${artifact}（先去掉 --skip-build，或先手动构建）`)
    }

    log(`复用已构建宿主产物（--skip-build）：${artifact}`)
    return { artifact, built: false }
  }

  const stale = present && newestSourceMtime(repositoryRoot) > statSync(artifact).mtimeMs
  if (buildMode === 'force' || !present || stale) {
    const reason = buildMode === 'force' ? '显式 --build' : present ? 'src/ 源码比产物新' : '产物不存在'
    log(`构建宿主（${reason}）…`)
    await runCommand('dotnet', ['build', 'src/OpenClockTower.Server', '-c', 'Release'], repositoryRoot)
    return { artifact, built: true }
  }

  log(`复用已构建宿主产物（源码未变；要强制重建加 --build）：${artifact}`)
  return { artifact, built: false }
}

/** src/ 下 C# 相关文件的最新修改时间；用于"源码比产物新"的判断（不递归进 bin/obj）。 */
function newestSourceMtime(repositoryRoot) {
  const root = path.join(repositoryRoot, 'src')
  if (!existsSync(root)) {
    return 0
  }

  const sourceFile = /\.(cs|csproj|props|targets|slnx|json|resx|razor)$/i
  let newest = 0
  const stack = [root]
  while (stack.length > 0) {
    const current = stack.pop()
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      if (entry.isDirectory()) {
        if (entry.name === 'bin' || entry.name === 'obj' || entry.name === 'node_modules') {
          continue
        }

        stack.push(path.join(current, entry.name))
        continue
      }

      if (sourceFile.test(entry.name)) {
        newest = Math.max(newest, statSync(path.join(current, entry.name)).mtimeMs)
      }
    }
  }

  return newest
}

/** 跑一个前置命令（构建等），stdout / stderr 直接透传，失败即抛。 */
function runCommand(command, args, cwd) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd, stdio: 'inherit' })
    child.on('error', reject)
    child.on('exit', (code) => {
      if (code === 0) {
        resolve()
        return
      }

      reject(new Error(`${command} ${args.join(' ')} 退出码 ${code}`))
    })
  })
}
