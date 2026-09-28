// SPDX-License-Identifier: MIT
// Sprint 10 - 2026-09-22: SysConfigController 变薄壳。
// 根因修复：把 8 个 private config* 方法（900+ 行）下沉到 IDatabaseInitializerService。
// Controller 现在只负责 HTTP 配置读写 + 调用初始化服务。

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FreeSql;
using Newtonsoft.Json.Linq;
using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// 系统配置向导（首次安装 + 连接测试）。
    /// </summary>
    /// <remarks>
    /// Sprint 10 重构：种子数据初始化已下沉到 <see cref="IDatabaseInitializerService"/>。
    /// 本 Controller 只承担 HTTP 层职责：读取用户输入、更新 appsettings.json、
    /// 构造连接并调用初始化服务。事务/幂等/日志均由 Service 处理。
    /// </remarks>
    public class SysConfigController : Controller
    {
        private readonly IDatabaseInitializerService _initializer;
        private readonly ILogger<SysConfigController> _logger;

        public SysConfigController(
            IDatabaseInitializerService initializer,
            ILogger<SysConfigController> logger)
        {
            _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>安装向导首页。</summary>
        public IActionResult Index()
        {
            var root = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
            ViewData["isConfig"] = root["isConfig"];
            return View();
        }

        /// <summary>测试数据库连接，返回可用数据库列表。</summary>
        public string tryConnect(
            int servertype,
            string servername,
            string serverport,
            string serveruid,
            string serverpwd)
        {
            var (datatype, connString) = BuildConnection(servertype, servername, serverport, serveruid, serverpwd);

            try
            {
                using var fsql = new FreeSqlBuilder()
                    .UseConnectionString(datatype, connString)
                    .Build();

                var databases = fsql.DbFirst.GetDatabases();
                var arr = new JArray();
                foreach (var db in databases)
                {
                    arr.Add(new JObject { ["table"] = db });
                }
                return XHDResult.Success(arr).ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "tryConnect 失败：servertype={Type}, servername={Name}", servertype, servername);
                return XHDResult.Error("数据库连接失败！").ToString();
            }
        }

        /// <summary>执行首次配置：更新 appsettings.json + 初始化数据库。</summary>
        public async Task<string> tryConfig()
        {
            // ① 检查是否已配置
            var root = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            if (root["isConfig"] != "0")
            {
                return XHDResult.Error("不允许配置，请检查配置文件！").ToString();
            }

            var type = root["type"]?.ToUpper() ?? "SQLITE";
            var connectionString = root["connectionString"];
            if (string.IsNullOrEmpty(connectionString))
            {
                return XHDResult.Error("连接字符串为空！").ToString();
            }

            // ② 把 isConfig 置 1 并回写 appsettings.json
            try
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                var text = System.IO.File.ReadAllText(filePath);
                var obj = JObject.Parse(text);
                obj["isConfig"] = 1;
                System.IO.File.WriteAllText(filePath, obj.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写回 appsettings.json 失败");
                return XHDResult.Error("配置失败！").ToString();
            }

            // ③ 构造 FreeSql 连接（首次配置用当前用户提供的连接串）
            IFreeSql fsql;
            try
            {
                var dt = ParseDataType(type);
                fsql = new FreeSqlBuilder()
                    .UseConnectionString(dt, connectionString)
                    .UseAutoSyncStructure(true)
                    .Build();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "构造 FreeSql 失败");
                return XHDResult.Error("数据库初始化失败！").ToString();
            }

            // ④ 调用初始化服务（事务 + 幂等 + 日志由 Service 处理）
            SeedResult result;
            try
            {
                result = await _initializer.SeedAsync(fsql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化服务抛异常（不应发生，SeedAsync 内部已 try/catch）");
                return XHDResult.Error("系统错误，请查看服务器日志！").ToString();
            }

            if (!result.Success)
            {
                _logger.LogError("初始化失败：{Error}", result.Error);
                return XHDResult.Error(result.Message).ToString();
            }

            // ⑤ 登出当前会话（配置向导不应保留旧 session）
            try
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignOut 失败（可忽略）");
            }

            _logger.LogInformation(
                "配置向导完成：{Total} 行初始化（已初始化={Already}）",
                result.TotalInserted, result.AlreadySeeded);

            return XHDResult.Success().ToString();
        }

        // ============================= helpers =============================

        private static FreeSql.DataType ParseDataType(string s)
        {
            switch (s?.ToUpper())
            {
                case "MYSQL": return FreeSql.DataType.MySql;
                case "SQLSERVER": return FreeSql.DataType.SqlServer;
                case "POSTGRESQL": return FreeSql.DataType.PostgreSQL;
                case "ORACLE": return FreeSql.DataType.Oracle;
                case "SQLITE": return FreeSql.DataType.Sqlite;
                case "ODBCORACLE": return FreeSql.DataType.OdbcOracle;
                case "ODBCSQLSERVER": return FreeSql.DataType.OdbcSqlServer;
                case "ODBCMYSQL": return FreeSql.DataType.OdbcMySql;
                case "ODBCPOSTGRESQL": return FreeSql.DataType.OdbcPostgreSQL;
                case "ODBC": return FreeSql.DataType.Odbc;
                case "MSACCESS": return FreeSql.DataType.MsAccess;
                case "DAMENG": return FreeSql.DataType.Dameng;
                case "SHENTONG": return FreeSql.DataType.ShenTong;
                case "KINGBASEES": return FreeSql.DataType.KingbaseES;
                case "FIREBIRD": return FreeSql.DataType.Firebird;
                default: return FreeSql.DataType.MySql;
            }
        }

        private static (FreeSql.DataType datatype, string connString) BuildConnection(
            int? servertype, string servername, string serverport, string serveruid, string serverpwd)
        {
            if (servertype == null) return (FreeSql.DataType.MySql, string.Empty);

            switch (servertype.Value)
            {
                case 0: // MySql
                    return (FreeSql.DataType.MySql,
                        $"Data Source={servername};Port={serverport};User ID={serveruid};Password={serverpwd};Charset=utf8;SslMode=none;Max pool size=2");
                case 1: // SqlServer
                    return (FreeSql.DataType.SqlServer,
                        serverport == "1433"
                            ? $"Uid={serveruid};Pwd={serverpwd};Initial Catalog=master;Data Source={servername};Pooling=true;Max Pool Size=3"
                            : $"Uid={serveruid};Pwd={serverpwd};Initial Catalog=master;Data Source={servername},{serverport};Pooling=true;Max Pool Size=3");
                case 2: // PostgreSQL
                    return (FreeSql.DataType.PostgreSQL,
                        $"Host={servername};Port={serverport};Username={serveruid};Password={serverpwd};Database=postgres;Pooling=true;Maximum Pool Size=2");
                case 4: // Sqlite
                    return (FreeSql.DataType.Sqlite,
                        $"Data Source={servername};Pooling=true;Max Pool Size=10");
                default:
                    return (FreeSql.DataType.MySql, string.Empty);
            }
        }
    }
}
