#!/usr/bin/env node
/**
 * 生成 Linux 发布包与配套服务配置（**通用**：填几个值就能用，任何人 clone 下来都能跑）。
 *
 * 产出三样东西，拿到目标机器上照着 `docs/operations/deploy.md` 做即可：
 *   1. `oct-linux.tar.gz`   —— 自包含发布包（自带 .NET 运行时，目标机不用装 .NET）；
 *   2. `clocktower.service` —— systemd 单元，已填好你给的路径 / 端口 / 席位数；
 *   3. `clocktower.conf`    —— nginx 片段，已按你给的前缀与端口写好转发（含 SignalR 长连接头）。
 *
 * 用法（在仓库根运行）：
 *   node tools/deploy-prepare.mjs --app-dir /srv/oct --prefix /clocktower/ --port 5080 --seats 7
 *   node tools/deploy-prepare.mjs --app-dir /srv/oct --prefix / --port 5080 --seats 5   # 挂在站点根
 *
 * 退出码：0 = 成功；1 = 失败（参数不合法 / 构建失败 / 产物缺页面）。
 */
import { spawnSync } from 'node:child_process'
import { cpSync, existsSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const serverProject = path.join(repositoryRoot, 'src', 'OpenClockTower.Server')
const templatesDir = path.join(repositoryRoot, 'tools', 'deploy', 'templates')

/**
 * 跑一个前台命令；输出直接透传（构建耗时可见），失败即抛。
 * 不开 `shell`：Windows 上 `npm` / `dotnet` 是批处理包装，交给 `cmd.exe /c` 调用即可，
 * 参数仍按数组传递、不经 shell 拼接——既避开 Node 的 shell 传参弃用警告，也没有注入面。
 */
function run(command, args, options = {}) {
  const isWindows = process.platform === 'win32'
  const result = isWindows
    ? spawnSync('cmd.exe', ['/c', command, ...args], { stdio: 'inherit', ...options })
    : spawnSync(command, args, { stdio: 'inherit', ...options })
  if (result.error) {
    throw new Error(`无法执行 ${command}：${result.error.message}`)
  }

  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} 退出码 ${result.status}`)
  }
}

function parseArguments(argv) {
  const options = {
    appDir: null,
    prefix: '/clocktower/',
    port: 5080,
    seats: 7,
    slotQuotaSeconds: 10,
    outDir: path.join(repositoryRoot, 'artifacts', 'deploy'),
    skipFrontendBuild: false,
  }

  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index]
    const value = argv[index + 1]
    switch (flag) {
      case '--app-dir':
        options.appDir = value ?? null
        index += 1
        break
      case '--prefix':
        options.prefix = value ?? '/'
        index += 1
        break
      case '--port':
        options.port = Number.parseInt(value ?? '', 10)
        index += 1
        break
      case '--seats':
        options.seats = Number.parseInt(value ?? '', 10)
        index += 1
        break
      case '--slot-quota':
        options.slotQuotaSeconds = Number.parseInt(value ?? '', 10)
        index += 1
        break
      case '--out':
        options.outDir = path.resolve(value ?? options.outDir)
        index += 1
        break
      case '--skip-frontend-build':
        options.skipFrontendBuild = true
        break
      case '--help':
      case '-h':
        printUsage()
        process.exit(0)
        break
      default:
        throw new Error(`未知参数：${flag}（用 --help 看用法）`)
    }
  }

  if (!options.appDir) {
    throw new Error('缺少 --app-dir：目标机器上的应用目录（程序与数据库都放这里）')
  }

  // 前缀形态由部署方式决定，写错会直接导致"资源 404"或"连不上 Hub"，所以在这里就拒绝。
  if (options.prefix !== '/' && !options.prefix.endsWith('/')) {
    throw new Error(`--prefix 必须以 / 结尾（例如 /clocktower/）：当前是 ${options.prefix}`)
  }

  if (options.prefix !== '/' && !options.prefix.startsWith('/')) {
    throw new Error(`--prefix 必须以 / 开头：当前是 ${options.prefix}`)
  }

  if (!Number.isInteger(options.port) || options.port < 1 || options.port > 65535) {
    throw new Error(`--port 不合法：${options.port}`)
  }

  if (!Number.isInteger(options.seats) || options.seats < 1) {
    throw new Error(`--seats 必须 ≥ 1：当前是 ${options.seats}`)
  }

  return options
}

function printUsage() {
  console.log(`用法：node tools/deploy-prepare.mjs --app-dir <目录> [选项]

必填：
  --app-dir <路径>        目标机器上的应用目录（例：/srv/oct）

选项：
  --prefix <前缀>         对外子路径，末尾带斜杠；挂在站点根填 /（默认 /clocktower/）
  --port <端口>           宿主监听的本机端口（默认 5080）
  --seats <数量>          席位数（默认 7）
  --slot-quota <秒>       每个行动格的最短配额（默认 10）
  --out <目录>            产物输出目录（默认 artifacts/deploy）
  --skip-frontend-build   复用现有 web/dist（只有你确定产物是新的才用）
  -h, --help              显示本说明

产出：oct-linux.tar.gz · clocktower.service · clocktower.conf
安装与排查见 docs/operations/deploy.md。`)
}

/** 清空一个目录下的内容（只作用于传入的这一个目录，不碰别处）。 */
function emptyDirectory(directory) {
  if (!existsSync(directory)) {
    return
  }

  rmSync(directory, { recursive: true, force: true })
}

/** 用 LF + UTF-8（无 BOM）写文本：生成的文件要在 Linux 上被 systemd / nginx 读取。 */
function writeUnixText(file, content) {
  writeFileSync(file, content.replace(/\r\n/g, '\n'), { encoding: 'utf8' })
}

/** 按模板生成配置文件。 */
function renderTemplate(name, replacements) {
  let text = readFileSync(path.join(templatesDir, name), 'utf8')
  for (const [key, value] of Object.entries(replacements)) {
    text = text.split(key).join(value)
  }

  return text
}

const options = parseArguments(process.argv.slice(2))
const isRootPath = options.prefix === '/'
// nginx 的 location 值：子路径用带尾巴的前缀（/clocktower/），根路径用 /。
const locationPath = isRootPath ? '/' : options.prefix

console.log(`目标形态：前缀 ${options.prefix} · 端口 ${options.port} · 席位 ${options.seats} · 应用目录 ${options.appDir}`)

mkdirSync(options.outDir, { recursive: true })
const publishDir = path.join(options.outDir, 'publish')
emptyDirectory(publishDir)
mkdirSync(publishDir, { recursive: true })

// ---- 1) 前端：把部署前缀编进产物 ----
if (options.skipFrontendBuild) {
  console.log('跳过前端构建（复用 web/dist）')
} else {
  console.log(`构建前端（VITE_BASE_PATH=${options.prefix}）…`)
  run('npm', ['run', 'build'], {
    cwd: webRoot,
    env: { ...process.env, VITE_BASE_PATH: options.prefix },
  })
}

const distIndex = path.join(webRoot, 'dist', 'index.html')
if (!existsSync(distIndex)) {
  throw new Error(`前端产物不存在：${distIndex}。先跑一次不带 --skip-frontend-build 的构建。`)
}

// 资源引用必须带上前缀，否则部署上去就是"页面能开、资源 404"——这类错在本地很难察觉，所以提前拦。
if (!isRootPath) {
  const indexHtml = readFileSync(distIndex, 'utf8')
  if (!indexHtml.includes(options.prefix)) {
    throw new Error(
      `前端产物里的资源引用不含前缀 ${options.prefix} —— 说明 web/dist 不是用这个前缀构建的。` +
        '删掉 web/dist 后重跑本脚本（不要加 --skip-frontend-build）。',
    )
  }
}

// ---- 2) 后端：自包含发布（目标机不装 .NET） ----
console.log('发布后端（linux-x64 自包含）…')
run('dotnet', [
  'publish',
  serverProject,
  '-c',
  'Release',
  '-r',
  'linux-x64',
  '--self-contained',
  'true',
  // 不带调试符号：包小一半，运行不需要它们。
  '-p:DebugType=none',
  '-p:DebugSymbols=false',
  '-o',
  publishDir,
])

const publishedIndex = path.join(publishDir, 'wwwroot', 'index.html')
if (!existsSync(publishedIndex)) {
  throw new Error(`发布产物里没有页面：${publishedIndex}（检查 OpenClockTower.Server.csproj 的前端复制目标）`)
}

// ---- 3) 打包：`tar -C <目录> .` 的内容形态，解压到任意目录都得到同一层结构 ----
const tarball = path.join(options.outDir, 'oct-linux.tar.gz')
if (existsSync(tarball)) {
  rmSync(tarball, { force: true })
}

run('tar', ['-czf', tarball, '-C', publishDir, '.'])

// ---- 4) 生成两份配置 ----
const unit = renderTemplate('clocktower.service.template', {
  '{{APP_DIR}}': options.appDir,
  '{{PORT}}': String(options.port),
  '{{SEATS}}': String(options.seats),
  '{{SLOT_QUOTA}}': String(options.slotQuotaSeconds),
})
writeUnixText(path.join(options.outDir, 'clocktower.service'), unit)

let nginx = renderTemplate('clocktower.conf.template', {
  '{{LOCATION}}': locationPath,
  // 精确匹配用的值：子路径部署要去掉末尾斜杠（`/clocktower`），否则 301 会拼出 `//` 而无限重定向。
  '{{LOCATION_EXACT}}': isRootPath ? '/' : options.prefix.slice(0, -1),
  // 子路径部署时 proxy_pass 末尾的 / 负责"去前缀"；根路径部署必须为空，否则会拼出 //hub/game。
  '{{PROXY_TAIL}}': isRootPath ? '' : '/',
  '{{PORT}}': String(options.port),
})
if (isRootPath) {
  // 那条 301 只服务于"子路径少了末尾斜杠"，根路径部署不需要。
  nginx = nginx.replace(/# BEGIN root-path-only[\s\S]*?# END root-path-only\n?/, '')
}

writeUnixText(path.join(options.outDir, 'clocktower.conf'), nginx)

// ---- 结果 ----
const sizeMb = (statSync(tarball).size / 1024 / 1024).toFixed(1)
console.log('')
console.log(`完成。产物在 ${options.outDir} ：`)
console.log(`  oct-linux.tar.gz    ${sizeMb} MB —— 传到目标机器`)
console.log('  clocktower.service  → /etc/systemd/system/')
console.log('  clocktower.conf     → /etc/nginx/conf.d/')
console.log('')
console.log('在目标机器上（详见 docs/operations/deploy.md §3）：')
console.log(`  mkdir -p ${options.appDir}/data`)
// 前端产物带内容哈希：每次构建换文件名，tar 覆盖式解压不会删旧文件——
// 不清就会每部署一次多留一份（实测积过 12 份 js/css）。
console.log(`  rm -f ${options.appDir}/wwwroot/assets/*`)
console.log(`  tar -xzf oct-linux.tar.gz -C ${options.appDir}`)
// 归档里的权限位来自构建机（Windows 上常落成 666 / 777），收敛一次。
// `X` 只对目录与本来就可执行的文件生效，新解出来的程序还没有执行位，所以要单独再给一次。
console.log(`  chmod -R u=rwX,go=rX ${options.appDir}`)
console.log(`  chmod u+x ${options.appDir}/OpenClockTower.Server`)
console.log('  # 放好两份配置 → systemctl daemon-reload && systemctl enable --now clocktower')
console.log('  # nginx -t && systemctl reload nginx')
console.log('')
console.log(`对外地址：http://<你的域名或IP>${options.prefix}`)
