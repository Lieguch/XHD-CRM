# syntax=docker/dockerfile:1
# ==========================================================================
# XHD CRM 3.1 (.NET 8 / ASP.NET Core) - Multi-stage Dockerfile
# ==========================================================================
# 项目:    小黄豆CRM3源码 v3.1.20260827.0
# 框架:    .NET 8 (net8.0)
# 入口:    1.UI/XHD.Core.View (XHD.Core.View.dll)
# 端口:    5001 (可通过 ASPNETCORE_URLS 覆盖)
# 数据库:  默认 Sqlite (/app/Data/xhdcrm3db.db)，可切 MySql/SqlServer/PostgreSQL
# ==========================================================================

########################################
# Stage 1: Build
########################################
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1) 先复制 sln 和所有 csproj —— 最大化 NuGet restore 层缓存
#    ⚠️ 必须与 XHDCRM3.sln 引用的 7 个项目完全一致，否则 dotnet restore 报 MSB3202
#    ⚠️ 使用 shell 形式 COPY（非 JSON 数组），避免 BuildKit 对数字开头路径的解析问题
COPY XHDCRM3.sln ./
COPY NuGet.config ./
COPY 1.UI/XHD.Core.View/XHD.Core.View.csproj 1.UI/XHD.Core.View/
COPY 2.Application/XHD.Core.Services/XHD.Core.Services.csproj 2.Application/XHD.Core.Services/
COPY 3.Repository/XHD.Core.Repository/XHD.Core.Repository.csproj 3.Repository/XHD.Core.Repository/
COPY 4.Entity/XHD.Core.Models/XHD.Core.Models.csproj 4.Entity/XHD.Core.Models/
COPY 5.Infrastructure/XHD.Core.Common/XHD.Core.Common.csproj 5.Infrastructure/XHD.Core.Common/
COPY 5.Infrastructure/CodeGenerator/CodeGenerator.csproj 5.Infrastructure/CodeGenerator/
COPY 7.Test/XHD.Core.Tests/XHD.Core.Tests.csproj 7.Test/XHD.Core.Tests/

# 2) Restore（完整 restore sln；测试项目的 xunit 等依赖只在 build 层，不进 runtime 镜像）
RUN dotnet restore ./XHDCRM3.sln --verbosity minimal

# 3) 复制所有源码
COPY . .

# 4) Publish（Release，输出 dll 而非 apphost，因容器内已有 dotnet runtime）
#    ⚠️ 不用 /p:SatelliteResourceLanguages —— MSBuild 命令行 /p: 值不支持 `;` 列表分隔符，
#       会被当命令行 switch 拆掉（报 "Switch: en" / MSB1006）。默认保留所有卫星资源
#       （含 zh-Hans），中文 CRM 必需。镜像大小差异 ~几百 KB，无关紧要。
RUN dotnet publish ./1.UI/XHD.Core.View/XHD.Core.View.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false \
    -v minimal

########################################
# Stage 2: Runtime
########################################
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# 装 curl（healthcheck 用）+ fonts（验证码图片生成用）
# ⚠️ fonts-dejavu-core + fontconfig 是 SystemFonts.Families 能返回非空的必要前提
#    .NET 8 的 SixLabors.Fonts SystemFonts 在 Linux 上从 /usr/share/fonts 读字体，
#    官方 aspnet 镜像默认没字体 → CreateImageAsync 里 SystemFonts.Families.FirstOrDefault() 返回 null
#    → fontFamily.CreateFont() 抛 NullReferenceException → 验证码 img 404 / 空白
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        curl ca-certificates \
        fonts-dejavu-core fontconfig && \
    rm -rf /var/lib/apt/lists/* && \
    fc-cache -f && \
    mkdir -p /app/Data /app/Uploads /app/Logs && \
    chown -R 1001:1001 /app

# 5) 复制 publish 产物
COPY --from=build /app/publish .

# 6) 环境变量（appsettings.json 里的 "Urls" 会被 ASPNETCORE_URLS 覆盖）
ENV ASPNETCORE_URLS=http://+:5001 \
    ASPNETCORE_ENVIRONMENT=Production \
    TZ=Asia/Shanghai \
    DOTNET_EnableDiagnostics=0

EXPOSE 5001

# 7) 非 root 用户运行（uid 1001，与上面 chown 一致）
USER 1001:1001

# 8) Healthcheck（30s 间隔，40s 启动宽限期）
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:5001/ >/dev/null || exit 1

# 9) 启动
ENTRYPOINT ["dotnet", "XHD.Core.View.dll"]
