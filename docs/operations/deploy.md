# 部署：一台 Linux 机器 + nginx（可挂在任意子路径）

> **目的**：把这套东西放到一台公网 Linux 机器上，让别人用浏览器就能一起玩。
> 本页是**通用说明**：凡是你自己机器上的具体值，都用占位符表示，按 §0 代入即可，
> 不需要改任何代码。机器地址、绝对路径这类本机信息不进仓库（见 `AGENTS.md`）。

## 0. 先定这几个值

| 占位符 | 含义 | 示例 |
|---|---|---|
| `<PREFIX>` | 对外子路径（末尾带 `/`）；想直接挂在根路径就填 `/` | `/clocktower/` |
| `<APP_DIR>` | 应用目录（程序与数据库都放这里） | 自定，如 `/srv/<名字>` |
| `<PORT>` | 宿主监听的本机端口（只对本机开放） | `5080` |
| `<SEATS>` | 席位数（说书人面板据此渲染，前端启动时读它） | `7` |
| `<运行用户>` | 服务进程的专用用户（M5 / G-A7-4，不再是 root） | `clocktower` |
| `<备份目录>` | 备份落地的目录（**建议另一块盘**，见 §6.2） | 自定，如 `/var/backups/<名字>` |

下文出现的这些占位符，全部替换成你的值即可；`deploy-prepare.mjs` 会把它们直接填进生成的配置里。

## 1. 形态

| 项 | 形态 |
|---|---|
| 机器要求 | x86_64 Linux，glibc ≥ 2.35（.NET 10 自包含包的要求） |
| 运行时 | **不需要在机器上装 .NET**——发布包自带运行时（自包含） |
| 入口 | nginx 监听 80，把子路径**去前缀**后转给宿主 |
| 数据库 | `<APP_DIR>/data/oct.db`（SQLite，整局状态都在这一个文件里） |
| 服务 | systemd 单元（开机自启 + 崩溃自动重拉） |

宿主**只监听 `127.0.0.1:<PORT>`，不直接对外**；外部一律经 nginx 进入。这样日后加 HTTPS、
限流、换端口都只动 nginx 一处。

## 2. 打包（在开发机上，需要 .NET SDK 与 Node）

**推荐用脚本**——按你给的值生成发布包与两份配置，并提前拦住"前缀与产物不一致"这类错：

```bash
node tools/deploy-prepare.mjs --app-dir <APP_DIR> --prefix <PREFIX> --port <PORT> --seats <SEATS>
# 产物：artifacts/deploy/{oct-linux.tar.gz, clocktower.service, clocktower.conf}
```

它内部做的就是下面两步（想手工做也可以）：

```bash
VITE_BASE_PATH=<PREFIX> npm run --prefix web build      # 1) 前端：把子路径编进产物
dotnet publish src/OpenClockTower.Server \
  -c Release -r linux-x64 --self-contained true -o /tmp/oct-publish
tar -czf oct-linux.tar.gz -C /tmp/oct-publish .          # 2) 自包含包（约 50MB）
```

**前缀为什么必须在构建期带上**：`index.html` 里引用资源用的是绝对路径，浏览器会照它去请求。
前端带上 `<PREFIX>` 之后，资源（`<PREFIX>assets/…`）、Hub（`<PREFIX>hub/game`）、健康检查
（`<PREFIX>healthz`）三者一致，nginx 只需要一条 location。

`VITE_BASE_PATH` 缺省为 `/`：此时产物与"挂在站点根"的历史形态一致，**换部署路径只改这一个变量**。

## 3. 安装（在目标机器上）

```bash
# 1) 专用运行用户：服务进程不再是 root，库文件也不再对同机所有人可读（M5 / G-A7-4 · G-A6-6）。
sudo useradd --system --no-create-home --shell /usr/sbin/nologin <运行用户>
# 2) 两个目录：data/ 放库（只在应用目录里），备份目录放备份（**建议放另一块盘**，见 §6）
sudo mkdir -p <APP_DIR>/data <备份目录>
sudo tar -xzf oct-linux.tar.gz -C <APP_DIR>
# 归档里的权限位来自构建机（Windows 上常落成 666 / 777），收敛一次。
# 注意 `X` 只对目录与**本来就可执行**的文件生效——新解出来的程序还没有执行位，
# 所以必须再单独给一次，而且是给**运行用户**可执行（755）：只加属主那一位会得到 744，
# 进程是 <运行用户> 而属主是 root，systemd 报 status=203/EXEC。
sudo chmod -R u=rwX,go=rX <APP_DIR>
sudo chmod 755 <APP_DIR>/OpenClockTower.Server
# 3) 属主与权限：程序文件 root 持有、运行用户只读；data/ 与备份目录归运行用户，且不对同机其它用户开放。
#    库文件里是**全部口令哈希与整局事件流**，644 等于同机任何用户都能读走（审计 G-A6-6 的读数）。
sudo chown -R root:<运行用户> <APP_DIR>
sudo chown -R <运行用户>:<运行用户> <APP_DIR>/data <备份目录>
sudo chmod 700 <APP_DIR>/data <备份目录>
sudo chmod 600 <APP_DIR>/data/oct.db        # 老库升级上来时做一次；新库由单元的 UMask=0077 直接建成 600
```

