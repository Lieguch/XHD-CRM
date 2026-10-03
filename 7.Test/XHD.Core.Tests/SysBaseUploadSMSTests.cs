using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using System.Security.Claims;
using XHD.Core.Common;
using XHD.Core.Common.DEncrypt;
using XHD.Core.Common.SMS;
using XHD.Core.IRepository;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.Services;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 7 单元测试：Sys_base + Sys_info + Sys_upload + Sys_SMS 9 函数。
    ///   #100 Sys_base.GetSysApp
    ///   #101 Sys_base.getUserTree
    ///   #102 Sys_base.GetOnline
    ///   #103 Sys_base.GetIcons
    ///   #115 Sys_info.regSMS（SysInfoController.RegSMS，走控制器管线）
    ///   #119 upload.cus_import（UploadController.CusImport，走控制器管线）
    ///   #120 upload.contact_import（UploadController.ContactImport，走控制器管线）
    ///   #124 SMS.send
    ///   #125 SMS_Helper.getBalance
    /// 全部走 Repository + SQLite 内存库；SMSHelper 与 IFormFile 走 Moq 桥接。
    /// </summary>
    public class SysBaseUploadSMSTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sys_onlineRepository _onlineRepo;
        private readonly SMSRepository _smsRepo;
        private readonly Sys_MenuRepository _menuRepo;
        private readonly Sys_authorityRepository _authRepo;
        private readonly Sys_role_empRepository _roleEmpRepo;
        private readonly hr_departmentRepository _deptRepo;
        private readonly hr_postRepository _postRepo;
        private readonly ISMSHelper _smsHelper;
        private readonly Mock<ISMSHelper> _smsHelperMock;

        public SysBaseUploadSMSTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _onlineRepo = new Sys_onlineRepository(_fsql);
            _smsRepo = new SMSRepository(_fsql);
            _menuRepo = new Sys_MenuRepository(_fsql);
            _authRepo = new Sys_authorityRepository(_fsql);
            _roleEmpRepo = new Sys_role_empRepository(_fsql);
            _deptRepo = new hr_departmentRepository(_fsql);
            _postRepo = new hr_postRepository(_fsql);

            _smsHelperMock = new Mock<ISMSHelper>();
            _smsHelper = _smsHelperMock.Object;
        }

        public void Dispose() => _fsql?.Dispose();

        // ============ #100 GetSysApp ============

        [Fact]
        public async Task GetSysApp_AppidEmpty_ReturnsEmpty()
        {
            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetSysAppAsync("", "EMP1", true);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetSysApp_AdminFiltersByAppidAndReturnsTree()
        {
            await _fsql.Insert(new Sys_Menu { id = "M1", App_id = "APP1", Menu_name = "根", parentid = "root", Menu_order = 1 }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_Menu { id = "M2", App_id = "APP1", Menu_name = "子", parentid = "M1", Menu_order = 1 }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_Menu { id = "M3", App_id = "OTHER", Menu_name = "其他", parentid = "root", Menu_order = 1 }).ExecuteAffrowsAsync();

            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetSysAppAsync("APP1", "EMP1", true);

            Assert.Single(result);
            Assert.Equal("M1", (string)result[0]["id"]);
            Assert.Single(result[0]["children"] as JArray);
            Assert.Equal("M2", (string)(result[0]["children"] as JArray)[0]["id"]);
        }

        [Fact]
        public async Task GetSysApp_NoMenusForApp_ReturnsEmpty()
        {
            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetSysAppAsync("NONEXIST", "EMP1", true);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetSysApp_NonAdminWithNoAuth_ReturnsEmpty()
        {
            await _fsql.Insert(new Sys_Menu { id = "M1", App_id = "APP1", Menu_name = "根", parentid = "root" }).ExecuteAffrowsAsync();
            // 员工 EMP2 无任何角色绑定
            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetSysAppAsync("APP1", "EMP2", false);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetSysApp_NonAdminWithMenuAuth_ReturnsFilteredTree()
        {
            await _fsql.Insert(new Sys_Menu { id = "M1", App_id = "APP1", Menu_name = "允许", parentid = "root" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_Menu { id = "M2", App_id = "APP1", Menu_name = "禁止", parentid = "root" }).ExecuteAffrowsAsync();
            // 员工 EMP3 → 角色 R1 → 授权 M1
            await _fsql.Insert(new Sys_role_emp { id = "RE1", role_id = "R1", emp_id = "EMP3" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_authority { App_id = "APP1", Role_id = "R1", Auth_type = 2, Auth_id = "M1" }).ExecuteAffrowsAsync();

            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetSysAppAsync("APP1", "EMP3", false);

            Assert.Single(result);
            Assert.Equal("M1", (string)result[0]["id"]);
        }

        // ============ #101 getUserTree ============

        [Fact]
        public async Task GetUserTree_EmptyEmpId_ReturnsEmpty()
        {
            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetUserTreeAsync("", "张三");
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetUserTree_EmptyDept_ReturnsEmpty()
        {
            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetUserTreeAsync("EMP1", "张三");
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetUserTree_SingleDeptWithEmployee_MarksOffline()
        {
            await _fsql.Insert(new hr_department { id = "D1", dep_name = "销售部", parentid = "root" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new hr_post { id = "P1", dep_id = "D1", emp_id = "EMP1", post_name = "销售员" }).ExecuteAffrowsAsync();

            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetUserTreeAsync("EMP9", "李四"); // 非 EMP1

            // 顶层为销售部，含 1 个子节点（EMP1 岗位），d_icon=93.png（离线）
            Assert.Single(result);
            var children = result[0]["children"] as JArray;
            Assert.NotNull(children);
            Assert.Single(children);
            Assert.Equal("93.png", (string)children[0]["d_icon"]);
        }

        [Fact]
        public async Task GetUserTree_OnlineEmployee_MarkedAsOnline()
        {
            await _fsql.Insert(new hr_department { id = "D1", dep_name = "销售部", parentid = "root" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new hr_post { id = "P1", dep_id = "D1", emp_id = "EMP1", post_name = "销售员" }).ExecuteAffrowsAsync();
            // EMP1 已在线
            await _fsql.Insert(new Sys_online { UserID = "EMP1", UserName = "张三", LastLogTime = DateTime.Now }).ExecuteAffrowsAsync();

            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetUserTreeAsync("EMP2", "李四");

            var children = result[0]["children"] as JArray;
            Assert.Single(children);
            Assert.Equal("37.png", (string)children[0]["d_icon"]);
        }

        // ============ #102 GetOnline ============

        [Fact]
        public async Task GetOnline_EmptyEmpId_ReturnsEmpty()
        {
            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetOnlineAsync("", "张三");
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetOnline_FirstCall_InsertsAndReturnsSelf()
        {
            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetOnlineAsync("EMP1", "张三");

            Assert.Single(result);
            Assert.Equal("EMP1", (string)result[0]["UserID"]);
            Assert.Equal("张三", (string)result[0]["UserName"]);
            Assert.False(string.IsNullOrEmpty((string)result[0]["LastLogTime"]));
        }

        [Fact]
        public async Task GetOnline_SecondCall_UpdatesExistingRow()
        {
            // 先插入一条较新的记录
            await _fsql.Insert(new Sys_online { UserID = "EMP1", UserName = "张三", LastLogTime = DateTime.Now }).ExecuteAffrowsAsync();

            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetOnlineAsync("EMP1", "张三-改");

            Assert.Single(result);
            Assert.Equal("张三-改", (string)result[0]["UserName"]);
        }

        [Fact]
        public async Task GetOnline_PurgesStaleUsers()
        {
            // 一条僵尸（10 分钟前）+ 一条新鲜（刚）
            await _fsql.Insert(new Sys_online { UserID = "STALE", UserName = "僵尸", LastLogTime = DateTime.Now.AddMinutes(-10) }).ExecuteAffrowsAsync();

            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetOnlineAsync("FRESH", "新鲜");

            // 僵尸被清理，仅返回当前用户
            Assert.Single(result);
            Assert.Equal("FRESH", (string)result[0]["UserID"]);
        }

        [Fact]
        public async Task GetOnline_MultipleActiveUsers_ReturnsAll()
        {
            await _fsql.Insert(new Sys_online { UserID = "A", UserName = "甲", LastLogTime = DateTime.Now }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_online { UserID = "B", UserName = "乙", LastLogTime = DateTime.Now }).ExecuteAffrowsAsync();

            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetOnlineAsync("C", "丙");

            Assert.Equal(3, result.Count);
        }

        // ============ #103 GetIcons ============

        [Fact]
        public async Task GetIcons_DirMissing_ReturnsEmpty()
        {
            var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
            var result = await svc.GetIconsAsync(Path.Combine(Path.GetTempPath(), "nonexistent_dir_xyz"));
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetIcons_DirWithFiles_ReturnsList()
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "sprint7_icons_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            try
            {
                File.WriteAllText(Path.Combine(tmpDir, "a.png"), "x");
                File.WriteAllText(Path.Combine(tmpDir, "b.png"), "y");

                var svc = new Sys_baseService(_menuRepo, _onlineRepo, _authRepo, _roleEmpRepo, _deptRepo, _postRepo);
                var result = await svc.GetIconsAsync(tmpDir);

                Assert.Equal(2, result.Count);
                var names = result.Select(r => (string)r["filename"]).ToHashSet();
                Assert.Contains("a.png", names);
                Assert.Contains("b.png", names);
            }
            finally
            {
                Directory.Delete(tmpDir, true);
            }
        }

        // ============ #115 regSMS（SysInfoController.RegSMS） ============

        /// <summary>
        /// 构造 SysInfoController，注入真实 Sys_infoService（走 SQLite 内存库）
        /// 与 Moq 桥接的 ISMSHelper，并模拟登录态。
        /// </summary>
        private SysInfoController CreateSysInfoController()
        {
            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var ctrl = new SysInfoController(
                new Mock<ILogger<SysLogController>>().Object,
                infoService,
                _smsHelper);

            var httpCtx = new DefaultHttpContext();
            httpCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, "TEST_USER"),
                new Claim(ClaimTypes.Name, "Test User")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return ctrl;
        }

        [Fact]
        public async Task RegSMS_HelperSucceeds_WritesThreeSysInfoRows()
        {
            // 预置三条 sys_key（与 A 侧 sys_info 种子数据语义一致）
            foreach (var k in new[] { "sms_no", "sms_key", "sms_done" })
            {
                await _fsql.Insert(new Sys_info { sys_key = k, sys_value = "" }).ExecuteAffrowsAsync();
            }

            _smsHelperMock.Setup(h => h.RegistEx("SER123", "KEYABC", "KEYABC")).Returns(0);
            var ctrl = CreateSysInfoController();

            var json = await ctrl.RegSMS("SER123", "KEYABC");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            _smsHelperMock.Verify(h => h.RegistEx("SER123", "KEYABC", "KEYABC"), Times.Once);

            var no = await _fsql.Select<Sys_info>().Where(a => a.sys_key == "sms_no").FirstAsync();
            var key = await _fsql.Select<Sys_info>().Where(a => a.sys_key == "sms_key").FirstAsync();
            var done = await _fsql.Select<Sys_info>().Where(a => a.sys_key == "sms_done").FirstAsync();

            Assert.Equal("SER123", no.sys_value);
            Assert.Equal(DESEncrypt.Encrypt("KEYABC"), key.sys_value);
            Assert.NotEqual("KEYABC", key.sys_value); // 已加密
            Assert.Equal("1", done.sys_value);
        }

        [Fact]
        public async Task RegSMS_HelperFails_LeavesSmsDoneEmpty()
        {
            foreach (var k in new[] { "sms_no", "sms_key", "sms_done" })
            {
                await _fsql.Insert(new Sys_info { sys_key = k, sys_value = "" }).ExecuteAffrowsAsync();
            }

            _smsHelperMock.Setup(h => h.RegistEx(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(-1);
            var ctrl = CreateSysInfoController();

            var json = await ctrl.RegSMS("SER123", "KEYABC");
            var obj = JObject.Parse(json);

            // 失败：返回 Error（code=-1，消息为错误码中文映射），且不写 sms_done="1"
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal(ISMSHelper.SmsResult(-1), (string)obj["msg"]!);

            var done = await _fsql.Select<Sys_info>().Where(a => a.sys_key == "sms_done").FirstAsync();
            Assert.NotEqual("1", done.sys_value);
        }

        [Fact]
        public async Task RegSMS_EmptySerialNo_ReturnsError()
        {
            var ctrl = CreateSysInfoController();

            var json = await ctrl.RegSMS("", "KEYABC");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("序列号不能为空", (string)obj["msg"]!);
            // 参数校验失败，不应写入任何配置
            Assert.Empty(await _fsql.Select<Sys_info>().ToListAsync());
        }

        [Fact]
        public async Task RegSMS_EmptyKey_ReturnsError()
        {
            var ctrl = CreateSysInfoController();

            var json = await ctrl.RegSMS("SER123", "");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("密钥不能为空", (string)obj["msg"]!);
            Assert.Empty(await _fsql.Select<Sys_info>().ToListAsync());
        }

        // ============ #119 cus_import / #120 contact_import（UploadController） ============

        /// <summary>
        /// 用 Moq 构造一个 IFormFile，从 byte[] 提供 Stream（供 Controller 测试用）。
        /// </summary>
        private static IFormFile CreateFormFile(string fileName, byte[] bytes)
        {
            var mock = new Mock<IFormFile>();
            mock.Setup(f => f.FileName).Returns(fileName);
            mock.Setup(f => f.Length).Returns(bytes.Length);
            mock.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(bytes));
            mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>()))
                .Returns((Stream target) =>
                {
                    using (var ms = new MemoryStream(bytes))
                    {
                        return ms.CopyToAsync(target);
                    }
                });
            return mock.Object;
        }

        /// <summary>
        /// 构造 UploadController，依赖均走 Moq（CusImport/ContactImport 不触业务 service）。
        /// </summary>
        private UploadController CreateUploadController()
        {
            var ctrl = new UploadController(
                new Mock<ICRM_Customer_attaService>().Object,
                new Mock<ILogger<UploadController>>().Object);

            var httpCtx = new DefaultHttpContext();
            httpCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, "TEST_USER"),
                new Claim(ClaimTypes.Name, "Test User")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return ctrl;
        }

        [Fact]
        public async Task CusImport_HappyPath_SavesToFixedNameAndReturnsFilename()
        {
            // UploadController.CusImport 落盘到 <cwd>/wwwroot/file/customer/Customer.xls
            var fullDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "file", "customer");
            try
            {
                var ctrl = CreateUploadController();
                var file = CreateFormFile("客户导入模板.xls", new byte[] { (byte)'N', (byte)'E', (byte)'W' });

                var json = await ctrl.CusImport(file);
                var obj = JObject.Parse(json);

                Assert.Equal(0, (int)obj["code"]!);
                Assert.Equal("Customer.xls", (string)obj["msg"]!);

                var savedPath = Path.Combine(fullDir, "Customer.xls");
                Assert.True(File.Exists(savedPath));
                Assert.Equal("NEW", File.ReadAllText(savedPath));
            }
            finally
            {
                if (Directory.Exists(fullDir)) Directory.Delete(fullDir, true);
            }
        }

        [Fact]
        public async Task CusImport_OverwritesExisting()
        {
            var fullDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "file", "customer");
            Directory.CreateDirectory(fullDir);
            var fullPath = Path.Combine(fullDir, "Customer.xls");
            File.WriteAllText(fullPath, "OLD");
            try
            {
                var ctrl = CreateUploadController();
                var file = CreateFormFile("客户导入模板.xls", new byte[] { (byte)'N', (byte)'E', (byte)'W' });

                var json = await ctrl.CusImport(file);
                var obj = JObject.Parse(json);

                Assert.Equal(0, (int)obj["code"]!);
                Assert.Equal("NEW", File.ReadAllText(fullPath));
            }
            finally
            {
                if (Directory.Exists(fullDir)) Directory.Delete(fullDir, true);
            }
        }

        [Fact]
        public async Task ContactImport_HappyPath_SavesToFixedNameAndReturnsFilename()
        {
            // UploadController.ContactImport 落盘到 <cwd>/wwwroot/file/contact/contact.xls
            var fullDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "file", "contact");
            try
            {
                var ctrl = CreateUploadController();
                var file = CreateFormFile("联系人导入模板.xls", new byte[] { (byte)'N', (byte)'E', (byte)'W' });

                var json = await ctrl.ContactImport(file);
                var obj = JObject.Parse(json);

                Assert.Equal(0, (int)obj["code"]!);
                Assert.Equal("contact.xls", (string)obj["msg"]!);

                var savedPath = Path.Combine(fullDir, "contact.xls");
                Assert.True(File.Exists(savedPath));
                Assert.Equal("NEW", File.ReadAllText(savedPath));
            }
            finally
            {
                if (Directory.Exists(fullDir)) Directory.Delete(fullDir, true);
            }
        }

        [Fact]
        public async Task ContactImport_OverwritesExisting()
        {
            var fullDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "file", "contact");
            Directory.CreateDirectory(fullDir);
            var fullPath = Path.Combine(fullDir, "contact.xls");
            File.WriteAllText(fullPath, "OLD");
            try
            {
                var ctrl = CreateUploadController();
                var file = CreateFormFile("联系人导入模板.xls", new byte[] { (byte)'N', (byte)'E', (byte)'W' });

                var json = await ctrl.ContactImport(file);
                var obj = JObject.Parse(json);

                Assert.Equal(0, (int)obj["code"]!);
                Assert.Equal("NEW", File.ReadAllText(fullPath));
            }
            finally
            {
                if (Directory.Exists(fullDir)) Directory.Delete(fullDir, true);
            }
        }

        // ============ #124 SMS.send ============

        [Fact]
        public async Task Send_HappyPath_UpdatesStateAndCallsHelper()
        {
            var smsId = Guid.NewGuid().ToString();
            await _fsql.Insert(new SMS { id = smsId, sms_title = "T", sms_content = "【XHD】测试", sms_mobiles = "13800000000,13900000000", isSend = 0, create_id = "EMP1", create_time = DateTime.Now }).ExecuteAffrowsAsync();
            // 配置 SMS
            await _fsql.Insert(new Sys_info { sys_key = "sms_no", sys_value = "SER123" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_info { sys_key = "sms_key", sys_value = DESEncrypt.Encrypt("KEYABC") }).ExecuteAffrowsAsync();

            _smsHelperMock.Setup(h => h.SendSMS("SER123", "KEYABC", It.IsAny<string[]>(), "【XHD】测试", It.IsAny<long>())).Returns(0);

            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var svc = new SMSService(_smsRepo, infoService, _smsHelperMock.Object);

            var result = await svc.SendAsync(smsId, "EMP2");

            _smsHelperMock.Verify(h => h.SendSMS("SER123", "KEYABC", It.IsAny<string[]>(), "【XHD】测试", It.IsAny<long>()), Times.Once);
            var sms = await _fsql.Select<SMS>().Where(a => a.id == smsId).FirstAsync();
            Assert.Equal(1, sms.isSend);
            Assert.Equal("EMP2", sms.check_id);
            Assert.NotNull(sms.sendtime);
            Assert.Contains("发送成功", result);
        }

        [Fact]
        public async Task Send_HelperFails_ReturnsError()
        {
            var smsId = Guid.NewGuid().ToString();
            await _fsql.Insert(new SMS { id = smsId, sms_content = "内容", sms_mobiles = "13800000000", isSend = 0 }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_info { sys_key = "sms_no", sys_value = "SER" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_info { sys_key = "sms_key", sys_value = DESEncrypt.Encrypt("K") }).ExecuteAffrowsAsync();

            _smsHelperMock.Setup(h => h.SendSMS(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<long>())).Returns(-2);

            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var svc = new SMSService(_smsRepo, infoService, _smsHelperMock.Object);

            var result = await svc.SendAsync(smsId, "EMP1");

            Assert.Contains("剩余短信", result);
            var sms = await _fsql.Select<SMS>().Where(a => a.id == smsId).FirstAsync();
            Assert.Equal(0, sms.isSend); // 未更新
        }

        [Fact]
        public async Task Send_NonExistentId_ReturnsError()
        {
            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var svc = new SMSService(_smsRepo, infoService, _smsHelperMock.Object);

            var result = await svc.SendAsync(Guid.NewGuid().ToString(), "EMP1");

            Assert.Contains("无数据", result);
        }

        [Fact]
        public async Task Send_EmptyId_ReturnsError()
        {
            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var svc = new SMSService(_smsRepo, infoService, _smsHelperMock.Object);

            var result = await svc.SendAsync("", "EMP1");

            Assert.Contains("参数错误", result);
        }

        [Fact]
        public async Task Send_NoSmsConfig_ReturnsConfigError()
        {
            var smsId = Guid.NewGuid().ToString();
            await _fsql.Insert(new SMS { id = smsId, sms_content = "内容", sms_mobiles = "138" }).ExecuteAffrowsAsync();
            // 无 sys_info sms_no/sms_key

            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var svc = new SMSService(_smsRepo, infoService, _smsHelperMock.Object);

            var result = await svc.SendAsync(smsId, "EMP1");

            Assert.Contains("未配置", result);
        }

        [Fact]
        public async Task Send_InvalidGuidId_ReturnsError()
        {
            await _fsql.Insert(new Sys_info { sys_key = "sms_no", sys_value = "SER" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_info { sys_key = "sms_key", sys_value = DESEncrypt.Encrypt("K") }).ExecuteAffrowsAsync();
            await _fsql.Insert(new SMS { id = "not-a-guid", sms_content = "内容", sms_mobiles = "138" }).ExecuteAffrowsAsync();

            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var svc = new SMSService(_smsRepo, infoService, _smsHelperMock.Object);

            var result = await svc.SendAsync("not-a-guid", "EMP1");

            Assert.Contains("ID 格式错误", result);
        }

        [Fact]
        public async Task Send_HelperThrows_ReturnsError()
        {
            var smsId = Guid.NewGuid().ToString();
            await _fsql.Insert(new SMS { id = smsId, sms_content = "内容", sms_mobiles = "138" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_info { sys_key = "sms_no", sys_value = "SER" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_info { sys_key = "sms_key", sys_value = DESEncrypt.Encrypt("K") }).ExecuteAffrowsAsync();

            _smsHelperMock.Setup(h => h.SendSMS(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<long>())).Throws(new Exception("net error"));

            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var svc = new SMSService(_smsRepo, infoService, _smsHelperMock.Object);

            var result = await svc.SendAsync(smsId, "EMP1");

            Assert.Contains("短信发送异常", result);
        }

        // ============ #125 getBalance ============

        [Fact]
        public async Task GetBalance_HappyPath_ReturnsBalance()
        {
            await _fsql.Insert(new Sys_info { sys_key = "sms_no", sys_value = "SER" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_info { sys_key = "sms_key", sys_value = DESEncrypt.Encrypt("K") }).ExecuteAffrowsAsync();
            _smsHelperMock.Setup(h => h.GetBalance("SER", "K")).Returns(1234.5);

            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var svc = new SMSService(_smsRepo, infoService, _smsHelperMock.Object);

            var balance = await svc.GetBalanceAsync();

            Assert.Equal(1234.5, balance);
        }

        [Fact]
        public async Task GetBalance_NoConfig_ReturnsZero()
        {
            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var svc = new SMSService(_smsRepo, infoService, _smsHelperMock.Object);

            var balance = await svc.GetBalanceAsync();

            Assert.Equal(0, balance);
        }

        [Fact]
        public async Task GetBalance_HelperThrows_ReturnsZero()
        {
            await _fsql.Insert(new Sys_info { sys_key = "sms_no", sys_value = "SER" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_info { sys_key = "sms_key", sys_value = DESEncrypt.Encrypt("K") }).ExecuteAffrowsAsync();
            _smsHelperMock.Setup(h => h.GetBalance(It.IsAny<string>(), It.IsAny<string>())).Throws(new Exception("boom"));

            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var svc = new SMSService(_smsRepo, infoService, _smsHelperMock.Object);

            var balance = await svc.GetBalanceAsync();

            Assert.Equal(0, balance);
        }

        [Fact]
        public async Task GetBalance_DecryptedKeyUsed()
        {
            await _fsql.Insert(new Sys_info { sys_key = "sms_no", sys_value = "SER" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_info { sys_key = "sms_key", sys_value = DESEncrypt.Encrypt("MYSECRET") }).ExecuteAffrowsAsync();
            _smsHelperMock.Setup(h => h.GetBalance("SER", "MYSECRET")).Returns(99.0);

            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var svc = new SMSService(_smsRepo, infoService, _smsHelperMock.Object);

            var balance = await svc.GetBalanceAsync();

            _smsHelperMock.Verify(h => h.GetBalance("SER", "MYSECRET"), Times.Once);
            Assert.Equal(99.0, balance);
        }
    }
}
