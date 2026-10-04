using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.Services;
using XHD.Core.View.Controllers;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.38 系统基座线（三）：SysBaseController / SysLogController /
    /// SysLogErrController / HomeController 端点真测试。
    /// 全部注入真实 Service（内部真实 Repository + 真实 SQLite 内存库）。
    /// Sys_baseService 的服务层行为已由 SysBaseUploadSMSTests 覆盖，本文件只补
    /// **Controller 层**盲区：Claim 提取（Sid/uid）、登录过期分支、admin 鉴权、
    /// 查询参数装配与 HomeController.iniUrl 的 portal 回归保护。
    /// </summary>
    public class SysBaseLogTests : IDisposable
    {
        private readonly IFreeSql _fsql;

        public SysBaseLogTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 装配辅助 ============

        private static IServiceProvider BuildRequestServices()
        {
            // iniUrl 异常分支会调 HttpContext.SignOutAsync，需要 IAuthenticationService
            var authMock = new Mock<IAuthenticationService>();
            authMock.Setup(a => a.SignOutAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask);
            return new ServiceCollection()
                .AddSingleton(authMock.Object)
                // 根因修复：Controller.View() 经 TempData 属性取 ITempDataDictionaryFactory，
                // 缺注册时 View() 抛 InvalidOperationException（生产由 AddControllersWithViews 提供）。
                // 本测试只构造 ViewResult 不执行它，Provider 不会被真正调用，注册实现即可。
                .AddSingleton<ITempDataDictionaryFactory, TempDataDictionaryFactory>()
                .AddSingleton<ITempDataProvider, SessionStateTempDataProvider>()
                .BuildServiceProvider();
        }

        /// <summary>
        /// 把 uid claim 并入 Controller 当前的 Identity。
        /// 根因修复：原实现 User.AddIdentity(...) 追加了**第二个** Identity，而
        /// HomeController.iniUrl:104 取 User.Identity（= 第一个 Identity）后再 FindFirst("uid")，
        /// uid 落在第二个 Identity 上必然查不到 => NullReferenceException => 进 catch 返回「系统错误！」。
        /// 生产环境 AccountController.SignIn 是「单一 Identity 承载全部 claim」，测试与之对齐。
        /// </summary>
        private static TController WithUid<TController>(TController ctrl, string uid) where TController : Controller
        {
            var httpContext = ctrl.ControllerContext.HttpContext;
            var existing = httpContext.User.Identity as ClaimsIdentity;
            var claims = (existing?.Claims ?? Enumerable.Empty<Claim>())
                .Append(new Claim("uid", uid));
            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(claims, existing?.AuthenticationType ?? "TestAuth"));
            return ctrl;
        }

        /// <summary>
        /// 把 User 换成没有任何 claim 的主体，用于「登录状态已过期」分支。
        /// </summary>
        private static TController WithNoClaims<TController>(TController ctrl) where TController : Controller
        {
            ctrl.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
            return ctrl;
        }

        private SysBaseController CreateSysBaseController(string userId = "TEST_USER", string userName = "Test User")
        {
            var svc = new Sys_baseService(
                new Sys_MenuRepository(_fsql),
                new Sys_onlineRepository(_fsql),
                new Sys_authorityRepository(_fsql),
                new Sys_role_empRepository(_fsql),
                new hr_departmentRepository(_fsql),
                new hr_postRepository(_fsql));

            var ctrl = TestControllerHelper.CreateWithHttpContext<SysBaseController>(
                string.Empty, userId, userName,
                new Mock<ILogger<SysBaseController>>().Object,
                svc);
            ctrl.ControllerContext.HttpContext.RequestServices = BuildRequestServices();
            return ctrl;
        }

        private SysLogController CreateSysLogController(
            string userId = "TEST_USER",
            string userName = "Test User",
            string queryString = "")
        {
            var svc = new Sys_logService(new Sys_logRepository(_fsql));

            return TestControllerHelper.CreateWithHttpContext<SysLogController>(
                queryString, userId, userName,
                new Mock<ILogger<SysLogController>>().Object,
                svc);
        }

        private SysLogErrController CreateSysLogErrController(
            string userId = "TEST_USER",
            string userName = "Test User",
            int authType = 4,
            string queryString = "")
        {
            var svc = new Sys_log_ErrService(new Sys_log_ErrRepository(_fsql));

            // 权限 mock 两件套：GetDataAuth 返回全公司权限，GetAuth 同时放行
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = authType, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            return TestControllerHelper.CreateWithHttpContext<SysLogErrController>(
                queryString, userId, userName,
                new Mock<ILogger<SysLogErrController>>().Object,
                svc,
                auth.Object);
        }

        private HomeController CreateHomeController(string userId = "TEST_USER", string userName = "Test User", string uid = null)
        {
            EnsureAppSettingsJsonAvailable();

            var ctrl = TestControllerHelper.CreateWithHttpContext<HomeController>(
                string.Empty, userId, userName,
                new Mock<ILogger<HomeController>>().Object,
                new Sys_MenuService(new Sys_MenuRepository(_fsql)),
                _fsql,
                new CRM_CustomerService(new CRM_CustomerRepository(_fsql)),
                new Sys_infoService(new Sys_infoRepository(_fsql)));

            if (uid != null)
            {
                WithUid(ctrl, uid);
            }
            ctrl.ControllerContext.HttpContext.RequestServices = BuildRequestServices();
            return ctrl;
        }

        /// <summary>
        /// HomeController.Index 用 ConfigurationBuilder.AddJsonFile("appsettings.json", optional: false)
        /// 读 BaseDirectory 下的文件；测试输出目录不一定带它，缺失时补一个最小空配置，
        /// 避免 Index 因文件不存在直接抛 FileNotFoundException（Index 无 try/catch 保护这段）。
        /// </summary>
        private static void EnsureAppSettingsJsonAvailable()
        {
            string file = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(file))
            {
                File.WriteAllText(file, "{}");
            }
        }

        // ============ 数据工厂 ============

        private static Sys_Menu NewMenu(string id, string appid, string name, string parentid, int order)
            => new Sys_Menu
            {
                id = id,
                App_id = appid,
                Menu_name = name,
                parentid = parentid,
                Menu_order = order,
                Menu_url = "/" + id,
                Menu_icon = "fa-circle",
                Menu_type = "1"
            };

        private static Sys_info NewInfo(string key, string value)
            => new Sys_info { sys_key = key, sys_value = value };

        private static Sys_log NewLog(string id, string eventType, DateTime eventDate, string title = "事件")
            => new Sys_log
            {
                id = id,
                EventType = eventType,
                EventDate = eventDate,
                EventTitle = title,
                EventID = "C1",
                UserID = "TEST_USER",
                UserName = "Test User",
                IPStreet = "127.0.0.1"
            };

        private static Sys_log_Err NewErr(string id, int typeid, string type, DateTime time, string message = "", string trace = "")
            => new Sys_log_Err
            {
                id = id,
                Err_typeid = typeid,
                Err_type = type,
                Err_time = time,
                Err_message = message,
                Err_trace = trace,
                Err_url = "/test/" + id,
                Err_emp_id = "TEST_USER",
                Err_emp_name = "Test User",
                Err_ip = "127.0.0.1"
            };

        private async Task InsertAsync<T>(T entity) where T : class
            => await _fsql.Insert(entity).ExecuteAffrowsAsync();

        // =====================================================================
        // SysBaseController
        // =====================================================================

        [Fact]
        public async Task GetSysApp_EmptyAppid_ReturnsEmptyTree()
        {
            await InsertAsync(NewMenu("M1", "APP1", "菜单一", "root", 1));

            var ctrl = CreateSysBaseController();
            WithUid(ctrl, "admin");

            var data = JObject.Parse(await ctrl.GetSysApp(""))["data"] as JArray;

            Assert.Empty(data);
        }

        [Fact]
        public async Task GetSysApp_Admin_ReturnsFullTree()
        {
            await InsertAsync(NewMenu("M1", "APP1", "父菜单", "root", 1));
            await InsertAsync(NewMenu("M2", "APP1", "子菜单", "M1", 2));
            await InsertAsync(NewMenu("MX", "APP2", "其它应用", "root", 1));

            var ctrl = CreateSysBaseController();
            WithUid(ctrl, "admin"); // uid == admin → isAdmin，跳过授权交集

            var arr = (JArray)JObject.Parse(await ctrl.GetSysApp("APP1"))["data"]!;

            Assert.Single(arr);
            Assert.Equal("M1", (string)arr[0]["id"]!);
            Assert.Equal("父菜单", (string)arr[0]["text"]!);
            var children = (JArray)arr[0]["children"]!;
            Assert.Single(children);
            Assert.Equal("M2", (string)children[0]["id"]!);
        }

        [Fact]
        public async Task GetSysApp_NonAdminWithoutAuth_ReturnsEmpty()
        {
            await InsertAsync(NewMenu("M1", "APP1", "菜单一", "root", 1));

            var ctrl = CreateSysBaseController();
            WithUid(ctrl, "EMP2"); // 无任何角色授权

            var arr = (JArray)JObject.Parse(await ctrl.GetSysApp("APP1"))["data"]!;

            Assert.Empty(arr);
        }

        [Fact]
        public async Task GetSysApp_NonAdminWithMenuAuth_ReturnsIntersectedTree()
        {
            await InsertAsync(NewMenu("M1", "APP1", "父菜单", "root", 1));
            await InsertAsync(NewMenu("M2", "APP1", "子菜单", "M1", 2));
            await InsertAsync(NewMenu("M3", "APP1", "未授权菜单", "root", 3));
            await InsertAsync(new Sys_role_emp { id = "RE1", role_id = "R1", emp_id = "EMP3" });
            await InsertAsync(new Sys_authority
            {
                id = "A1",
                Role_id = "R1",
                Auth_type = 2, // 2 = 菜单类授权
                Auth_id = "M1,M2"
            });

            var ctrl = CreateSysBaseController(userId: "EMP3");
            WithUid(ctrl, "EMP3");

            var arr = (JArray)JObject.Parse(await ctrl.GetSysApp("APP1"))["data"]!;

            Assert.Single(arr);
            Assert.Equal("M1", (string)arr[0]["id"]!);
            // M2 在授权列表里（作为 M1 的子节点保留），M3 被过滤掉
            Assert.Single((JArray)arr[0]["children"]!);
            Assert.Equal("M2", (string)arr[0]["children"]![0]["id"]!);
        }

        [Fact]
        public async Task UserTree_NoSidClaim_ReturnsLoginExpired()
        {
            var ctrl = WithNoClaims(CreateSysBaseController());

            var obj = JObject.Parse(await ctrl.UserTree());

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("登录状态已过期", (string)obj["msg"]!);
        }

        [Fact]
        public async Task UserTree_ReturnsDeptTreeWithOnlineEmployee()
        {
            await InsertAsync(new hr_department { id = "D1", dep_name = "研发部", parentid = "root" });
            await InsertAsync(new hr_post { id = "P1", post_name = "张三的岗位", dep_id = "D1", emp_id = "EMP1" });

            var ctrl = CreateSysBaseController(userId: "EMP1", userName: "张三");

            var arr = (JArray)JObject.Parse(await ctrl.UserTree())["data"]!;

            var dept = Assert.Single(arr);
            Assert.Equal("D1", (string)dept["id"]!);
            Assert.Equal("研发部", (string)dept["text"]!);
            var children = (JArray)dept["children"]!;
            var empNode = Assert.Single(children);
            Assert.Equal("P1", (string)empNode["id"]!);
            // 当前用户即 EMP1，TouchAsync 后在线 → 37.png
            Assert.Equal("37.png", (string)empNode["d_icon"]!);
        }

        [Fact]
        public async Task GetOnline_UpdatesAndReturnsCurrentUser()
        {
            var ctrl = CreateSysBaseController(userId: "EMP1", userName: "张三");

            var arr = (JArray)JObject.Parse(await ctrl.GetOnline())["data"]!;

            var self = Assert.Single(arr);
            Assert.Equal("EMP1", (string)self["UserID"]!);
            Assert.Equal("张三", (string)self["UserName"]!);
            Assert.NotEmpty((string)self["LastLogTime"]!);
        }

        [Fact]
        public async Task Icons_ReturnsWellFormedList()
        {
            var ctrl = CreateSysBaseController();

            var arr = (JArray)JObject.Parse(await ctrl.Icons())["data"]!;

            // 测试环境一般无 wwwroot/images/icon 目录 → 空数组；有目录时每项必带 filename
            Assert.All(arr, item => Assert.NotNull(item["filename"]));
        }

        // =====================================================================
        // SysLogController
        // =====================================================================

        [Fact]
        public async Task LogAdd_ReturnsViewResultWithLogTypes()
        {
            var ctrl = CreateSysLogController();

            var result = await ctrl.Add();

            var viewResult = Assert.IsType<ViewResult>(result);
            var logTypes = Assert.IsAssignableFrom<JArray>(viewResult.ViewData["logTypes"]);
            Assert.NotNull(logTypes);
        }

        [Fact]
        public async Task LogGrid_NoFilter_ReturnsAllLogs()
        {
            await InsertAsync(NewLog("L1", "用户登录", new DateTime(2024, 6, 1, 9, 0, 0)));
            await InsertAsync(NewLog("L2", "客户操作", new DateTime(2024, 6, 2, 10, 0, 0)));

            var ctrl = CreateSysLogController();

            var obj = JObject.Parse(await ctrl.Grid(TestControllerHelper.BuildPageView<Sys_log>()));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (long)obj["count"]!);
        }

        [Fact]
        public async Task LogGrid_LogtypeFilter_ReturnsOnlyMatching()
        {
            await InsertAsync(NewLog("L1", "用户登录", new DateTime(2024, 6, 1, 9, 0, 0)));
            await InsertAsync(NewLog("L2", "客户操作", new DateTime(2024, 6, 2, 10, 0, 0)));

            var ctrl = CreateSysLogController(queryString: "?logtype=用户登录");

            var obj = JObject.Parse(await ctrl.Grid(TestControllerHelper.BuildPageView<Sys_log>()));

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("L1", (string)data[0]["id"]!);
        }

        [Fact]
        public async Task LogGrid_DateRangeFilter_ReturnsOnlyInRange()
        {
            await InsertAsync(NewLog("L1", "用户登录", new DateTime(2024, 1, 15, 9, 0, 0)));
            await InsertAsync(NewLog("L2", "用户登录", new DateTime(2024, 6, 15, 9, 0, 0)));

            var ctrl = CreateSysLogController(queryString: "?date1=2024-03-01&date2=2024-12-31");

            var obj = JObject.Parse(await ctrl.Grid(TestControllerHelper.BuildPageView<Sys_log>()));

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("L2", (string)data[0]["id"]!);
        }

        [Fact]
        public async Task LogType_ReturnsDistinctEventTypes()
        {
            await InsertAsync(NewLog("L1", "用户登录", new DateTime(2024, 6, 1, 9, 0, 0)));
            await InsertAsync(NewLog("L2", "客户操作", new DateTime(2024, 6, 2, 10, 0, 0)));
            await InsertAsync(NewLog("L3", "用户登录", new DateTime(2024, 6, 3, 11, 0, 0)));

            var ctrl = CreateSysLogController();

            var obj = JObject.Parse(await ctrl.LogType());

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(2, data.Count);
            Assert.Contains("用户登录", data.Select(t => (string)t!));
            Assert.Contains("客户操作", data.Select(t => (string)t!));
        }

        [Fact]
        public async Task LogDelete_NonAdmin_ReturnsPermissionError()
        {
            var ctrl = CreateSysLogController(userId: "TEST_USER");

            var obj = JObject.Parse(await ctrl.Delete("L1"));

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("只有超级管理员才能删除！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task LogDelete_Admin_DeletesExistingLog()
        {
            await InsertAsync(NewLog("L1", "用户登录", new DateTime(2024, 6, 1, 9, 0, 0)));
            var ctrl = CreateSysLogController(userId: "admin");

            var obj = JObject.Parse(await ctrl.Delete("L1"));

            Assert.Equal(0, (int)obj["code"]!);
            long remaining = await _fsql.Select<Sys_log>().Where(a => a.id == "L1").CountAsync();
            Assert.Equal(0, remaining);
        }

        [Fact]
        public async Task LogDelete_Admin_MissingLog_ReturnsDeleteFailed()
        {
            var ctrl = CreateSysLogController(userId: "admin");

            var obj = JObject.Parse(await ctrl.Delete("NOT_EXIST"));

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("删除失败！", (string)obj["msg"]!);
        }

        // =====================================================================
        // SysLogErrController
        // =====================================================================

        [Fact]
        public async Task ErrGetLogtype_NoSidClaim_ReturnsLoginExpired()
        {
            var ctrl = WithNoClaims(CreateSysLogErrController());

            var obj = JObject.Parse(await ctrl.GetLogtype());

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("登录状态已过期", (string)obj["msg"]!);
        }

        [Fact]
        public async Task ErrGetLogtype_NonFullAuth_ReturnsNoPermission()
        {
            var ctrl = CreateSysLogErrController(authType: 2);

            var obj = JObject.Parse(await ctrl.GetLogtype());

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无操作权限", (string)obj["msg"]!);
        }

        [Fact]
        public async Task ErrGetLogtype_FullAuth_ReturnsTypeDictionary()
        {
            await InsertAsync(NewErr("E1", 2, "数据库错误", new DateTime(2024, 6, 1)));
            await InsertAsync(NewErr("E2", 1, "未捕获异常", new DateTime(2024, 6, 2)));

            var ctrl = CreateSysLogErrController();

            var obj = JObject.Parse(await ctrl.GetLogtype());

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(2, data.Count);
            // typeid 升序
            Assert.Equal(1, (int)data[0]["typeid"]!);
            Assert.Equal("未捕获异常", (string)data[0]["type"]!);
            Assert.Equal(2, (int)data[1]["typeid"]!);
        }

        [Fact]
        public async Task ErrGrid_NonFullAuth_ReturnsNoPermission()
        {
            var ctrl = CreateSysLogErrController(authType: 2);

            var obj = JObject.Parse(await ctrl.Grid(TestControllerHelper.BuildPageView<Sys_log_Err>()));

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无操作权限", (string)obj["msg"]!);
        }

        [Fact]
        public async Task ErrGrid_FullAuth_ReturnsAllErrors()
        {
            await InsertAsync(NewErr("E1", 1, "未捕获异常", new DateTime(2024, 6, 1), message: "msg1"));
            await InsertAsync(NewErr("E2", 1, "未捕获异常", new DateTime(2024, 6, 2), message: "msg2"));

            var ctrl = CreateSysLogErrController();

            var obj = JObject.Parse(await ctrl.Grid(TestControllerHelper.BuildPageView<Sys_log_Err>()));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (long)obj["count"]!);
        }

        [Fact]
        public async Task ErrGrid_LogtypeFilter_ReturnsOnlyMatching()
        {
            await InsertAsync(NewErr("E1", 1, "未捕获异常", new DateTime(2024, 6, 1)));
            await InsertAsync(NewErr("E2", 2, "数据库错误", new DateTime(2024, 6, 2)));

            var ctrl = CreateSysLogErrController(queryString: "?logtype=2");

            var obj = JObject.Parse(await ctrl.Grid(TestControllerHelper.BuildPageView<Sys_log_Err>()));

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("E2", (string)data[0]["id"]!);
        }

        [Fact]
        public async Task ErrGrid_KeywordFilter_MatchesMessageAndTrace()
        {
            await InsertAsync(NewErr("E1", 1, "未捕获异常", new DateTime(2024, 6, 1), message: "订单保存失败"));
            await InsertAsync(NewErr("E2", 1, "未捕获异常", new DateTime(2024, 6, 2), trace: "at OrderService.Save 订单"));

            var ctrl = CreateSysLogErrController(queryString: "?keyword=订单");

            var obj = JObject.Parse(await ctrl.Grid(TestControllerHelper.BuildPageView<Sys_log_Err>()));

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(2, data.Count);
        }

        // =====================================================================
        // HomeController
        // =====================================================================

        [Fact]
        public async Task IniUrl_Admin_ReturnsPortalHomeUrl()
        {
            // [Phase 2 回归保护] iniUrl 已从 /Home/home 改为 /Home/portal，
            // 这是门户工作台成为默认首页的前端入口，禁止回退。
            await InsertAsync(NewInfo("sys_name", "小黄豆"));
            await InsertAsync(NewInfo("sys_logo", "/images/logo.png"));

            var ctrl = CreateHomeController(uid: "admin");

            var obj = JObject.Parse(await ctrl.iniUrl());

            Assert.Equal("/Home/portal", (string)obj["homeInfo"]!["href"]!);
            Assert.Equal("小黄豆", (string)obj["logoInfo"]!["title"]!);
            Assert.Equal("/images/logo.png", (string)obj["logoInfo"]!["image"]!);
            // admin 不走菜单过滤，空菜单表时 menuInfo 为空数组而非 null
            Assert.Empty((JArray)obj["menuInfo"]!);
        }

        [Fact]
        public async Task IniUrl_LongSysName_TruncatedToSixChars()
        {
            await InsertAsync(NewInfo("sys_name", "小黄豆CRM系统"));
            await InsertAsync(NewInfo("sys_logo", "/images/logo.png"));

            var ctrl = CreateHomeController(uid: "admin");

            var obj = JObject.Parse(await ctrl.iniUrl());

            Assert.Equal("小黄豆CRM", (string)obj["logoInfo"]!["title"]!);
        }

        [Fact]
        public async Task IniUrl_NonAdmin_FiltersMenusByAuthority()
        {
            await InsertAsync(NewInfo("sys_name", "小黄豆"));
            await InsertAsync(NewInfo("sys_logo", "/images/logo.png"));
            await InsertAsync(NewMenu("M1", "APP1", "父菜单", "root", 1));
            await InsertAsync(NewMenu("M2", "APP1", "子菜单", "M1", 2));
            await InsertAsync(NewMenu("M3", "APP1", "未授权菜单", "root", 3));
            await InsertAsync(new Sys_role_emp { id = "RE1", role_id = "R1", emp_id = "EMP1" });
            await InsertAsync(new Sys_authority
            {
                id = "A1",
                Role_id = "R1",
                Auth_type = 2,
                Auth_id = "M1,M2"
            });

            var ctrl = CreateHomeController(userId: "EMP1", uid: "EMP1");

            var arr = (JArray)JObject.Parse(await ctrl.iniUrl())["menuInfo"]!;
            // 菜单树带层级（iniUrl 用 "child" 装配下级），递归摊平后再比对
            var ids = FlattenMenuIds(arr).ToList();

            Assert.Contains("M1", ids);
            Assert.Contains("M2", ids);
            Assert.DoesNotContain("M3", ids);
        }

        /// <summary>
        /// 递归摊平菜单树（兼容 "child" / "children" 两种下级键名）。
        /// </summary>
        private static IEnumerable<string> FlattenMenuIds(JArray arr)
        {
            foreach (var item in arr)
            {
                yield return (string)item["id"]!;
                if (item["child"] is JArray childArr)
                {
                    foreach (var sid in FlattenMenuIds(childArr))
                    {
                        yield return sid;
                    }
                }
                if (item["children"] is JArray childrenArr)
                {
                    foreach (var sid in FlattenMenuIds(childrenArr))
                    {
                        yield return sid;
                    }
                }
            }
        }

        [Fact]
        public async Task IniUrl_MissingSysInfo_ReturnsSystemErrorAndSignsOut()
        {
            // 缺 sys_name 键时 dic["sys_name"] 抛 KeyNotFoundException → catch 分支
            // 记日志 + SignOutAsync + 返回「系统错误！」
            var ctrl = CreateHomeController(uid: "admin");

            var obj = JObject.Parse(await ctrl.iniUrl());

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("系统错误！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Home_ReturnsViewResultWithCounts()
        {
            await InsertAsync(new CRM_Customer { id = "C1" });
            await InsertAsync(new CRM_Customer { id = "C2" });

            var ctrl = CreateHomeController();

            var result = ctrl.home();

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal(2L, Convert.ToInt64(viewResult.ViewData["cuscount"]));
            Assert.Equal(0L, Convert.ToInt64(viewResult.ViewData["ordercount"]));
            Assert.Equal(0L, Convert.ToInt64(viewResult.ViewData["receivecount"]));
        }

        [Fact]
        public void Portal_ReturnsViewResultWithUserName()
        {
            var ctrl = CreateHomeController(userName: "张三");

            var result = ctrl.Portal();

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("张三", (string)viewResult.ViewData["user_name"]!);
        }

        [Fact]
        public void Index_NoSysInfo_RedirectsToAccountIndex()
        {
            // 未配置系统信息时回登录页（appsettings.json 由测试兜底准备）
            var ctrl = CreateHomeController();

            var result = ctrl.Index();

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("/Account/index", redirect.Url);
        }

        [Fact]
        public async Task Index_WithSysInfo_ReturnsViewWithCompany()
        {
            await InsertAsync(NewInfo("sys_name", "小黄豆CRM"));

            var ctrl = CreateHomeController();

            var result = ctrl.Index();

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal("小黄豆CRM", (string)viewResult.ViewData["company"]!);
        }
    }
}