> 运行用户不存在时 systemd 会以 `status=217/USER` 拒绝启动——先建用户，再 `enable`。
> 升级已有部署（从"以 root 运行、库 644"的老形态过来）时，上面第 1、3 步照做一遍即可，
> `data/` 里的库不会被动到内容；**别忘了入口程序的执行位**——旧形态以 root 运行，`chmod u+x`（744）够用，
> 换成专用用户之后它会在 systemd 里表现成 `status=203/EXEC`（"服务起不来"却看不出是权限问题，真机踩过）。

systemd 单元 `/etc/systemd/system/clocktower.service`（**推荐直接用生成的**：`artifacts/deploy/clocktower.service`
已把路径 / 端口 / 席位数 / 运行用户填好；下面这段是说明版）：

```ini
[Unit]
Description=OpenClockTower
After=network.target

[Service]
Type=simple
# 专用运行用户（M5 / G-A7-4）：进程不再是 root。用户要先建出来（见本节开头）。
User=<运行用户>
Group=<运行用户>
# 新写入的文件只有属主可读（M5 / G-A6-6）：库文件与它的 -wal / -shm 都在这个目录下。
UMask=0077
# 必须是应用目录：宿主的页面目录（wwwroot）跟着工作目录走，写错会变成"接口能用、页面 404"
WorkingDirectory=<APP_DIR>
ExecStart=<APP_DIR>/OpenClockTower.Server
Restart=always
RestartSec=3
# 最小权限：不提权、看不到别人的家目录、系统目录只读、临时目录私有。
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=true
ProtectKernelTunables=true
ProtectControlGroups=true
RestrictSUIDSGID=true
Environment=DOTNET_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:<PORT>
Environment=GameServer__DatabasePath=<APP_DIR>/data/oct.db
Environment=GameServer__SeatCount=<SEATS>
Environment=GameServer__SlotQuotaSeconds=10
# 谁能开桌（D-0026）：不配 = 放开，任何登录账号都能开一桌自己主持（小圈子自用）。
# 公开部署怕被刷桌时改成 false，此时只有 GameServer__AdminUsernames 里的运维身份能开
# ——**两行要一起放开**：只改上面那行不配名单，就没有任何账号能开新桌了（启动日志会告警）。
# Environment=GameServer__AllowPlayerTables=false
# Environment=GameServer__AdminUsernames=<运维登录名>[,<再来一个>]   # 逗号分隔或索引式都认

# 传输面与风控（M3 / D-0031 / D-0032）：**都有默认值，不配也能跑**；列在这里是因为它们是可以调的旋钮。
# 传输面上限（应用侧；反代侧还有一层同类上限，见 §4）：
# Environment=GameServer__Transport__MaxRequestBodyBytes=262144        # 默认 256 KB（框架默认 30 MB）
# Environment=GameServer__Transport__MaxSignalRMessageBytes=65536      # 默认 64 KB（框架默认 32 KB）
# Environment=GameServer__Transport__MaxConcurrentConnections=512      # 默认 512（框架默认不限）
# Environment=GameServer__Transport__RequestHeadersTimeoutSeconds=15   # 默认 15 s（框架默认 30 s）
# Environment=GameServer__Transport__KeepAliveTimeoutSeconds=60        # 默认 60 s（框架默认 130 s）
# 账号入口限速（同一来源 + 同一登录名 的失败计数）：
# Environment=GameServer__Throttle__WindowSeconds=300                  # 默认 5 分钟
# Environment=GameServer__Throttle__LoginFailuresPerUsername=5         # 默认 5 次
# Environment=GameServer__Throttle__LoginFailuresPerClient=20          # 默认 20 次（跨登录名；NAT 后面的集体额度）
# Environment=GameServer__Throttle__RegisterCallsPerClient=10          # 默认 10 次（每次调用都算、成功不清零）
# Environment=GameServer__Throttle__RegisterCallsGlobal=30             # 默认 30 次（**整个部署**的注册额度；换 IP 刷注册只有它拦得住）
# 滥用与风控（M4 / D-0033）：
# Environment=GameServer__AllowSelfRegistration=false                  # 默认 true；**关掉之后没有任何入口能开新账号**（首版没有邀请码与管理台）
# Environment=GameServer__TableQuota__MaxTablesPerAccount=12           # 默认 12 张（单个账号名下**在册**的桌；首版不回收，所以这是硬上限）
# Environment=GameServer__TableQuota__MaxTablesGlobal=64               # 默认 64 张（整个部署在册的桌；到顶后谁都开不出新桌，清理见 §9.4）
# Environment=GameServer__ActionThrottle__WindowSeconds=300            # 默认 5 分钟
# Environment=GameServer__ActionThrottle__JoinCallsPerClientAndGame=60 # 默认 60 次（同一来源在同一桌的入座 / 重连）
# Environment=GameServer__ActionThrottle__JoinCallsPerClient=120       # 默认 120 次（同一来源跨桌）
# Environment=GameServer__ActionThrottle__WriteTextCallsPerActor=120   # 默认 120 次（注记 / 说明 / 原因，按来源 × 桌 × 身份）
# 数据库口径（M5 / D-0034）：日志模式（wal）与同步级别（FULL）写死在代码里，只有"等锁等多久"可调。
# 同机用 sqlite3 手工改库时会希望它长一点；调小到 0 等于"一撞锁就失败"。
# Environment=GameServer__Sqlite__BusyTimeoutMilliseconds=5000         # 默认 5000
# 反代不在同一台机器 / 在容器里时，**必须**写出它的地址或网段，否则 X-Forwarded-* 一律不认（真实 IP 会退化成代理地址）：
# Environment=GameServer__TrustedProxies=172.18.0.0/16                 # 地址或 CIDR，逗号分隔；写错宿主启动即失败

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now clocktower
systemctl is-active clocktower && journalctl -u clocktower -n 20 --no-pager
```

