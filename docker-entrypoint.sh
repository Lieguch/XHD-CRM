#!/bin/sh
set -eu

# 容器启动时修正命名卷目录权限。
# Compose 命名卷会覆盖镜像内 chown 结果；首次创建或历史卷为 root:root 时，
# 应用以 UID 1001 运行会导致 /app/Data 写 DataProtection key/tmp 失败。
if [ "$(id -u)" = "0" ]; then
    mkdir -p /app/Data /app/Uploads /app/Logs
    chown -R 1001:1001 /app/Data /app/Uploads /app/Logs
    exec gosu 1001:1001 "$@"
fi

exec "$@"
