#!/bin/bash
# =============================================================================
# entrypoint.sh — XHDCRM3 数据库幂等初始化脚本
#
# 由 db-init 服务（mssql/server 镜像 + 本脚本）执行一次后 exit 0。
# compose 里 app 依赖本服务 service_completed_successfully，确保建库完成才启动 app。
#
# 幂等保证：
#   IF DB_ID(N'XHDCRM3') IS NULL CREATE DATABASE [XHDCRM3];
#   已存在则跳过，可重复执行（容器重建后仍能安全重跑）。
#
# 环境变量（由 docker-compose.yml 注入）：
#   MSSQL_SA_PASSWORD — sa 密码（必填，来自 .env 的 DB_SA_PASSWORD）
#   DB_HOST           — SQL Server 容器名（默认 db）
#   DB_PORT           — 端口（默认 1433）
#   DB_NAME           — 要创建的库名（默认 XHDCRM3）
#   DB_USER           — sa 用户（默认 sa）
#   DB_INIT_MAX_WAIT  — 等待超时秒数（默认 180）
#   DB_INIT_INTERVAL  — 轮询间隔秒数（默认 5）
# =============================================================================
set -euo pipefail

DB_HOST="${DB_HOST:-db}"
DB_PORT="${DB_PORT:-1433}"
DB_NAME="${DB_NAME:-XHDCRM3}"
DB_USER="${DB_USER:-sa}"
DB_PASSWORD="${MSSQL_SA_PASSWORD:-}"
SQLCMD="${SQLCMD:-/opt/mssql-tools/bin/sqlcmd}"
MAX_WAIT="${DB_INIT_MAX_WAIT:-180}"
INTERVAL="${DB_INIT_INTERVAL:-5}"

echo "[db-init] =============================================="
echo "[db-init] XHDCRM3 database initialization"
echo "[db-init] host=${DB_HOST}:${DB_PORT}  db=${DB_NAME}  user=${DB_USER}"
echo "[db-init] =============================================="

# --- 前置校验 ---------------------------------------------------------------
if [ -z "${DB_PASSWORD}" ]; then
    echo "[db-init] FATAL: MSSQL_SA_PASSWORD is not set." >&2
    echo "[db-init] Set DB_SA_PASSWORD in .env — see .env.example." >&2
    exit 1
fi

if [ ! -x "${SQLCMD}" ]; then
    echo "[db-init] FATAL: sqlcmd not found or not executable at ${SQLCMD}" >&2
    ls -l /opt/mssql-tools/bin/ 2>/dev/null || true
    exit 1
fi

# --- 1/3 轮询等待 SQL Server 就绪 -------------------------------------------
# 用 SELECT 1 探测（连默认 master 库，只证明 SQL Server 进程可用）。
# -C = Trust Server Certificate（匹配 appsettings 的 TrustServerCertificate=True）
# -b = 出错即退出（让 while 循环捕获非零退出码重试）
echo "[db-init] Step 1/3: waiting for SQL Server to accept connections..."
elapsed=0
while ! "${SQLCMD}" -S "${DB_HOST},${DB_PORT}" -U "${DB_USER}" -P "${DB_PASSWORD}" \
        -C -b -Q "SELECT 1" -o /dev/null 2>/dev/null; do
    if [ "${elapsed}" -ge "${MAX_WAIT}" ]; then
        echo "[db-init] FATAL: SQL Server at ${DB_HOST}:${DB_PORT} not reachable after ${MAX_WAIT}s" >&2
        exit 1
    fi
    echo "[db-init]   not ready yet (${elapsed}s/${MAX_WAIT}s), retry in ${INTERVAL}s..."
    sleep "${INTERVAL}"
    elapsed=$((elapsed + INTERVAL))
done
echo "[db-init]   SQL Server is accepting connections."

# --- 2/3 幂等创建 XHDCRM3 库 ------------------------------------------------
# IF DB_ID(...) IS NULL CREATE DATABASE — 已存在则跳过，可重复执行
echo "[db-init] Step 2/3: ensuring database [${DB_NAME}] exists (idempotent)..."
"${SQLCMD}" -S "${DB_HOST},${DB_PORT}" -U "${DB_USER}" -P "${DB_PASSWORD}" \
    -C -b -Q "IF DB_ID(N'${DB_NAME}') IS NULL CREATE DATABASE [${DB_NAME}];"
echo "[db-init]   CREATE DATABASE [${DB_NAME}] done (no-op if already exists)."

# --- 3/3 打印数据库列表确认 -------------------------------------------------
echo "[db-init] Step 3/3: verifying databases on ${DB_HOST}:"
"${SQLCMD}" -S "${DB_HOST},${DB_PORT}" -U "${DB_USER}" -P "${DB_PASSWORD}" \
    -C -b -s "|" -W -Q "SELECT name AS DatabaseName, state_desc AS State FROM sys.databases ORDER BY name;"

echo "[db-init] =============================================="
echo "[db-init] DONE. Database [${DB_NAME}] is ready."
echo "[db-init] =============================================="
exit 0