### 3.1 备份定时器（推荐一起装）

生成的 `clocktower-backup.service` + `clocktower-backup.timer` 放进 `/etc/systemd/system/`，然后：

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now clocktower-backup.timer
systemctl list-timers clocktower-backup.timer --no-pager     # 下一次什么时候跑
sudo systemctl start clocktower-backup.service               # 立刻跑一次（不等整点）
journalctl -u clocktower-backup -n 20 --no-pager             # 这一次的读数
```

它跑的就是 §6.1 那条命令（`backup --db … --out … --keep …`），
默认每天一次、保留 7 份、停机错过的下一次开机补跑。备份**不停服**。

### 3.2 启动读数

启动时会打印三行读数（传输上限 / 可信代理 / 账号限速；风控配额与频率；数据库口径），出问题时先看它们——
**配置真的生效了没有，看这三行**：

```
传输面：请求体≤262144B · SignalR消息≤65536B · 连接≤512 · 请求头超时=15s · 可信代理=回环（默认） · 登录限速=5次/300s
风控面：自助注册=开 · 注册额度=每来源10次/全局30次每300s · 在册桌上限=每账号12张/全局64张 · 入座额度=每桌60次每300s · 写文本额度=每身份120次每300s
数据库口径：库=<APP_DIR>/data/oct.db · 大小=…B · 日志模式=wal · 同步级别=FULL · 写锁等待=5000ms
```

第三行是 M5 新加的（G-A6-8）：**日志模式必须是 `wal`**，不再是默认的 `delete`。
它不是"设过了就算"——那是读回实际生效的值；只读文件系统或库被别的连接独占时，
`PRAGMA journal_mode=wal` 会静默不生效，这一行会变成 `实际 delete` 外加一条告警（§8 有排查行）。

## 4. nginx

**推荐直接用生成的配置**：`node tools/deploy-prepare.mjs --app-dir <APP_DIR> --prefix <前缀> --port <端口>` 会产出
`artifacts/deploy/clocktower.conf`（前缀 / 端口 / 长连接头 / 上限 / 超时都已填好），把它放到
`/etc/nginx/conf.d/` 即可。下面这段是同一份配置的**说明版**（手写时至少要有的东西）：

```nginx
# 长连接需要：有 Upgrade 头就透传 upgrade，否则 close（缺了这两行 SignalR 会连不上）
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

# 上限与限流的状态桶（按客户端地址；声明在 server 外面）
limit_req_zone $binary_remote_addr zone=clocktower_req:10m rate=30r/s;
limit_conn_zone $binary_remote_addr zone=clocktower_conn:10m;

server {
    listen 80;
    server_name <你的域名或IP>;
    server_tokens off;                 # 不白送 nginx 版本号
    client_max_body_size 1m;           # 应用侧是 256 KB，这里留一层余量
    client_header_timeout 15s;
    client_body_timeout 15s;
    send_timeout 30s;
    limit_req zone=clocktower_req burst=100 nodelay;
    limit_req_status 429;
    limit_conn clocktower_conn 32;     # 7 席 × 2 条连接（账号 + 对局）的量级留一倍余量
    limit_conn_status 429;

    # 少了末尾斜杠会与下面的 location 前缀不匹配，于是 /clocktower 本身 404
    location = /clocktower {
        return 301 /clocktower/;
    }

    # SignalR 长连接：只有这一段需要"整局不被掐断"的读超时
    location /clocktower/hub/ {
        proxy_pass http://127.0.0.1:<PORT>/hub/;   # location 已吃掉前缀，这里写死 /hub/ 对两种部署都对
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        # 用 $proxy_add_x_forwarded_for（追加在末尾），应用只取最后一段——客户端自带的伪造值会被丢掉
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
        proxy_buffering off;
    }

    location /clocktower/ {
        proxy_pass http://127.0.0.1:<PORT>/;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_connect_timeout 5s;
        proxy_read_timeout 60s;        # 页面与静态资源不需要一小时
        proxy_send_timeout 60s;
    }
}
```

**安全响应头不要在 nginx 里加**：`Strict-Transport-Security`（仅 HTTPS 时）/ `Content-Security-Policy` /
`X-Content-Type-Options` / `Referrer-Policy` / `Permissions-Policy` / `X-Frame-Options` 与静态资源的
`Cache-Control` 全部由应用统一发（`ResponseHeadersMiddleware`）。两处都写必然漂移，而且出问题时
看不出"少了哪一个、是谁少的"。HSTS 由应用按请求协议自行决定：HTTPS 上发、明文上不发。

想挂在**站点根**（`<PREFIX>` = `/`）时，把 `location /clocktower/` 改成 `location /`、
`location /clocktower/hub/` 改成 `location /hub/`、`proxy_pass` 保持 `http://127.0.0.1:<PORT>;`
（**末尾不要斜杠**，此时无需去前缀），并删掉那条 301。

