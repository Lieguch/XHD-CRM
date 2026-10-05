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
    /// HrEmployeeController.Grid 数据权限过滤测试（真集成：SQLite in-memory + 真实 Service/Repository）。
    /// 背景：B 版员工选择器是单页表格弹层（8 处前端 tableSelect 调 Grid），
    /// 原先 Grid 无任何数据权限过滤，任何登录用户都能选到全公司员工。
    /// 现以 DataScope 统一收口：authtype&lt;5 只返回权限范围内员工，admin/全部权限返回全部（除 admin）。
    /// 对应 A 版 View/HR/Getemp_Auth.aspx 语义（A 版 Server/hr_employee.cs:80-96 服务端过滤已被 A 版整体注释失效，不复活）。
    /// </summary>
    public class HrEmployeeGridAuthTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly hr_employeeRepository _empRepo;
        private readonly Ihr_employeeService _empSvc;
        private readonly DBAuthService _authSvc;

        public HrEmployeeGridAuthTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _empRepo = new hr_employeeRepository(_fsql);
            _empSvc = new hr_employeeService(_empRepo);
            _authSvc = new DBAuthService(new DBAuthRepository(_fsql));
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static hr_employee NewEmp(string id, string depId = "", string roleId = "") =>
            new hr_employee
            {
                id = id,
                name = $"员工-{id}",
                dep_id = depId,
                role_id = roleId,
                isDelete = 0
            };

        private static hr_department NewDept(string id) =>
            new hr_department { id = id, dep_name = $"部门-{id}" };

        private static Sys_role NewRole(string id, int dataAuth) =>
            new Sys_role { id = id, RoleName = $"角色-{id}", DataAuth = dataAuth };

        private async Task InsertDeptAsync(string id) =>
            await _fsql.Insert(NewDept(id)).ExecuteAffrowsAsync();

        private async Task InsertRoleAsync(string id, int dataAuth) =>
            await _fsql.Insert(NewRole(id, dataAuth)).ExecuteAffrowsAsync();

        private async Task InsertEmpAsync(string id, string depId = "", string roleId = "") =>
            await _fsql.Insert(NewEmp(id, depId, roleId)).ExecuteAffrowsAsync();

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
                new Mock<ISys_logService>().Object,
                _authSvc);
        }

        private static async Task<XHDData<hr_employee>> CallGrid(HrEmployeeController ctrl)
        {
            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<hr_employee>());
            // XHDData<T>.ToString() 输出 {code,msg,data,count}，与 CustomerControllerTests 同样的解析方式
            return JsonConvert.DeserializeObject<XHDData<hr_employee>>(JObject.Parse(json).ToString());
        }

        // ============ authtype=1 本人：只能选到自己 ============

        [Fact]
        public async Task Grid_AuthType1_SelfOnly_ReturnsOnlySelf()
        {
            await InsertDeptAsync("D1");
            await InsertRoleAsync("R1", 1);
            await InsertEmpAsync("ME", "D1", "R1");
            // 同部门其他员工，必须不可见
            await InsertEmpAsync("E2", "D1");
            await InsertEmpAsync("E3", "D1");

            var data = await CallGrid(CreateController("ME"));

            Assert.Equal(1, data.count);
            Assert.Single(data.data);
            Assert.Equal("ME", data.data[0].id);
        }

        // ============ authtype=2 本部：只返回本部门员工 ============

        [Fact]
        public async Task Grid_AuthType2_SameDept_ReturnsOnlyDeptEmployees()
        {
            await InsertDeptAsync("D1");
            await InsertDeptAsync("D2");
            await InsertRoleAsync("R2", 2);
            await InsertEmpAsync("ME", "D1", "R2");
            await InsertEmpAsync("E2", "D1");
            await InsertEmpAsync("E3", "D2");

            var data = await CallGrid(CreateController("ME"));

            Assert.Equal(2, data.count);
            var ids = data.data.Select(e => e.id).ToHashSet();
            Assert.Contains("ME", ids);
            Assert.Contains("E2", ids);
            Assert.DoesNotContain("E3", ids);
        }

        // ============ authtype=5 全部 / admin：返回全部（除 admin）============

        [Fact]
        public async Task Grid_Admin_ReturnsAllExceptAdmin()
        {
            await InsertDeptAsync("D1");
            await InsertDeptAsync("D2");
            await InsertEmpAsync("admin", "D1");
            await InsertEmpAsync("E1", "D1");
            await InsertEmpAsync("E2", "D2");
            await InsertEmpAsync("E3", "D2");

            var data = await CallGrid(CreateController("admin"));

            Assert.Equal(3, data.count);
            var ids = data.data.Select(e => e.id).ToHashSet();
            Assert.Contains("E1", ids);
            Assert.Contains("E2", ids);
            Assert.Contains("E3", ids);
            Assert.DoesNotContain("admin", ids);
        }

        // ============ 既有 keyword 逻辑保持不变（回归）============

        [Fact]
        public async Task Grid_AuthType2_WithKeyword_StillScopedAndFiltered()
        {
            await InsertDeptAsync("D1");
            await InsertDeptAsync("D2");
            await InsertRoleAsync("R2", 2);
            await InsertEmpAsync("ME", "D1", "R2");
            await InsertEmpAsync("E2_Zhang", "D1");
            await InsertEmpAsync("E3_Li", "D2");

            var ctrl = TestControllerHelper.CreateWithHttpContext<HrEmployeeController>(
                "keyword=Zhang", "ME", "Test User",
                new Mock<ILogger<HrEmployeeController>>().Object,
                _empSvc,
                new Mock<ICRM_CustomerService>().Object,
                new Mock<ICRM_followService>().Object,
                new Mock<ISale_orderService>().Object,
                new Mock<ISale_contractService>().Object,
                new Mock<IFinance_ReceiveService>().Object,
                new Mock<IFinance_InvoiceService>().Object,
                new Mock<ISys_logService>().Object,
                _authSvc);

            var data = await CallGrid(ctrl);

            // 数据权限先收口到本部（ME、E2_Zhang），再按 keyword 过滤
            Assert.Equal(1, data.count);
            Assert.Equal("E2_Zhang", data.data[0].id);
        }
    }
}
