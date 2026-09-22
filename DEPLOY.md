# XHD CRM 3.1 部署说明 (Docker)

> 目标：把 XHD CRM 3.1 (.NET 8 / ASP.NET Core) 一键部署到任意 Linux Docker 主机。
> 源仓库：CNB `https://cnb.cool/lieguch/XHD-CRM`（HEAD `2e66153`，Sprint 9 收官）

---

## 一、快速开始（默认 Sqlite）

```bash
# 1. 拉代码（或解包已下载的 xhd-crm-3.1-deploy.zip）
cd /opt/xhd-crm

# 2. 构建并启动（首次约 3-5 分钟，取决于镜像拉取速度）
docker compose up -d --build

# 3. 查看状态
docker compose ps
docker compose logs -f web

# 4. 访问
#    http://<host>:5001
```

首次启动会自动创建 Sqlite 数据库 `/app/Data/xhdcrm3db.db` 并跑 CodeFirst 迁移。

---

## 二、数据库切换（推荐生产用 MySQL）

### 2.1 Sqlite → MySQL 切换步骤

```bash
# 1) 编辑 docker-compose.yml
#    - 取消 mysql 服务的注释（把注释符号 # 去掉）
#    - 修改 web.environment 的 DB_TYPE 和 DB_CONNECTION
#    - 修改 web.depends_on.mysql.condition

# 2) 更新 appsettings.json（在 1.UI/XHD.Core.View/appsettings.json）
#    - "type": "MySql"
#    - "connectionString": "Server=mysql;Port=3306;Database=xhdcrm;Uid=xhdcrm;Pwd=xhdcrm_pwd;Charset=utf8mb4;Pooling=true;Min Pool Size=1"

# 3) 重建
docker compose down
docker compose up -d --build
```

### 2.2 支持的数据库

FreeSql 3.5.310 通过 `type` 字段切换驱动，`connectionString` 格式见 appsettings.json 注释：

| 类型 | 依赖包 | 备注 |
|------|--------|------|
| `Sqlite` | FreeSql.Provider.Sqlite | 默认，单机快速部署 |
| `MySql` | FreeSql.Provider.MySqlConnector | 生产推荐 |
| `SqlServer` | FreeSql.Provider.SqlServer | 兼容 A 版 |
| `PostgreSQL` | FreeSql.Provider.PostgreSQL | 需改连接串 |

---

## 三、环境变量

Docker 容器内已预设：

```yaml
ASPNETCORE_URLS: "http://+:5001"
ASPNETCORE_ENVIRONMENT: "Production"
TZ: "Asia/Shanghai"
DOTNET_EnableDiagnostics: 0
```

如需覆盖，在 `docker-compose.yml` 的 `web.environment` 段添加即可。

---

## 四、持久化卷

| 容器路径 | 卷名 | 内容 |
|----------|------|------|
| `/app/Data` | `xhd-crm-data` | Sqlite 数据库文件 `xhdcrm3db.db` |
| `/app/Uploads` | `xhd-crm-uploads` | 用户上传文件（附件、头像、导入 Excel） |
| `/app/Logs` | `xhd-crm-logs` | 应用日志 |

导出备份：

```bash
docker run --rm -v xhd-crm-data:/data -v $(pwd):/backup alpine \
  tar czvf /backup/xhdcrm-data-$(date +%Y%m%d).tar.gz -C /data .
```

恢复：

```bash
docker run --rm -v xhd-crm-data:/data -v $(pwd):/backup \
  alpine tar xzvf /backup/xhdcrm-data-YYYYMMDD.tar.gz -C /data
docker compose restart web
```

---

## 五、常见运维命令

```bash
# 查看日志
docker compose logs -f web

# 停止
docker compose down

# 停止 + 删除数据卷（危险！）
docker compose down -v

# 重新构建镜像
docker compose build --no-cache web

# 进入容器调试
docker exec -it xhd-crm-web bash

# 查看版本
docker exec xhd-crm-web dotnet --version
```

---

## 六、反向代理（Nginx / Caddy）

生产环境建议加 HTTPS 反代。示例 Caddyfile：

```
your-domain.com {
    reverse_proxy localhost:5001
}
```

Nginx 配置要点：

```nginx
server {
    listen 443 ssl;
    server_name your-domain.com;
    client_max_body_size 50M;   # 附件上传
    location / {
        proxy_pass http://localhost:5001;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

---

## 七、故障排查

| 症状 | 原因 | 解决 |
|------|------|------|
| 启动后立即退出 | 数据库连接失败 | `docker compose logs web` 查看错误，检查 `appsettings.json` |
| 端口 5001 冲突 | 本机已有 5001 服务 | 改 `ports: "8080:5001"` |
| 附件上传 404 | `/app/Uploads` 目录不存在 | 容器内 `mkdir -p /app/Uploads` |
| Sqlite 数据库损坏 | 磁盘满 / 断电 | 恢复备份卷 |
| Razor 视图运行时错误 | 未发布 Razor 编译 | 重新 `docker compose build --no-cache` |

---

## 八、从 A 版 (2018) 迁移注意

A 版部署产物是 IIS + Web.config + SQL Server 直连。B 版 Docker 化后：

1. **Web.config 迁移**：A 版 `web.config` 里的 `appSettings` / `connectionStrings` 已合并到 B 版 `appsettings.json`（见 1.UI/XHD.Core.View/）。
2. **数据库**：若原库为 SQL Server，可直接切 `type=SqlServer` + 原连接串；建议先导出结构再切 MySql。
3. **静态文件**：A 版 `/View/` 和 `/ueditor/` 已在 B 版合并到 `wwwroot/`。
4. **权限**：A 版依赖 Windows IIS 匿名账户；B 版容器内以 uid=1001 运行。
5. **定时任务**：A 版若有 Windows 计划任务调用 `/api/xxx`，改为 cron 或容器内 Systemd 定时 curl。

---

## 九、部署清单

- [ ] 主机 Docker ≥ 24.0，Docker Compose ≥ 2.20
- [ ] 开放端口 5001（或映射为 80/443）
- [ ] 已备份 `appsettings.json` 的 DB 配置
- [ ] 已备份 A 版 SQL Server 数据库（若从 A 迁移）
- [ ] 已配置反向代理（生产）
- [ ] 已配置数据卷定时备份
