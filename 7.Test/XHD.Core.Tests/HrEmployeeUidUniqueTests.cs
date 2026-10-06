using System;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.Extensions.Logging;
using Moq;
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
    /// HrEmployeeController.Save 登录账号（uid）唯一性真集成测试（SQLite in-memory + 真实 Service/Repository）。
    /// 背景：A 版 Server/hr_employee.cs:258 新增分支有 ExistUid（:159）校验，B 版 Save 新增时
    /// 完全不校验，两个员工可共用同一登录账号，登录链路（按 uid 查员工）会撞车。
    /// 修复：新增分支拦截重复 uid；编辑分支排除自身后拦截被他人占用的 uid（uid 可被编辑，
    /// hr_employeeRepository.UpdateAsync 的 IgnoreColumns 不含 uid），uid 等于自身不误报。
    /// B 版收紧为 uid 单字段：A 版 uid+name 组合条件会漏掉「同 uid 不同 name」的重复。
    /// </summary>
    public class HrEmployeeUidUniqueTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Ihr_employeeService _empSvc;
        private readonly DBAuthService _authSvc;
        private readonly Sys_logService _logSvc;

        public HrEmployeeUidUniqueTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            var empRepo = new hr_employeeRepository(_fsql);
            _empSvc = new hr_employeeService(empRepo);
            _authSvc = new DBAuthService(new DBAuthRepository(_fsql));
            _logSvc = new Sys_logService(new Sys_logRepository(_fsql));
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ Controller 装配 ============

        /// <summary>
        /// 用 admin 身份装配：DBAuthService.GetAuth 对 admin 直接返回 true，
        /// 免种按钮权限即可通过 Save 的 hr_employee|add / hr_employee|edit 授权。
        /// </summary>
        private HrEmployeeController CreateController()
        {
            return TestControllerHelper.CreateWithHttpContext<HrEmployeeController>(
                string.Empty, "admin", "管理员",
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

        private static (int code, string msg) ParseResult(string json)
        {
            var jo = JObject.Parse(json);
            return (jo.Value<int>("code"), jo.Value<string>("msg") ?? string.Empty);
        }

        private async Task SeedEmpAsync(string id, string uid, string name)
        {
            await _fsql.Insert(new hr_employee
            {
                id = id,
                uid = uid,
                name = name,
                pwd = "X",
                isDelete = 0
            }).ExecuteAffrowsAsync();
        }

        private async Task<long> CountByUidAsync(string uid) =>
            await _fsql.Select<hr_employee>().Where(a => a.uid == uid).CountAsync();

        // ============ 1. 新增重复 uid：返回错误且不落库 ============

        [Fact]
        public async Task Save_NewEmployee_DuplicateUid_ReturnsError_NoRowInserted()
        {
            await SeedEmpAsync("E1", "dup_uid", "张三");

            var model = new hr_employee { uid = "dup_uid", name = "李四" };

            var (code, msg) = ParseResult(await CreateController().Save(model));

            Assert.Equal(-1, code);
            Assert.Equal("登录账号已存在", msg);

            // 未落库：dup_uid 仍只有 1 条（种子数据），且总数仍为 1
            Assert.Equal(1, await CountByUidAsync("dup_uid"));
            Assert.Equal(1, await _fsql.Select<hr_employee>().CountAsync());
        }

        // ============ 2. 同 uid 不同 name 也拦截（B 版收紧，修 A 版组合条件漏洞） ============

        [Fact]
        public async Task Save_NewEmployee_SameUidDifferentName_StillBlocked()
        {
            await SeedEmpAsync("E1", "dup_uid", "张三");

            // A 版 ExistUid 是 uid AND name 组合（Server/hr_employee.cs:161），
            // 此场景在 A 版会漏过；B 版按 uid 单字段必须拦截
            var model = new hr_employee { uid = "dup_uid", name = "完全不同的名字" };

            var (code, _) = ParseResult(await CreateController().Save(model));

            Assert.Equal(-1, code);
            Assert.Equal(1, await _fsql.Select<hr_employee>().CountAsync());
        }

        // ============ 3. 新增不重复 uid：成功且落库 ============

        [Fact]
        public async Task Save_NewEmployee_UniqueUid_Success()
        {
            await SeedEmpAsync("E1", "taken_uid", "张三");

            var model = new hr_employee { uid = "fresh_uid", name = "王五" };

            var (code, _) = ParseResult(await CreateController().Save(model));

            Assert.Equal(0, code);
            Assert.Equal(1, await CountByUidAsync("fresh_uid"));
            Assert.Equal(2, await _fsql.Select<hr_employee>().CountAsync());
        }

        // ============ 4. 编辑员工时 uid 等于自身：不误报 ============

        [Fact]
        public async Task Save_Edit_UidSameAsSelf_NoFalsePositive()
        {
            await SeedEmpAsync("E1", "u_self", "张三");

            // 只改名字，uid 保持自身——不得被当成重复拦截
            var model = new hr_employee { id = "E1", uid = "u_self", name = "张三丰" };

            var (code, _) = ParseResult(await CreateController().Save(model));

            Assert.Equal(0, code);
            Assert.Equal(1, await CountByUidAsync("u_self"));
        }

        // ============ 5. 编辑员工时改成他人已占用的 uid：拦截且原值不变 ============

        [Fact]
        public async Task Save_Edit_UidTakenByOther_ReturnsError()
        {
            await SeedEmpAsync("E1", "u_a", "张三");
            await SeedEmpAsync("E2", "u_b", "李四");

            // 把 E1 的 uid 改成 E2 已占用的 u_b
            var model = new hr_employee { id = "E1", uid = "u_b", name = "张三" };

            var (code, msg) = ParseResult(await CreateController().Save(model));

            Assert.Equal(-1, code);
            Assert.Equal("登录账号已存在", msg);

            // E1 的 uid 保持原值
            var e1 = await _fsql.Select<hr_employee>().Where(a => a.id == "E1").FirstAsync();
            Assert.Equal("u_a", e1.uid);
        }
    }
}