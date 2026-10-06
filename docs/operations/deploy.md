# 部署：一台 Linux 机器 + nginx（可挂在任意子路径）

> **目的**：把这套东西放到一台公网 Linux 机器上，让别人用浏览器就能一起玩。
> 本页是**通用说明**：凡是你自己机器上的具体值，都用占位符表示，按 §0 代入即可，
> 不需要改任何代码。机器地址、绝对路径这类本机信息不进仓库（见 `AGENTS.md`）。

## 0. 先定四个值

| 占位符 | 含义 | 示例 |
|---|---|---|
| `<PREFIX>` | 对外子路径（末尾带 `/`）；想直接挂在根路径就填 `/` | `/clocktower/` |
| `<APP_DIR>` | 应用目录（程序与数据库都放这里） | 自定，如 `/srv/<名字>` |
| `<PORT>` | 宿主监听的本机端口（只对本机开放） | `5080` |
| `<SEATS>` | 席位数（说书人面板据此渲染，前端启动时读它） | `7` |

下文出现的这四个占位符，全部替换成你的值即可。

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
sudo mkdir -p <APP_DIR>/data
sudo tar -xzf oct-linux.tar.gz -C <APP_DIR>
# 归档里的权限位来自构建机（Windows 上常落成 666 / 777），收敛一次。
# 注意 `X` 只对目录与**本来就可执行**的文件生效——新解出来的程序还没有执行位，
# 所以必须再单独给入口程序加，否则 systemd 报 status=203/EXEC（起不来）。
sudo chmod -R u=rwX,go=rX <APP_DIR>
sudo chmod u+x <APP_DIR>/OpenClockTower.Server
```

systemd 单元 `/etc/systemd/system/clocktower.service`：

```ini
[Unit]
Description=OpenClockTower
After=network.target

[Service]
Type=simple
# 必须是应用目录：宿主的页面目录（wwwroot）跟着工作目录走，写错会变成"接口能用、页面 404"
WorkingDirectory=<APP_DIR>
ExecStart=<APP_DIR>/OpenClockTower.Server
Restart=always
RestartSec=3
User=<运行用户>
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
# 反代不在同一台机器 / 在容器里时，**必须**写出它的地址或网段，否则 X-Forwarded-* 一律不认（真实 IP 会退化成代理地址）：
# Environment=GameServer__TrustedProxies=172.18.0.0/16                 # 地址或 CIDR，逗号分隔；写错宿主启动即失败

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now clocktower
systemctl is-active clocktower && journalctl -u clocktower -n 20 --no-pager
```

启动时会打印一行读数（上限、可信代理、限速阈值），出问题时先看它——**配置真的生效了没有，看这一行**：

```
传输面：请求体≤262144B · SignalR消息≤65536B · 连接≤512 · 请求头超时=15s · 可信代理=回环（默认） · 登录限速=5次/300s
```

启动日志里会打印**说书人票据**与**各席位票据**（见 §5），把它记下来。

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

整局状态就是一个 SQLite 文件，冷备即可：

```bash
systemctl stop clocktower
cp <APP_DIR>/data/oct.db <APP_DIR>/data/backup-$(date +%F-%H%M).db
systemctl start clocktower
```

恢复 = 把备份文件放回 `oct.db` 再启动。

## 7. 更新版本

```bash
systemctl stop clocktower
# 前端产物带内容哈希：每次构建换文件名，而 tar 覆盖式解压**不会删掉旧文件**。
# 不清就会每部署一次多留一份（实测：几天下来积了 12 份 js/css），所以先清空这一步。
rm -f <APP_DIR>/wwwroot/assets/*
tar -xzf oct-linux.tar.gz -C <APP_DIR>        # 覆盖程序；data/ 不随包发布，不受影响
chmod -R u=rwX,go=rX <APP_DIR>                # 归档权限来自构建机，收敛一次
chmod u+x <APP_DIR>/OpenClockTower.Server     # 入口程序的执行位（丢了会 203/EXEC）
systemctl start clocktower
journalctl -u clocktower -n 20 --no-pager     # 确认起来了、库还是原来那一个
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

## 9. 已知限制（不藏）

1. **走 HTTP 时口令与会话凭据明文传输**：链路上能拿到可直接冒充的身份材料，适合私有网络 / 白名单内的短时使用。
   对外发布**必须先上 HTTPS**（§4.1 有完整步骤）；上了之后 HSTS 与其余安全头会自动生效，不需要改 nginx 的头。
   审计口径：这条对应 `G-A3-1`（Critical），**在 TLS 落地前一直算未清零**，不许当成"已经加固过了"。
2. **单进程多桌**（D-0024）：一个宿主按 `GameId` 维护多张桌，共享账号与连接设施；同时开几桌不需要多开进程。
   代价是 SQLite 单写者——多桌同时写入会排队（小圈子 2–5 桌可接受）。
3. **自助开桌没有配额**：默认谁都能开桌（D-0026），当前没有桌数上限、也没有空闲桌自动回收。
   公开部署请配 `GameServer__AllowPlayerTables=false` 收紧，并定期清理不开的桌。
4. **表结构变更需换新库**：当前用 EF 的 `EnsureCreated`，缺表时启动会显式失败并提示换新库，不会静默丢数据。
   唯一的例外是启动守卫对 `Games` 表的**原地列对账**（缺列补上、退场列清掉），所以"票据时代"的旧库能直接升上来。
5. **限速只在进程内存里**（D-0032）：重启即清零；多实例部署时**每实例各算一份**（首版明确不做多实例）。
   同一 NAT 后面的所有人共用"每 5 分钟 20 次登录失败"的额度——这是防爆破与不误伤之间的取舍，可用
   `GameServer__Throttle__*` 调。
6. **限速不覆盖界面动作**：开桌 / 入座 / 重复加入仍无配额（审计 `G-A5-2` / `G-A5-5` / `G-A5-6`，排在 M4）。
7. **反代上限与应用上限要人肉保持一致**（§4 的 `client_max_body_size 1m` 对应用侧 256 KB）：
   门禁只保证两边都有显式值，数值本身不一致时不会变红。

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
