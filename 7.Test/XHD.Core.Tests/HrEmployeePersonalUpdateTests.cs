using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.Services;
using XHD.Core.View.Controllers;

using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// HrEmployeeController.PersonalUpdate 真集成测试（SQLite in-memory + 真实 Service/Repository/日志服务）。
    /// 背景：A 版 Server/hr_employee.cs:321 PersonalUpdate 在 B 版长期缺失——Me.cshtml 保存按钮被注释、
    /// 无对应 action、头像子页回写 #headimg 无隐藏域接值，导致个人资料「只能看不能改」、头像永不落库。
    /// 现补齐 action：自作用域（强制登录态、忽略表单 id）+ 列白名单更新（管理字段不动）+ 变更日志。
    /// 本测试覆盖四条安全/正确性主线：自作用域防越权、列白名单防误改管理字段、headimg 持久化、Sys_log 落库且不含密码哈希。
    /// </summary>
    public class HrEmployeePersonalUpdateTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly hr_employeeRepository _empRepo;
        private readonly Ihr_employeeService _empSvc;
        private readonly DBAuthService _authSvc;
        // 真实日志服务（而非 Moq）：本测试要查库断言日志真的落库
        private readonly Sys_logService _logSvc;

        public HrEmployeePersonalUpdateTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _empRepo = new hr_employeeRepository(_fsql);
            _empSvc = new hr_employeeService(_empRepo);
            _authSvc = new DBAuthService(new DBAuthRepository(_fsql));
            _logSvc = new Sys_logService(new Sys_logRepository(_fsql));
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        /// <summary>
        /// 种入一条「管理字段齐全」的员工，用于验证白名单更新不会动到它们。
        /// </summary>
        private async Task SeedFullEmpAsync(string id)
        {
            await _fsql.Insert(new hr_employee
            {
                id = id,
                name = $"员工-{id}",
                uid = $"uid_{id}",
                dep_id = "D1",
                position_id = "P1",
                post_id = "PO1",
                role_id = "R1",
                status = 1,
                canlogin = 1,
                sort = 5,
                default_city = "北京",
                EntryDate = "2024-01-01",
                pwd = "XHD-PBKDF2$10000$BASE64SALT$BASE64HASH",
                remarks = "原始备注",
                tel = "13800000000",
                email = "old@example.com",
                sex = 1,
                address = "旧地址",
                education = "本科",
                professional = "旧专业",
                schools = "旧学校",
                idcard = "110000000000000000",
                birthday = "1990-01-01",
                headimg = "",
                isDelete = 0
            }).ExecuteAffrowsAsync();
        }

        private async Task<hr_employee> GetEmpAsync(string id) =>
            await _fsql.Select<hr_employee>().Where(a => a.id == id).FirstAsync();

        // ============ Controller 装配 ============

        private HrEmployeeController CreateController(string userId)
        {
            return TestControllerHelper.CreateWithHttpContext<HrEmployeeController>(
                string.Empty, userId, "Test User",
                new Mock<ILogger<HrEmployeeController>>().Object,
                _empSvc,
                new Mock<ICRM_CustomerService>().Object,
                new Mock<ICRM_followService>().Object,
                new Mock<ISale_orderService>().Object,
                new Mock<ISale_contractService>().Object,
                new Mock<IFinance_ReceiveService>().Object,
                new Mock<IFinance_InvoiceService>().Object,
                _logSvc,
                _authSvc);
        }

        /// <summary>
        /// 解析 XHDResult JSON 的 code/msg。
        /// </summary>
        private static (int code, string msg) ParseResult(string json)
        {
            var jo = JObject.Parse(json);
            return (jo.Value<int>("code"), jo.Value<string>("msg") ?? string.Empty);
        }

        // ============ 1. 自作用域：提交他人 id 只能改自己 ============

        [Fact]
        public async Task PersonalUpdate_SelfScope_IgnoresSubmittedId()
        {
            await SeedFullEmpAsync("ME");
            await SeedFullEmpAsync("OTHER");

            // 恶意场景：表单 id 填别人的员工号，试图改他人资料
            var model = new hr_employee { id = "OTHER", name = "被改了", tel = "13900000000" };

            var (code, msg) = ParseResult(await CreateController("ME").PersonalUpdate(model));

            Assert.Equal(0, code);

            // 自己的资料被改
            var me = await GetEmpAsync("ME");
            Assert.Equal("被改了", me.name);
            Assert.Equal("13900000000", me.tel);

            // 他人的资料纹丝不动
            var other = await GetEmpAsync("OTHER");
            Assert.Equal("员工-OTHER", other.name);
            Assert.Equal("13800000000", other.tel);
        }

        // ============ 2. 列白名单：管理字段不被表单值覆盖 ============

        [Fact]
        public async Task PersonalUpdate_Whitelist_AdminFieldsUntouched()
        {
            await SeedFullEmpAsync("ME");

            // 表单里塞满管理字段的非法值（模拟前端伪造提交）
            var model = new hr_employee
            {
                id = "ME",
                name = "张三",
                tel = "13700000000",
                email = "new@example.com",
                sex = 2,
                address = "新地址",
                education = "硕士",
                professional = "新专业",
                schools = "新学校",
                idcard = "120000000000000000",
                birthday = "1991-02-02",
                headimg = "/Upload/Header/2026/new.png",
                // —— 以下全部是非白名单管理字段，必须被忽略 ——
                uid = "HACKED_UID",
                dep_id = "HACKED_DEP",
                position_id = "HACKED_POS",
                post_id = "HACKED_POST",
                role_id = "HACKED_ROLE",
                status = 99,
                canlogin = 99,
                sort = 999,
                default_city = "HACKED_CITY",
                EntryDate = "2099-12-31",
                pwd = "HACKED_PASSWORD_HASH",
                remarks = "HACKED_REMARKS"
            };

            var (code, msg) = ParseResult(await CreateController("ME").PersonalUpdate(model));

            Assert.Equal(0, code);

            var emp = await GetEmpAsync("ME");

            // 白名单字段：已更新
            Assert.Equal("张三", emp.name);
            Assert.Equal("13700000000", emp.tel);
            Assert.Equal("new@example.com", emp.email);
            Assert.Equal(2, emp.sex);
            Assert.Equal("新地址", emp.address);
            Assert.Equal("硕士", emp.education);
            Assert.Equal("新专业", emp.professional);
            Assert.Equal("新学校", emp.schools);
            Assert.Equal("120000000000000000", emp.idcard);
            Assert.Equal("1991-02-02", emp.birthday);
            Assert.Equal("/Upload/Header/2026/new.png", emp.headimg);

            // 管理字段：保持原值（根因防御：不依赖前端是否提交）
            Assert.Equal("uid_ME", emp.uid);
            Assert.Equal("D1", emp.dep_id);
            Assert.Equal("P1", emp.position_id);
            Assert.Equal("PO1", emp.post_id);
            Assert.Equal("R1", emp.role_id);
            Assert.Equal(1, emp.status);
            Assert.Equal(1, emp.canlogin);
            Assert.Equal(5, emp.sort);
            Assert.Equal("北京", emp.default_city);
            Assert.Equal("2024-01-01", emp.EntryDate);
            Assert.Equal("XHD-PBKDF2$10000$BASE64SALT$BASE64HASH", emp.pwd);
            Assert.Equal("原始备注", emp.remarks);
        }

        // ============ 3. headimg 单独更新（头像链路闭环） ============

        [Fact]
        public async Task PersonalUpdate_HeadImg_Persisted()
        {
            await SeedFullEmpAsync("ME");

            var model = new hr_employee
            {
                id = "ME",
                name = "员工-ME",
                tel = "13800000000",
                headimg = "/Upload/Header/2026/10/05/abc.png"
            };

            var (code, msg) = ParseResult(await CreateController("ME").PersonalUpdate(model));

            Assert.Equal(0, code);

            var emp = await GetEmpAsync("ME");
            Assert.Equal("/Upload/Header/2026/10/05/abc.png", emp.headimg);
        }

        // ============ 4. 变更日志：落库、字段正确、不含密码哈希 ============

        [Fact]
        public async Task PersonalUpdate_Change_WritesSysLog_WithoutPassword()
        {
            await SeedFullEmpAsync("ME");

            var model = new hr_employee
            {
                id = "ME",
                name = "张三",
                tel = "13800000000"
            };

            await CreateController("ME").PersonalUpdate(model);

            var logs = await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "个人信息修改")
                .ToListAsync();

            var log = Assert.Single(logs);

            // 事件归属正确（自作用域）
            Assert.Equal("ME", log.EventID);
            Assert.Equal("ME", log.UserID);
            Assert.Equal("张三", log.EventTitle);
            // 身份与 IP 取自 HttpContext
            Assert.Equal("Test User", log.UserName);
            Assert.Equal("127.0.0.1", log.IPStreet);

            // diff 内容包含被改字段的新旧值
            Assert.Contains("【name】", log.Log_Content);
            Assert.Contains("员工-ME", log.Log_Content);
            Assert.Contains("张三", log.Log_Content);

            // 安全断言：密码哈希绝不入日志（after 副本沿用旧值，LogContent 不会 diff 到 pwd）
            Assert.DoesNotContain("XHD-PBKDF2", log.Log_Content);
            Assert.DoesNotContain("BASE64SALT", log.Log_Content);
        }

        // ============ 5. 无变更：成功但不写日志（避免无效日志噪声） ============

        [Fact]
        public async Task PersonalUpdate_NoChange_NoLogRow()
        {
            await SeedFullEmpAsync("ME");

            // 全部白名单字段与旧值完全一致
            var model = new hr_employee
            {
                id = "ME",
                name = "员工-ME",
                tel = "13800000000",
                email = "old@example.com",
                sex = 1,
                address = "旧地址",
                education = "本科",
                professional = "旧专业",
                schools = "旧学校",
                idcard = "110000000000000000",
                birthday = "1990-01-01",
                headimg = ""
            };

            var (code, msg) = ParseResult(await CreateController("ME").PersonalUpdate(model));

            Assert.Equal(0, code);

            var count = await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "个人信息修改")
                .CountAsync();

            Assert.Equal(0, count);
        }

        // ============ 6. 必填校验：姓名 ============

        [Fact]
        public async Task PersonalUpdate_EmptyName_ReturnsError()
        {
            await SeedFullEmpAsync("ME");

            var model = new hr_employee { id = "ME", name = "", tel = "13800000000" };

            var (code, msg) = ParseResult(await CreateController("ME").PersonalUpdate(model));

            Assert.Equal(-1, code);
            Assert.Equal("姓名不能为空！", msg);

            // 校验失败不应动库
            var emp = await GetEmpAsync("ME");
            Assert.Equal("员工-ME", emp.name);
        }

        // ============ 7. 必填校验：电话 ============

        [Fact]
        public async Task PersonalUpdate_EmptyTel_ReturnsError()
        {
            await SeedFullEmpAsync("ME");

            var model = new hr_employee { id = "ME", name = "张三", tel = "" };

            var (code, msg) = ParseResult(await CreateController("ME").PersonalUpdate(model));

            Assert.Equal(-1, code);
            Assert.Equal("电话不能为空！", msg);

            var emp = await GetEmpAsync("ME");
            Assert.Equal("员工-ME", emp.name);
        }

        // ============ 8. 目标记录不存在 ============

        [Fact]
        public async Task PersonalUpdate_EmployeeNotFound_ReturnsError()
        {
            // 库里没有 GHOST 这条员工记录
            var model = new hr_employee { id = "GHOST", name = "张三", tel = "13800000000" };

            var (code, msg) = ParseResult(await CreateController("GHOST").PersonalUpdate(model));

            Assert.Equal(-1, code);
            Assert.Equal("找不到数据！", msg);
        }
    }
}