改完执行：

```bash
sudo nginx -t && sudo systemctl reload nginx
```

### 4.1 上 HTTPS（给"别人的部署"用的路径）

本仓库的默认形态是同机 nginx + 明文（§9 第 1 条写清了代价）。要对外发布就换成 HTTPS，两处改动：

```bash
# 1) 证书（自己的域名 + Let's Encrypt；http-01 需要 80 端口对外开放）
sudo certbot --nginx -d <你的域名>
```

```nginx
# 2) 把 §4 的 server 拆成两个：443 带证书，80 只做跳转（location 块整段搬过去，限流一起搬）
server {
    listen 443 ssl;
    listen [::]:443 ssl;
    http2 on;
    server_name <你的域名>;
    ssl_certificate     /etc/letsencrypt/live/<你的域名>/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/<你的域名>/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    # …§4 的 location 块…（含 limit_req / limit_conn / server_tokens off）
}

server {
    listen 80;
    server_name <你的域名>;
    location /.well-known/acme-challenge/ { root <ACME挑战目录>; }   # 续期用（certbot 的默认 webroot 就是它）
    location / { return 301 https://$host$request_uri; }
}
```

上完以后**不需要**在 nginx 加任何安全响应头：`X-Forwarded-Proto` 已透传，应用会按 HTTPS 处理，
HSTS 自动出现在响应里（`max-age=30 天`，不带 `includeSubDomains`）。复核办法：

```bash
curl -I http://<域名>/<前缀>/      # 期望 301 到 https
curl -I https://<域名>/<前缀>/     # 期望 200，且带 strict-transport-security / content-security-policy 等
node tools/verify-transport-hardening.mjs --base-url https://<域名>/<前缀>/
```

## 5. 入口与账号

四个入口（同一个页面，靠**地址路径**分面；旧井号写法仍然能打开，会自动跳到新地址）：

| 入口 | 地址 | 干什么 |
|---|---|---|
| 首页 | `<PREFIX>`（也认 `<PREFIX>home`） | 打开站点先看到"这是什么 + 两个方向" |
| 加入一桌 | `<PREFIX>play`（旧写法 `#player` 仍可用） | 注册 / 登录，从在开的桌里挑空席位入座 |
| 主持一局 | `<PREFIX>storyteller` | 登录后开一桌；「我主持的桌」里点进主持台 |

**路径分面不需要额外配置**：宿主对"不像文件的路径"回退到同一份 `index.html`（`Program.cs` 末尾的
SPA 回退），nginx 那条 `location <PREFIX>` 也照常转发——换地址形状**不用改 nginx**。
点站内链接由前端接管（`pushState`，不重载文档），所以切面不会掉登录态。

**账号是唯一的身份证**（D-0027）：没登录时每个面只有一张登录 / 注册卡；
**谁开桌，这一桌就归谁**，进主持台只认那个账号——换设备、清缓存，登录同一账号桌还在。

- **没有"默认桌"**：装完打开站点就是**空大厅**，第一桌由人在界面上开出来（旧版升级上来的库里
  可能还留着过去那张 `default` 桌，见 §7 的说明）。
- **不再有说书人票据**：那串要抄的凭据已整个退场（协议里也没有了）。
- 谁都能开一桌（D-0026）；要收紧就按 §3 配 `GameServer__AllowPlayerTables=false`。
- **没账号的人今天在界面上没有入座入口**：席位票据本身还在协议里，登录后有一处
  「有邀请码？」兜底（给"这一桌已开局、大厅点不动"的场合，比如中途到场的旅行者）；
  游客要不要一个入口，是**尚未拍板的产品决定**（见 `docs/backlog/todo/seat-ticket-entrance.md`）。

## 6. 备份与恢复

整局状态就是一个 SQLite 文件，但**不要用"停服 + cp"当备份方案**（审计 G-A6-4 的原始读数就是那三行）：
停服会真的把玩家踢下线，于是"该备份了"永远排在"这局还没打完"后面。
本版自带热备份命令，服务照常跑着备。

