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

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now clocktower
systemctl is-active clocktower && journalctl -u clocktower -n 20 --no-pager
```

启动日志里会打印**说书人票据**与**各席位票据**（见 §5），把它记下来。

## 4. nginx

子路径挂载（`<PREFIX>` 为 `/clocktower/` 时）：

```nginx
# 长连接需要：有 Upgrade 头就透传 upgrade，否则 close（缺了这两行 SignalR 会连不上）
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

server {
    listen 80;
    server_name <你的域名或IP>;

    # 少了末尾斜杠会与下面的 location 前缀不匹配，于是 /clocktower 本身 404
    location = /clocktower {
        return 301 /clocktower/;
    }

    location /clocktower/ {
        # 末尾的 `/` 是"去掉 /clocktower 前缀"的关键：/clocktower/hub/game → /hub/game
        proxy_pass http://127.0.0.1:<PORT>/;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_read_timeout 3600s;   # 对局中长连接不能中途被切断
        proxy_send_timeout 3600s;
        proxy_buffering off;
    }
}
```

想挂在**站点根**（`<PREFIX>` = `/`）时，把 `location /clocktower/` 改成 `location /`、
`proxy_pass` 保持 `http://127.0.0.1:<PORT>;`（**末尾不要斜杠**，此时无需去前缀），并删掉那条 301。

改完执行：

```bash
sudo nginx -t && sudo systemctl reload nginx
```

## 5. 入口与票据

三个入口（同一个页面，靠地址里的 hash 分面）：

| 入口 | 地址 | 干什么 |
|---|---|---|
| 首页 | `<PREFIX>#/home` | 看有哪些桌在开，挑一个入口 |
| 玩家端 | `<PREFIX>#/play`（旧写法 `#player` 仍可用） | 注册 / 登录，选空席位入座 |
| 说书人端 | `<PREFIX>#/storyteller`（**空 hash 也是它**） | 主持一局：开桌、配板、走夜晚 |

**说书人票据**（主机凭据，等同密码）：

- **默认桌的票据**在首次建局时生成，之后固定不变：`journalctl -u clocktower | grep 票据`
- **别人自己开的桌**不需要你去发票据：谁开桌，平台就把那一桌的票据回给谁，他直接进主持台。
  这也是说书人的口径——**说书人是玩这一局的角色，不是系统权限**，所以默认谁都能开一桌（D-0026）；
  要收紧就按 §3 配 `GameServer__AllowPlayerTables=false`。
- 席位票据仍然可用（邀请朋友 / 换设备兜底），但玩家注册登录后可以自己选空席位，不需要它。
- 票据是明文凭据：**走 HTTP 时链路上的人可以看到**。长期开建议加 HTTPS（§8）

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
**开新的一局**，票据全部重新生成。

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

## 9. 已知限制（不藏）

1. **走 HTTP 时票据与口令明文传输**：适合小圈子短时开；长期开请在同机 nginx 上加证书（`listen 443 ssl` + `X-Forwarded-Proto` 已透传）。
2. **单进程多桌**（D-0024）：一个宿主按 `GameId` 维护多张桌，共享账号与连接设施；同时开几桌不需要多开进程。
   代价是 SQLite 单写者——多桌同时写入会排队（小圈子 2–5 桌可接受）。
3. **自助开桌没有配额**：默认谁都能开桌（D-0026），当前没有桌数上限、也没有空闲桌自动回收。
   公开部署请配 `GameServer__AllowPlayerTables=false` 收紧，并定期清理不开的桌。
4. **表结构变更需换新库**：当前用 EF 的 `EnsureCreated`，缺表时启动会显式失败并提示换新库，不会静默丢数据。
5. **登录无失败限流**：暴力尝试只有日志记录。

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
