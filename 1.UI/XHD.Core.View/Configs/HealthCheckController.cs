using System;
using System.Reflection;
using System.Text.RegularExpressions;
using FreeSql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using XHD.Core.Common;

namespace XHD.Core.View.Configs
{
    /// <summary>
    /// Sprint 10.12 (2026-09-18): 容器健康检查端点
    ///
    /// 根因（2026-09-18 核实）：
    ///   1. docker-compose.yml 的 db 服务 healthcheck `SELECT 1` 连的是默认
    ///      master 库，无法反映用户库 XHDCRM3 是否已建 → app 依赖
    ///      service_healthy 立刻启动 → 首个 DB 查询抛异常。
    ///   2. Dockerfile / compose 的 app healthcheck 原为 TCP 探测
    ///      (`exec 3<>/dev/tcp/127.0.0.1/5001`)，只能证明进程活着，
    ///      无法证明数据库可用。
    ///
    /// 修复：新增 /health 端点，真实执行 SELECT 1 探测数据库，
    ///      返回结构化 JSON 诊断信息。Dockerfile / compose 的 HEALTHCHECK
    ///      改为 `curl -fsS http://localhost:5001/health`，
    ///      使"容器 healthy"真正等于"数据库可用"。
    ///
    /// 注意：本端点必须 [AllowAnonymous]，因为它是登录页的依赖项，
    ///       不能要求已认证（否则启动期死锁）。
    /// </summary>
    [ApiController]
    [Route("/health")]
    [AllowAnonymous]
    public class HealthCheckController : Controller
    {
        private readonly IFreeSql _fsql;
        private readonly IHostEnvironment _env;

        public HealthCheckController(IFreeSql fsql, IHostEnvironment env)
        {
            _fsql = fsql;
            _env = env;
        }

        /// <summary>
        /// GET /health — 真实探测数据库，返回结构化诊断 JSON
        /// </summary>
        [HttpGet]
        public IActionResult Get()
        {
            // --- 1. 读取 DbConfig（读不到也不抛异常，优雅降级） ---
            string dbType = null;
            string configDetail = null;
            try
            {
                var cfg = new ConfigHelper().Get<DbConfig>("appsettings", _env.EnvironmentName);
                if (cfg == null)
                {
                    configDetail = "DbConfig 未加载（appsettings.json 缺失或结构不符）";
                }
                else
                {
                    dbType = cfg.Type.ToString();
                    configDetail = "type=" + dbType + ", connection=" + SanitizeConnectionString(cfg.ConnectionString);
                }
            }
            catch (Exception ex)
            {
                configDetail = "读 DbConfig 异常: " + Truncate(ex.Message, 300);
            }

            // --- 2. 真实探测数据库：SELECT 1 ---
            // ExecuteScalar 跨数据库通用（SqlServer/Sqlite/MySql/PostgreSQL 均支持）
            // 连接失败抛异常 → 被 catch → dbOk=false → 返回 503 → HEALTHCHECK 判定失败
            bool dbOk = false;
            string dbDetail = null;
            try
            {
                _fsql.Ado.ExecuteScalar("SELECT 1");
                dbOk = true;
                dbDetail = "SELECT 1 成功";
            }
            catch (Exception ex)
            {
                dbDetail = Truncate(ex.Message, 300);
            }

            // --- 3. 版本号 ---
            string version = "unknown";
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var v = asm.GetName().Version;
                version = v != null ? v.ToString() : (asm.GetName().Name ?? "unknown");
            }
            catch
            {
                version = "unknown";
            }

            // --- 4. 组装返回 JSON ---
            var payload = new
            {
                status = dbOk ? "healthy" : "unhealthy",
                db = dbOk,
                dbDetail = dbDetail,
                dbConfig = configDetail,
                environment = _env.EnvironmentName,
                version = version,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            // 不健康时返回 503，让 curl -fsS 返回非零退出码 → HEALTHCHECK 判定失败
            return dbOk
                ? Json(payload)
                : StatusCode(503, payload);
        }

        /// <summary>
        /// 截断过长字符串，避免响应体爆炸
        /// </summary>
        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }

        /// <summary>
        /// 脱敏连接串：把 Password/pwd/passwd 等敏感字段值替换为 *****
        /// </summary>
        private static string SanitizeConnectionString(string conn)
        {
            if (string.IsNullOrWhiteSpace(conn)) return "(empty)";
            var masked = Regex.Replace(
                conn,
                "(?i)((?:Password|Pwd|Passwd)\\s*=\\s*)[^;]*",
                "$1*****");
            return Truncate(masked, 300);
        }
    }
}