### 6.1 热备份（不停服，推荐走定时器）

```bash
# 手工跑一次（身份要对：备份文件与库文件同权限，见 §3）
sudo -u <运行用户> <APP_DIR>/OpenClockTower.Server backup \
  --db <APP_DIR>/data/oct.db --out <备份目录> --keep 7
```

一次运行做四件事，读数直接打在 journal 里：

| 步骤 | 说明 |
|---|---|
| ① 在线备份 | 走 SQLite 的在线备份 API（`sqlite3_backup`），**服务不用停**，一致性由 SQLite 保证 |
| ② 收成单文件 | 备份文件是普通回滚日志模式、没有 `-wal` / `-shm` 伴生文件——任何工具都能直接读它 |
| ③ 当场体检 | `integrity_check` + 逐局事件条数 + 整文件 SHA-256；**没过就删掉这份坏备份并报错** |
| ④ 轮转 | 只保留最近 `--keep` 份，**只删本工具产出的 `oct-*.db`**，目录里别的东西一律不碰 |

文件名是 `oct-<UTC 时刻>.db`（形如 `oct-20261006T031500Z.db`），按名字排序即按时间排序；
同一秒内备两次不会覆盖，会追加 `-1`。

定时：`clocktower-backup.timer`（§3.1，默认每天一次 + 停机补跑）。想改时间就改单元的
`OnCalendar=`（`systemd-analyze calendar daily` 可以验证表达式），改完 `daemon-reload`。

### 6.2 备份要离开这台机器

**与库同盘的备份不算备份**：磁盘坏掉、误删目录、机器被回收，三种情况一起没。
备份目录应当**在另一块盘**（`--out <另一块盘的目录>`），并定期往外拉一份：

```bash
# 从运维机上拉走（单向）
rsync -av --delete <运行用户>@<机器>:<备份目录>/ ./clocktower-backups/
```

拉走之后**核对 SHA-256**：`backup` 每次都会打印它，落到 journal 里就是比对依据。

### 6.3 恢复

```bash
sudo systemctl stop clocktower
# 先把现在这个库留一份（万一恢复错了还能回来）——注意它现在归运行用户所有
sudo cp -a <APP_DIR>/data/oct.db <APP_DIR>/data/oct.db.before-restore
sudo -u <运行用户> <APP_DIR>/OpenClockTower.Server db-report --db <备份目录>/oct-<时刻>.db   # 先确认这份备份是好的
sudo cp -a <备份目录>/oct-<时刻>.db <APP_DIR>/data/oct.db
sudo chown <运行用户>:<运行用户> <APP_DIR>/data/oct.db && sudo chmod 600 <APP_DIR>/data/oct.db
sudo systemctl start clocktower
journalctl -u clocktower -n 20 --no-pager     # 看"数据库口径"与"在册的桌"
```

**恢复出来的是"备份那一刻"的状态**：那之后注册的账号、开的桌、打完的事件都不在里面。
恢复是**有损**操作，别拿它当"撤销键"。

### 6.4 恢复演练（没演练过的备份不算备份）

仓库里带了演练脚本，它在**临时目录**里恢复、起一个**临时实例**、读完就收：
**全程不碰正在服务的库**（这一点由脚本结构保证：它只往 `mktemp -d` 出来的目录里写）。

```bash
# 在目标机器上（用能读备份文件的身份；服务自己的用户通常就行）
sudo -u <运行用户> bash tools/deploy/restore-drill.sh \
  --app-dir <APP_DIR> --backup <备份目录>/oct-<时刻>.db --port 5199
```

读数四步：① `db-report` 的完整性 + 逐局事件条数 → ② 临时实例 `/healthz` 返回 200 →
③ 临时实例启动日志里的"数据库口径 / 在册的桌" → ④ 收尾（临时实例停掉、临时目录删掉）。
**换机器部署、升级完、以及每季度，各跑一次**；结论要落纸（谁、什么时候、哪一份备份、事件条数是多少）。

### 6.5 关于 WAL 与"为什么不用停服"（口径）

| 项 | 值 | 谁定的 |
|---|---|---|
| `journal_mode` | `wal`（库文件里的持久属性，启动时设一次并**读回**） | `SqliteConnectionPragmas`（D-0034） |
| `synchronous` | `FULL`（连接级，每个连接都设） | 同上 |
| `busy_timeout` | 5000 ms（连接级，可配 `GameServer__Sqlite__BusyTimeoutMilliseconds`） | `SqliteOptions` |

- **WAL** 让读不挡写、写不挡读：玩家读复盘不会卡住正在写入的事件；崩溃后由 WAL 自动前滚。
- **`synchronous=FULL`** 是有意用吞吐换正确性：`NORMAL` 更快，但掉电可能丢掉最后几笔**已提交**的事务，
  而事件流就是"这一局发生过什么"的唯一记录。
- **热备份为什么安全**：在线备份 API 在源库上开一个读事务、边读边往目标文件写，拿到的是一致快照；
  WAL 下也不受影响（`VACUUM INTO` / `.backup` 是同一类做法，本命令走的是前者那套 API）。
