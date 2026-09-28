# syntax=docker/dockerfile:1.7
# =============================================================================
# db-init/Dockerfile — XHDCRM3 数据库一次性初始化服务
#
# 根因（2026-09-18 核实）：
#   mcr.microsoft.com/mssql/server 镜像只自动创建 master/tempdb/model/msdb 四个
#   系统库，从不创建用户库 XHDCRM3。docker-compose.yml 的 db 服务 healthcheck
#   `sqlcmd -Q 'SELECT 1'` 未指定 -d，默认连 master → 永远 healthy → app 依赖
#   service_healthy 立刻启动 → 首个 DB 查询抛
#   "Cannot open database 'XHDCRM3' requested by the login"。
#
# 修复方案：复用 mssql/server 镜像（自带 sqlcmd，无需额外下载工具镜像），
#   COPY entrypoint.sh 作为 ENTRYPOINT，幂等创建 XHDCRM3 后 exit 0。
#   compose 里 app 依赖本服务 condition: service_completed_successfully，
#   而不是直接依赖 db 的 service_healthy（因为 db 的 healthcheck 连 master，
#   无法反映 XHDCRM3 是否已建）。
#
# 官方依据: https://learn.microsoft.com/sql/linux/quickstart-install-connect-docker
#   mssql/server 官方给的建库方式就是 sqlcmd -Q "CREATE DATABASE [name]"
# =============================================================================

# 参数化基础镜像 tag，与 docker-compose.yml 的 DB_IMAGE_TAG 保持一致
ARG DB_IMAGE_TAG=2025-latest
FROM mcr.microsoft.com/mssql/server:${DB_IMAGE_TAG}

# mssql/server 镜像的默认 ENTRYPOINT 是 /opt/mssql/bin/sqlservr（SQL Server 服务）。
# 这里用 COPY 覆盖 ENTRYPOINT，让容器启动后只跑初始化脚本然后退出。
# sqlcmd 在 /opt/mssql-tools/bin/sqlcmd（mssql/server 2022/2025 镜像内置，无需下载）。
COPY entrypoint.sh /usr/local/bin/entrypoint.sh

# Windows 构建上下文不保留可执行位，必须显式 chmod +x
RUN chmod +x /usr/local/bin/entrypoint.sh

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
