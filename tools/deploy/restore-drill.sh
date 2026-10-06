#!/usr/bin/env bash
#
# 恢复演练（M5 / G-A6-4）：把一份备份**真的恢复出来**，证明它不只是"看着有"。
#
# 做四件事，任何一步不对就以非零退出：
#   1. 把备份复制到一个临时目录（**绝不碰正在服务的那个库**）；
#   2. 用 `db-report` 读一遍：完整性、逐局事件条数——"这一局的复盘还在不在"；
#   3. 用这个恢复出来的库**起一个临时实例**（另一个端口，同一个程序），读 /healthz 与启动日志；
#   4. 收尾：停掉临时实例、删掉临时目录，原服务与生产库自始至终没被碰过。
#
# 用法（在目标机器上，需要能读备份文件的身份；服务自己的用户通常就是）：
#   bash tools/deploy/restore-drill.sh --app-dir <APP_DIR> --backup <备份文件> [--port 5199]
#
# 为什么值得单独跑一遍：备份命令自己会做完整性检查，但那只能证明"写出来那一刻是好的"。
# 恢复演练证明的是另一件事——**这份文件放回去能不能真的把一局跑起来**（页面、Hub、事件流全都在）。
set -euo pipefail

app_dir=""
backup_file=""
port=5199

usage() {
  cat <<'EOF'
用法：bash tools/deploy/restore-drill.sh --app-dir <APP_DIR> --backup <备份文件> [--port 5199]
  --app-dir <路径>   应用目录（里面是 OpenClockTower.Server 与 wwwroot）
  --backup <路径>    要演练的备份文件（oct-*.db）
  --port <端口>      临时实例监听的端口（默认 5199，只在本机回环上开一下）
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --app-dir) app_dir="${2:-}"; shift 2 ;;
    --backup) backup_file="${2:-}"; shift 2 ;;
    --port) port="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "未知参数：$1" >&2; usage; exit 1 ;;
  esac
done

if [[ -z "$app_dir" || -z "$backup_file" ]]; then
  echo "缺少参数。" >&2
  usage
  exit 1
fi

if [[ ! -x "$app_dir/OpenClockTower.Server" ]]; then
  echo "找不到可执行文件：$app_dir/OpenClockTower.Server" >&2
  exit 1
fi

if [[ ! -f "$backup_file" ]]; then
  echo "找不到备份文件：$backup_file" >&2
  exit 1
fi

work_dir="$(mktemp -d)"
server_pid=""

cleanup() {
  if [[ -n "$server_pid" ]] && kill -0 "$server_pid" 2>/dev/null; then
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi

  # 逐层清理：先删文件、再删目录，不做递归删除。
  if [[ -d "$work_dir" ]]; then
    find "$work_dir" -maxdepth 1 -type f -delete 2>/dev/null || true
    rm -f "$work_dir/data/oct.db" "$work_dir/data/oct.db-wal" "$work_dir/data/oct.db-shm" 2>/dev/null || true
    rmdir "$work_dir/data" 2>/dev/null || true
    rmdir "$work_dir" 2>/dev/null || true
  fi
}
trap cleanup EXIT

echo "演练目录：$work_dir"
mkdir -p "$work_dir/data"
cp "$backup_file" "$work_dir/data/oct.db"
chmod 600 "$work_dir/data/oct.db"

echo "① 体检恢复出来的库"
"$app_dir/OpenClockTower.Server" db-report --db "$work_dir/data/oct.db"

echo "② 用它起一个临时实例（端口 $port，只在本机回环）"
# 工作目录必须是应用目录：宿主的页面目录（wwwroot）跟着工作目录走，与 systemd 单元里那条一致。
( cd "$app_dir" && exec env -i \
  PATH="$PATH" \
  HOME="$work_dir" \
  DOTNET_ENVIRONMENT=Production \
  ASPNETCORE_URLS="http://127.0.0.1:$port" \
  GameServer__DatabasePath="$work_dir/data/oct.db" \
  GameServer__SeatCount="${SEATS:-7}" \
  "$app_dir/OpenClockTower.Server" ) >"$work_dir/server.log" 2>&1 &
server_pid=$!

alive=""
for _ in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:$port/healthz" >"$work_dir/healthz.json" 2>/dev/null; then
    alive="yes"
    break
  fi

  if ! kill -0 "$server_pid" 2>/dev/null; then
    break
  fi

  sleep 0.5
done

if [[ -z "$alive" ]]; then
  echo "临时实例没起来，启动日志如下：" >&2
  cat "$work_dir/server.log" >&2
  exit 1
fi

echo "③ 临时实例的读数"
cat "$work_dir/healthz.json"
echo
grep -E "数据库口径|在册的桌" "$work_dir/server.log" || true

echo
echo "演练结论：**通过**——这份备份恢复出来能起服、能读出上面那几局的历史。"