- **`busy_timeout`** 管的是"撞上写锁先排队"：审计读数里那个 `busy_timeout = 0` 其实是
  `sqlite3` 命令行**自己那条连接**的读数，不是应用的；本版把它显式化成配置，启动日志里能读到。
- 单进程多桌的写入仍然串行（SQLite 单写者），这条限制没变（§9 第 2 条）。

## 7. 更新版本

**升级前先备一份**（几秒钟的事，回滚要用它）：

```bash
sudo -u <运行用户> <APP_DIR>/OpenClockTower.Server backup --db <APP_DIR>/data/oct.db --out <备份目录> --keep 7
sudo systemctl stop clocktower
# 前端产物带内容哈希：每次构建换文件名，而 tar 覆盖式解压**不会删掉旧文件**。
# 不清就会每部署一次多留一份（实测：几天下来积了 12 份 js/css），所以先清空这一步。
rm -f <APP_DIR>/wwwroot/assets/*
tar -xzf oct-linux.tar.gz -C <APP_DIR>        # 覆盖程序；data/ 不随包发布，不受影响
chmod -R u=rwX,go=rX <APP_DIR>                # 归档权限来自构建机，收敛一次
chmod 755 <APP_DIR>/OpenClockTower.Server     # 入口程序的执行位要给运行用户（丢了会 203/EXEC）
chown -R root:<运行用户> <APP_DIR>            # 程序文件归 root、运行用户只读（§3）
chown -R <运行用户>:<运行用户> <APP_DIR>/data && chmod 700 <APP_DIR>/data
sudo systemctl start clocktower
journalctl -u clocktower -n 20 --no-pager     # 确认起来了、库还是原来那一个、日志模式=wal
```

`data/` 不在包里，**升级不会动你的对局数据**。反过来：换新库（删掉 `oct.db` 重启）等于
**开新的一局**。

**从"有默认桌"的旧版升级上来时**（D-0027 之后）：库里过去那张 `default` 桌会**补列成"没有房主"**
——它照旧出现在大厅里，但谁也进不去它的主持台（没有归属就没有说书人）。这是如实反映
"升级前那一桌本来就没有开桌账号"，不是故障。要清掉它就按下面的 SQL 删（**先停服务再删**：
开着的桌活在宿主内存里，不停服务就删会被写回来）：

```bash
systemctl stop clocktower
sqlite3 <APP_DIR>/data/oct.db "DELETE FROM SeatBindings WHERE GameId='default'; \
  DELETE FROM Events WHERE GameId='default'; DELETE FROM Snapshots WHERE GameId='default'; \
  DELETE FROM Receipts WHERE GameId='default'; DELETE FROM Games WHERE GameId='default';"
systemctl start clocktower
```

**旧库里的 `StorytellerTicket` 列会在启动时被清掉**（说书人票据时代的凭据，D-0027）：本版不再映射它，
而它是 `NOT NULL` 且**没有默认值**——留着会让**开新桌**的写入被 SQLite 拒掉
（`NOT NULL constraint failed: Games.StorytellerTicket`，在界面上只表现为"开桌失败"）。
`ALTER TABLE ... DROP COLUMN` 是原地操作，不动别列数据、也不动你原来那一桌；
启动日志里会有一行 `旧库删列（退场凭据）：Games.StorytellerTicket`。**不需要你做任何事**。

## 8. 排查

