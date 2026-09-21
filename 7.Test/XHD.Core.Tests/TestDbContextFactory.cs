using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using XHD.Core.Common;
using XHD.Core.Models;

namespace XHD.Core.Tests
{
    /// <summary>
    /// 测试数据库工厂：为每条测试提供独立的 SQLite 内存数据库，
    /// 自动 Code First 同步 Schema，避免测试间相互污染。
    /// </summary>
    public static class TestDbContextFactory
    {
        /// <summary>
        /// 创建内存 SQLite FreeSql 实例，自动同步所有 XHD 实体表结构。
        /// </summary>
        /// <returns>已配置好的 <see cref="IFreeSql"/> 实例</returns>
        public static IFreeSql CreateFreeSql()
        {
            var fsql = new FreeSqlBuilder()
                .UseConnectionString(FreeSql.DataType.Sqlite, "DataSource=:memory:")
                .UseAutoSyncStructure(true)
                .Build();

            // 显式建表：与生产环境 DB.cs 保持一致，AutoSyncStructure 首次查询时才会同步
            // 提前触发一次 schema 同步，保证测试开始时表已就绪
            SyncSchema(fsql);
            return fsql;
        }

        /// <summary>
        /// 手动触发所有 XHD Entity 的 Code First 建表。
        /// </summary>
        /// <param name="fsql">FreeSql 实例</param>
        public static void SyncSchema(IFreeSql fsql)
        {
            fsql.CodeFirst.SyncStructure<CRM_Customer>();
            fsql.CodeFirst.SyncStructure<Sys_Param>();
            fsql.CodeFirst.SyncStructure<Sys_Param_Type>();
            fsql.CodeFirst.SyncStructure<hr_employee>();
            fsql.CodeFirst.SyncStructure<hr_department>();
            fsql.CodeFirst.SyncStructure<hr_position>();
            fsql.CodeFirst.SyncStructure<Sale_order>();
            fsql.CodeFirst.SyncStructure<Sale_order_details>();
            fsql.CodeFirst.SyncStructure<CRM_Contact>();
            fsql.CodeFirst.SyncStructure<CRM_follow>();
            fsql.CodeFirst.SyncStructure<Finance_Receivable>();
            fsql.CodeFirst.SyncStructure<Finance_Receive>();
            fsql.CodeFirst.SyncStructure<Finance_Invoice>();
            fsql.CodeFirst.SyncStructure<Sys_role>();
            fsql.CodeFirst.SyncStructure<Sys_authority>();
            fsql.CodeFirst.SyncStructure<Sys_log>();
            fsql.CodeFirst.SyncStructure<Sys_info>();
            fsql.CodeFirst.SyncStructure<Sys_Menu>();
            fsql.CodeFirst.SyncStructure<Sys_Button>();
            fsql.CodeFirst.SyncStructure<Sys_Param_Provinces>();
            fsql.CodeFirst.SyncStructure<Sys_Param_City>();
            fsql.CodeFirst.SyncStructure<CRM_Customer_atta>();
            fsql.CodeFirst.SyncStructure<CRM_Customer_Bath>();
            fsql.CodeFirst.SyncStructure<Product>();
            fsql.CodeFirst.SyncStructure<Product_category>();
            fsql.CodeFirst.SyncStructure<Sale_contract>();
            fsql.CodeFirst.SyncStructure<Sale_contract_atta>();
            fsql.CodeFirst.SyncStructure<Message_news>();
            fsql.CodeFirst.SyncStructure<My_Calendar>();
            fsql.CodeFirst.SyncStructure<My_Note>();
            fsql.CodeFirst.SyncStructure<Jobs>();
            fsql.CodeFirst.SyncStructure<Jobs_follow>();
            // Sprint 5 Wave 1 新增
            fsql.CodeFirst.SyncStructure<hr_post>();
            fsql.CodeFirst.SyncStructure<Sys_role_emp>();
            // Sprint 7 新增
            fsql.CodeFirst.SyncStructure<Sys_online>();
            fsql.CodeFirst.SyncStructure<SMS>();
        }
    }

    /// <summary>
    /// Controller 测试辅助类：为测试构造带 <see cref="HttpContext"/> 的 Controller，
    /// 使其可以调用 <see cref="Request"/>, <see cref="User"/> 等属性。
    /// </summary>
    public static class TestControllerHelper
    {
        /// <summary>
        /// 用指定 QueryString 和 用户身份构造一个带默认 HttpContext 的 Controller。
        /// </summary>
        /// <typeparam name="TController">Controller 类型</typeparam>
        /// <param name="queryString">形如 "?keyword=xx&date1=2024-03-01"</param>
        /// <param name="userId">当前用户 ID（ClaimTypes.Sid）</param>
        /// <param name="userName">当前用户名（ClaimTypes.Name）</param>
        /// <param name="constructor">构造器参数</param>
        public static TController CreateWithHttpContext<TController>(
            string queryString = "",
            string userId = "TEST_USER",
            string userName = "Test User",
            params object[] constructor) where TController : Controller
        {
            // 组装构造函数参数
            object[] ctorArgs = constructor;
            TController ctrl = (TController)Activator.CreateInstance(typeof(TController), ctorArgs)!;

            var httpContext = new DefaultHttpContext();

            // 设置 Query String
            if (!string.IsNullOrEmpty(queryString))
            {
                // 去掉最前面的 ? 后组装 QueryString
                var qs = queryString.StartsWith("?") ? queryString.Substring(1) : queryString;
                httpContext.Request.QueryString = new QueryString($"?{qs}");
            }

            // 设置 Identity
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, userId),
                new Claim(ClaimTypes.Name, userName)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            httpContext.User = new ClaimsPrincipal(identity);

            // 把 RemoteIP 设成 127.0.0.1，避免 Controller 访问 RemoteIpAddress 时抛异常
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

            ctrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return ctrl;
        }

        /// <summary>
        /// 从 QueryString 构造 PageView（模拟前端请求）。
        /// </summary>
        public static PageView<T> BuildPageView<T>(int page = 1, int limit = 30)
        {
            return new PageView<T> { Page = page, Limit = limit };
        }
    }
}