| 现象 | 先看什么 |
|---|---|
| 访问根路径或错误路径 404 | 这是正常的：应用只挂在 `<PREFIX>` 下 |
| 页面 404 | `systemctl is-active clocktower`；`ls <APP_DIR>/wwwroot`（必须有 index.html） |
| 页面 200 但空白 | 前端构建时 `VITE_BASE_PATH` 与 nginx 的 location 是否**同一个值** |
| 能打开但点「加入」没反应 | 同上；再看浏览器 Network 里 `/hub/**` 是否 404 |
| 加入后收不到推送 | nginx 的 `Upgrade` / `Connection` 两条头（§4） |
| 502 | 宿主没起来：`journalctl -u clocktower -n 50` |
| `plan.seat_unassigned` | 席位数量不匹配：`GameServer__SeatCount` 要 ≥ 实际入座人数 |
| 登录提示"尝试过于频繁，请 N 秒后再试" | 入口限速生效了（D-0032）：同一来源 + 同一登录名的失败额度用尽。阈值见 §3 的 `GameServer__Throttle__*` |
| 请求返 `413` | 请求体超限：应用侧 256 KB / 反代 1 MB（§3 / §4），看 `Server` 头判是哪一层拒的 |
| 请求返 `429` | 反代限流：`limit_req`（速率）或 `limit_conn`（并发连接），见 §4 |
| 提示"尝试过于频繁，请 N 秒后再试" | 入口 / 动作限速生效了（D-0032 / D-0033）：登录失败、注册额度、入座次数或写文本次数用尽。阈值见 §3 的 `GameServer__Throttle__*` 与 `GameServer__ActionThrottle__*` |
| 提示"本服当前不开放自助注册" | `GameServer__AllowSelfRegistration=false`（§3）：要开新账号就把它改回 true 并重启 |
| 提示"本服在册的桌已达上限" | 全局桌数到顶（§9.4）：清掉不再使用的桌，或调 `GameServer__TableQuota__MaxTablesGlobal` |
| 提示"你名下已经有 N 张在册的桌" | 单账号桌数到顶（§9.4）：清掉旧桌，或调 `GameServer__TableQuota__MaxTablesPerAccount` |
| `systemctl status` 报 `status=217/USER` | 运行用户不存在（§3 第 1 步）：`useradd --system` 建出来再 `enable` |
| 页面 500 / 库打不开 | `data/` 或 `oct.db` 的属主与权限不对（§3 第 3 步）：应归运行用户、`data/` 700、库 600 |
| 启动日志出现"日志模式没有生效"告警 | 库所在文件系统只读、或库被别的连接独占（§3.2 · §6.5）：这一行会给出实际模式 |
| 备份报"完整性检查未通过" | **源库可能已经坏了**：先 `db-report` 看源库；不要覆盖任何文件，按 §6.3 用更早的备份恢复 |
| 备份报"数据库不存在" | `--db` 路径写错，或这台机器还没起过服务（库是首次启动时建出来的） |
| `clocktower-backup.timer` 不跑 | `systemctl list-timers clocktower-backup.timer`；`journalctl -u clocktower-backup -n 50` |

## 9. 已知限制与运营口径

### 9.1 已知限制（不藏）

1. **走 HTTP 时口令与会话凭据明文传输**：链路上能拿到可直接冒充的身份材料，适合私有网络 / 白名单内的短时使用。
   对外发布**必须先上 HTTPS**（§4.1 有完整步骤）；上了之后 HSTS 与其余安全头会自动生效，不需要改 nginx 的头。
   审计口径：这条对应 `G-A3-1`（Critical），**在 TLS 落地前一直算未清零**，不许当成"已经加固过了"。
2. **单进程多桌**（D-0024）：一个宿主按 `GameId` 维护多张桌，共享账号与连接设施；同时开几桌不需要多开进程。
   代价是 SQLite 单写者——多桌同时写入会排队（小圈子 2–5 桌可接受）。
3. **开桌有配额、但没有回收**：默认谁都能开桌（D-0026），配额是单账号 12 张 / 全局 64 张（D-0033，可配）；
   首版**不自动回收空闲桌**，到顶后需要人工清理（§9.4）。公开部署怕被刷桌时仍建议配
   `GameServer__AllowPlayerTables=false` 收紧到运维名单。
4. **表结构变更需换新库**：当前用 EF 的 `EnsureCreated`，缺表时启动会显式失败并提示换新库，不会静默丢数据。
   唯一的例外是启动守卫对 `Games` 表的**原地列对账**（缺列补上、退场列清掉），所以"票据时代"的旧库能直接升上来。
5. **所有配额与限速都只在进程内存里**（D-0032 / D-0033）：重启即清零（**重启也会清掉攻击者的计数**）；
   多实例部署时**每实例各算一份**（首版明确不做多实例）。同一 NAT 后面的所有人共用额度
   （登录失败 20 次 / 注册 10 次 / 入座 60 次每 5 分钟）——这是防滥用与不误伤之间的取舍，可用
   `GameServer__Throttle__*` 与 `GameServer__ActionThrottle__*` 调。
6. **界面动作的限速在服务端生效、界面本身没有节流**：连点按钮会看到服务端的拒绝文案
   （"…请 N 秒后再试"），够用但不优雅；界面的按钮节流排在 M5 的体验收口。
   另：**重连包仍是每次全量重建**（G-A5-6 的缓存半边留 M5）——频率闸只堵住"循环调用"。
7. **反代上限与应用上限要人肉保持一致**（§4 的 `client_max_body_size 1m` 对应用侧 256 KB）：
   门禁只保证两边都有显式值，数值本身不一致时不会变红。
8. **库的备份是"定期 + 异地"，不是"实时"**：两次备份之间崩盘，那一段时间的对局会丢。
   默认每天备一次（§3.1）；打得勤的部署可以把 `OnCalendar=` 改成每小时（`hourly`）。
9. **单实例**：既没有迁移机制也没有单实例锁（G-A6-3，排在 M5 后面）；
   同机误起第二个实例仍然可能以"库结构不匹配"的样子失败（§9.3 有回滚口径）。

### 9.2 重启对正在进行的对局意味着什么

重启（`systemctl restart`、升级、崩溃后 `Restart=always` 拉起）会清掉**进程内存里的一切**：
账号会话、连接级凭据、席位绑定、限速计数桶、每局的会话缓存。库里的东西（桌、事件流、快照、账号）不受影响。

| 谁 | 重启后看到什么 |
|---|---|
| 玩家 / 说书人 | **要重新登录**（会话在内存里），然后重新入座；席位绑定在库里，所以登录后还能回到自己那一席 |
| 正在进行的对局 | 一局的状态是"快照 + 事件流"重建出来的，所以**局面不会丢**；但正在等待的那一次行动请求要重新推一次 |
| 已结束的局 | 复盘照常看得到（都在库里） |
| 限速计数 | 归零（D-0032 / D-0033 明说过的代价：**重启也会清掉攻击者的计数**） |

**所以：重启不需要"等这局打完"**，但要在群里说一声——玩家那边表现为"掉线了、要重登"。

### 9.3 回滚

回滚 = **旧程序包 + 备份里的库**，两样缺一不可（审计 G-A7-5 的原话：回滚不是"换包重启"）。

```bash
# 0) 手上有两样东西：上一版的 oct-linux.tar.gz（自己留档）与升级前那份备份（§7 第 0 步）
sudo systemctl stop clocktower
# 1) 先把当前库留一份，别让回滚变成单向门
sudo cp -a <APP_DIR>/data/oct.db <APP_DIR>/data/oct.db.rollback-source
# 2) 放回旧程序（与升级同一套步骤，只是用旧包）
rm -f <APP_DIR>/wwwroot/assets/* && tar -xzf oct-linux-<上一版>.tar.gz -C <APP_DIR>
sudo chown -R root:<运行用户> <APP_DIR> && sudo chmod 755 <APP_DIR>/OpenClockTower.Server
# 3) 放回旧库（旧程序不认识新库的新形状时，这一步是必须的）
sudo -u <运行用户> <APP_DIR>/OpenClockTower.Server db-report --db <备份目录>/oct-<升级前时刻>.db
sudo cp -a <备份目录>/oct-<升级前时刻>.db <APP_DIR>/data/oct.db
sudo chown <运行用户>:<运行用户> <APP_DIR>/data/oct.db && sudo chmod 600 <APP_DIR>/data/oct.db
sudo systemctl start clocktower
```

**哪些变更回不去**（发布清单要写明，§9.1 第 4 条那种"补列 / 删列"是本版唯一的原地动作）：

| 变更 | 能不能靠换包回滚 |
|---|---|
| 加表 / 加列（有默认值） | 能：旧程序不认新列，照常读写自己那几列 |
| **删列**（如 `DROP COLUMN StorytellerTicket`） | **不能**：旧程序要那一列，且它是 `NOT NULL` 无默认值——必须连同备份里的库一起回 |
| 改列类型 / 加约束 / 加索引 | 当前没有这类迁移；将来有了，同样要"包 + 库"一起回 |

### 9.4 清理不再使用的桌（首版没有关桌功能）

在册桌数是**有上限的**（默认单账号 12 张 / 全局 64 张，见 D-0033），而首版**不回收空闲桌**：
上限一到，谁都开不出新桌。清理就是**从库里删掉那些桌的行**，操作前先停服务
（开着的桌活在宿主内存里，不停服务就删会被写回来）：

```bash
sudo systemctl stop clocktower
# 先看一眼有哪些桌（桌名与开桌账号一眼能认出来）
sudo -u <运行用户> sqlite3 <APP_DIR>/data/oct.db "SELECT g.GameId, g.Name, u.Username FROM Games g LEFT JOIN Users u ON u.Id = g.CreatedByAccountId;"
# 确认 GameId 之后逐个删（<ID> 换成上面查到的那一个）：
sudo -u <运行用户> sqlite3 <APP_DIR>/data/oct.db "DELETE FROM SeatBindings WHERE GameId='<ID>'; DELETE FROM Events WHERE GameId='<ID>'; DELETE FROM Snapshots WHERE GameId='<ID>'; DELETE FROM Receipts WHERE GameId='<ID>'; DELETE FROM Games WHERE GameId='<ID>';"
sudo systemctl start clocktower
```

删桌会**一并删掉那一局的全部事件与席位绑定**（复盘也就没了）——只删确定不要的。
空闲桌回收（按时间自动归档）排在 M5 的后续批次；在那之前上面这套就是运营路径。
**删之前先备一份**（§6.1）：这套 SQL 不可逆。

## 依据

- **自包含发布**：目标机不必预装 .NET，少一层外部依赖；代价是包变大（约 50MB）。
- **前缀由前端构建期携带**（`VITE_BASE_PATH`）：资源、Hub、健康检查同源同前缀，nginx 只需一条 location；
  对比"nginx 去前缀 + 前端用根路径引用"的做法，后者会把 `/assets/`、`/hub/` 这类通用名字铺到站点根上，
  与同机其他服务抢路径。
- **只监听 `127.0.0.1`**：对外入口收敛到 nginx 一处，便于日后加 HTTPS 与限流。

## 相关阅读

- 架构与依赖方向：`docs/architecture/current.md`
- 开发时怎么跑（含前端与宿主分开启动）：`web/AGENTS.md` §2
- 验收装置怎么跑：`docs/acceptance/devices.md`
