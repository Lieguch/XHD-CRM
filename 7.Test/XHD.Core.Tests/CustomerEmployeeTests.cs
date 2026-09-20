using FreeSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;
using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 4 Wave 1b 单元测试：客户池 + 员工模块 5 函数。
    ///   #01 CustomerController.Regain（客户重取 / 回收站恢复）
    ///   #02 CustomerController.UpdateApp（移动端客户更新，简化字段子集）
    ///   #10 HrEmployeeController.Exist（员工唯一性校验）
    ///   #11 HrEmployeeController.getDefaultCity（读取员工默认城市）
    ///   #12 HrEmployeeController.updateDefaultCity（更新员工默认城市）
    /// </summary>
    public class CustomerEmployeeTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly CRM_CustomerRepository _custRepo;
        private readonly hr_employeeRepository _empRepo;

        public CustomerEmployeeTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _custRepo = new CRM_CustomerRepository(_fsql);
            _empRepo = new hr_employeeRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static CRM_Customer NewCustomer(string id, string empId = "E1", int isDelete = 0, int state = 0)
        {
            return new CRM_Customer
            {
                id = id,
                cus_name = $"客户-{id}",
                cus_add = "旧地址",
                cus_tel = "13900000000",
                emp_id = empId,
                create_id = empId,
                create_time = new DateTime(2024, 6, 15),
                isDelete = isDelete,
                isPrivate = 1,
                state = state,
                sn = $"CU-{id}",
                Delete_time = isDelete == 1 ? new DateTime(2024, 7, 1) : (DateTime?)null,
                Delete_id = isDelete == 1 ? "DELETER" : string.Empty
            };
        }

        private static hr_employee NewEmployee(string id, string uid = "uid1", string name = "张三", string defaultCity = "")
        {
            return new hr_employee
            {
                id = id,
                uid = uid,
                name = name,
                default_city = defaultCity,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        private async Task InsertCustomerAsync(CRM_Customer c)
            => await _fsql.Insert(c).ExecuteAffrowsAsync();

        private async Task InsertEmployeeAsync(hr_employee e)
            => await _fsql.Insert(e).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        /// <summary>
        /// 全公司权限（authtype=4）+ 可选按钮权限开关。
        /// </summary>
        private static Mock<IDBAuthService> CreateFullAccessAuth(bool grantDel = true, bool grantEdit = true)
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 4, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.Is<string>(x => x == "CRM_Customer|del")))
                .ReturnsAsync(grantDel);
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.Is<string>(x => x == "CRM_Customer|edit")))
                .ReturnsAsync(grantEdit);
            return auth;
        }

        /// <summary>
        /// 有限数据权限（authtype=1，empList 不含目标员工）——用于验证数据权限拒绝分支。
        /// </summary>
        private static Mock<IDBAuthService> CreateScopedAuth(List<string> empList)
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 1, empList = empList ?? new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            return auth;
        }

        /// <summary>
        /// 无权限（authtype=0，全部按钮拒绝）。
        /// </summary>
        private static Mock<IDBAuthService> CreateNoAuth()
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 0, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            return auth;
        }

        /// <summary>
        /// 装配 CustomerController：ICRM_CustomerService 桥接到真实 CRM_CustomerRepository，
        /// 其余依赖用空 Mock；HttpContext 带 ClaimTypes.Sid / Name 与 Loopback RemoteIP。
        /// </summary>
        private CustomerController CreateCustomerController(
            Mock<IDBAuthService> authMock,
            Mock<ISys_logService> logMock,
            string userId = "TEST_USER")
        {
            var customerSvc = new Mock<ICRM_CustomerService>();
            customerSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer, bool>>>(),
                                              It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<CRM_Customer, bool>> e, int p, int l)
                    => _custRepo.GridAsync(e, p, l));
            customerSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer, bool>>>(),
                                              It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<CRM_Customer, bool>> e, int p, int l, string o)
                    => _custRepo.GridAsync(e, p, l, o));
            customerSvc.Setup(s => s.RegainAsync(It.IsAny<string>()))
                .Returns((string id) => _custRepo.RegainAsync(id));
            customerSvc.Setup(s => s.UpdateAppAsync(It.IsAny<CRM_Customer>()))
                .Returns((CRM_Customer m) => _custRepo.UpdateAppAsync(m));

            var ctrl = new CustomerController(
                customerSvc.Object,
                new Mock<ICRM_ContactService>().Object,
                new Mock<ICRM_followService>().Object,
                new Mock<ISale_orderService>().Object,
                new Mock<ISale_contractService>().Object,
                authMock.Object,
                new Mock<ISys_ParamService>().Object,
                new Mock<ISys_Param_ProvincesService>().Object,
                logMock.Object,
                new Mock<ISys_infoService>().Object);

            var httpCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            httpCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, userId),
                new Claim(ClaimTypes.Name, "Test User")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return ctrl;
        }

        /// <summary>
        /// 装配 HrEmployeeController：Ihr_employeeService 桥接到真实 hr_employeeRepository，
        /// 其余依赖用空 Mock；HttpContext 带 ClaimTypes.Sid / Name。
        /// </summary>
        private HrEmployeeController CreateEmployeeController(
            Mock<IDBAuthService> authMock,
            string userId = "TEST_USER")
        {
            var empSvc = new Mock<Ihr_employeeService>();
            empSvc.Setup(s => s.ExistsAsync(It.IsAny<Expression<Func<hr_employee, bool>>>()))
                .Returns((Expression<Func<hr_employee, bool>> e) => _empRepo.ExistsAsync(e));
            empSvc.Setup(s => s.GetDefaultCityAsync(It.IsAny<string>()))
                .Returns((string id) => _empRepo.GetDefaultCityAsync(id));
            empSvc.Setup(s => s.UpdateDefaultCityAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string id, string city) => _empRepo.UpdateDefaultCityAsync(id, city));

            var ctrl = new HrEmployeeController(
                new Mock<ILogger<HrEmployeeController>>().Object,
                empSvc.Object,
                new Mock<ICRM_CustomerService>().Object,
                new Mock<ICRM_followService>().Object,
                new Mock<ISale_orderService>().Object,
                new Mock<ISale_contractService>().Object,
                new Mock<IFinance_ReceiveService>().Object,
                new Mock<IFinance_InvoiceService>().Object,
                new Mock<ISys_logService>().Object,
                authMock.Object);

            var httpCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            httpCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, userId),
                new Claim(ClaimTypes.Name, "Test User")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return ctrl;
        }

        // ============ #01 Regain：客户重取（回收站恢复） ============

        [Fact]
        public async Task Regain_DeletedCustomer_RestoresAndWritesLog()
        {
            // Arrange：预删除状态的客户（isDelete=1，带 Delete_time / Delete_id）
            var custId = Guid.NewGuid().ToString();
            await InsertCustomerAsync(NewCustomer(custId, isDelete: 1));
            var logMock = new Mock<ISys_logService>();
            logMock.Setup(l => l.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);

            var ctrl = CreateCustomerController(CreateFullAccessAuth(), logMock);

            // Act
            var json = await ctrl.Regain(custId);
            var obj = JObject.Parse(json);

            // Assert：业务成功
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Contains("恢复成功", (string)obj["msg"]!);

            // Assert：恢复语义生效（isDelete=0，Delete_time 清空，Delete_id 清空）
            var rows = await _fsql.Select<CRM_Customer>().Where(a => a.id == custId).ToListAsync();
            Assert.Single(rows);
            Assert.Equal(0, rows[0].isDelete);
            Assert.Null(rows[0].Delete_time);
            Assert.Equal(string.Empty, rows[0].Delete_id);

            // Assert：Sys_log 已写入且 cus_id / EventType 正确
            logMock.Verify(l => l.DeleteLog(It.Is<Sys_log>(x =>
                x.cus_id == custId && x.EventType == "[客户]恢复")), Times.Once);
        }

        [Fact]
        public async Task Regain_NonExistentId_ReturnsError()
        {
            // Arrange：空库
            var logMock = new Mock<ISys_logService>();
            var ctrl = CreateCustomerController(CreateFullAccessAuth(), logMock);

            // Act
            var json = await ctrl.Regain(Guid.NewGuid().ToString());
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("系统错误", (string)obj["msg"]!);

            // 关键：未找到客户不应产生 Sys_log
            logMock.Verify(l => l.DeleteLog(It.IsAny<Sys_log>()), Times.Never);
        }

        [Fact]
        public async Task Regain_AlreadyNormalCustomer_ReturnsSuccessIdempotent()
        {
            // Arrange：未预删除的客户（isDelete=0）——幂等场景
            var custId = Guid.NewGuid().ToString();
            await InsertCustomerAsync(NewCustomer(custId, isDelete: 0));
            var logMock = new Mock<ISys_logService>();
            logMock.Setup(l => l.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);

            var ctrl = CreateCustomerController(CreateFullAccessAuth(), logMock);

            // Act
            var json = await ctrl.Regain(custId);
            var obj = JObject.Parse(json);

            // Assert：返回成功（已处于恢复态），isDelete 保持 0
            Assert.Equal(0, (int)obj["code"]!);
            var rows = await _fsql.Select<CRM_Customer>().Where(a => a.id == custId).ToListAsync();
            Assert.Single(rows);
            Assert.Equal(0, rows[0].isDelete);
        }

        [Fact]
        public async Task Regain_NoDelButtonPermission_ReturnsPermissionDenied()
        {
            // Arrange：del 按钮权限被拒
            var custId = Guid.NewGuid().ToString();
            await InsertCustomerAsync(NewCustomer(custId, isDelete: 1));
            var logMock = new Mock<ISys_logService>();

            var ctrl = CreateCustomerController(CreateFullAccessAuth(grantDel: false), logMock);

            // Act
            var json = await ctrl.Regain(custId);
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限", (string)obj["msg"]!);

            // 关键：无权限不写 Sys_log、不改数据
            logMock.Verify(l => l.DeleteLog(It.IsAny<Sys_log>()), Times.Never);
            var rows = await _fsql.Select<CRM_Customer>().Where(a => a.id == custId).ToListAsync();
            Assert.Equal(1, rows[0].isDelete);
        }

        [Fact]
        public async Task Regain_DataAuthExcludesCustomer_ReturnsPermissionDenied()
        {
            // Arrange：authtype=1 且 empList 不含客户归属人 E1
            var custId = Guid.NewGuid().ToString();
            await InsertCustomerAsync(NewCustomer(custId, empId: "E1", isDelete: 1));
            var logMock = new Mock<ISys_logService>();

            var ctrl = CreateCustomerController(CreateScopedAuth(new List<string> { "OTHER" }), logMock);

            // Act
            var json = await ctrl.Regain(custId);
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限", (string)obj["msg"]!);

            // 关键：数据权限拒绝不得产生 Sys_log、不得修改数据
            logMock.Verify(l => l.DeleteLog(It.IsAny<Sys_log>()), Times.Never);
            var rows = await _fsql.Select<CRM_Customer>().Where(a => a.id == custId).ToListAsync();
            Assert.Single(rows);
            Assert.Equal(1, rows[0].isDelete);
        }

        // ============ #02 UpdateApp：移动端客户更新 ============

        [Fact]
        public async Task UpdateApp_ValidModel_UpdatesMobileFieldsOnly()
        {
            // Arrange
            var custId = Guid.NewGuid().ToString();
            await InsertCustomerAsync(NewCustomer(custId));

            var model = new CRM_Customer
            {
                id = custId,
                cus_name = "新名称",
                cus_add = "新地址",
                cus_tel = "13800000000",
                cus_fax = "010-12345678",
                cus_website = "https://example.com",
                cus_industry_id = "IND1",
                Provinces_id = "PV1",
                City_id = "CITY1",
                cus_type_id = "TP1",
                cus_level_id = "LV1",
                cus_source_id = "SRC1",
                DesCripe = "新描述",
                Remarks = "新备注",
                isPrivate = 0
                // emp_id 故意留空，验证 Controller 强制覆盖为当前登录用户
            };

            var ctrl = CreateCustomerController(CreateFullAccessAuth(), new Mock<ISys_logService>());

            // Act
            var json = await ctrl.UpdateApp(model);
            var obj = JObject.Parse(json);

            // Assert：业务成功
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Contains("更新成功", (string)obj["msg"]!);

            var c = (await _fsql.Select<CRM_Customer>().Where(a => a.id == custId).ToListAsync())[0];

            // Assert：15 个移动端字段全部更新
            Assert.Equal("新名称", c.cus_name);
            Assert.Equal("新地址", c.cus_add);
            Assert.Equal("13800000000", c.cus_tel);
            Assert.Equal("010-12345678", c.cus_fax);
            Assert.Equal("https://example.com", c.cus_website);
            Assert.Equal("IND1", c.cus_industry_id);
            Assert.Equal("PV1", c.Provinces_id);
            Assert.Equal("CITY1", c.City_id);
            Assert.Equal("TP1", c.cus_type_id);
            Assert.Equal("LV1", c.cus_level_id);
            Assert.Equal("SRC1", c.cus_source_id);
            Assert.Equal("新描述", c.DesCripe);
            Assert.Equal("新备注", c.Remarks);
            Assert.Equal(0, c.isPrivate);
            Assert.Equal("TEST_USER", c.emp_id);

            // Assert：PC 端独有 / 管理字段不被覆盖
            Assert.Equal($"CU-{custId}", c.sn);
            Assert.Equal(new DateTime(2024, 6, 15), c.create_time.Value);
            Assert.Equal(0, c.isDelete);
            Assert.Null(c.Delete_time);
            Assert.Null(c.lastfollow);
            Assert.Equal(0, c.state);
            Assert.Null(c.x);
            Assert.Null(c.y);
        }

        [Fact]
        public async Task UpdateApp_NonExistentId_ReturnsError()
        {
            // Arrange：空库
            var model = new CRM_Customer
            {
                id = Guid.NewGuid().ToString(),
                cus_name = "不存在"
            };
            var ctrl = CreateCustomerController(CreateFullAccessAuth(), new Mock<ISys_logService>());

            // Act
            var json = await ctrl.UpdateApp(model);
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("找不到数据", (string)obj["msg"]!);
        }

        [Fact]
        public async Task UpdateApp_InvalidId_ReturnsError()
        {
            // Arrange：id 不是合法 GUID
            var model = new CRM_Customer
            {
                id = "NOT_A_GUID",
                cus_name = "坏ID"
            };
            var ctrl = CreateCustomerController(CreateFullAccessAuth(), new Mock<ISys_logService>());

            // Act
            var json = await ctrl.UpdateApp(model);
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("客户ID无效", (string)obj["msg"]!);
        }

        [Fact]
        public async Task UpdateApp_DataAuthExcludesCustomer_ReturnsError()
        {
            // Arrange：authtype=1 且 empList 不含客户归属人 E1
            var custId = Guid.NewGuid().ToString();
            await InsertCustomerAsync(NewCustomer(custId, empId: "E1"));

            var model = new CRM_Customer
            {
                id = custId,
                cus_name = "越权修改"
            };
            var ctrl = CreateCustomerController(CreateScopedAuth(new List<string> { "OTHER" }),
                                                new Mock<ISys_logService>());

            // Act
            var json = await ctrl.UpdateApp(model);
            var obj = JObject.Parse(json);

            // Assert：拒绝且数据未被改动
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限更新该客户", (string)obj["msg"]!);
            var rows = await _fsql.Select<CRM_Customer>().Where(a => a.id == custId).ToListAsync();
            Assert.Single(rows);
            Assert.Equal($"客户-{custId}", rows[0].cus_name);
        }

        // ============ #10 Exist：员工唯一性校验 ============

        [Fact]
        public async Task Exist_UidConflict_ReturnsTrue()
        {
            // Arrange
            await InsertEmployeeAsync(NewEmployee("E1", uid: "zhangsan", name: "张三"));
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act
            var result = await ctrl.Exist("uid", "zhangsan");

            // Assert
            Assert.Equal("true", result);
        }

        [Fact]
        public async Task Exist_NameConflict_ReturnsTrue()
        {
            // Arrange
            await InsertEmployeeAsync(NewEmployee("E1", uid: "zhangsan", name: "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", uid: "lisi", name: "张三"));
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act：姓名重复（uid 不重复）
            var result = await ctrl.Exist("name", "张三");

            // Assert
            Assert.Equal("true", result);
        }

        [Fact]
        public async Task Exist_NoConflict_ReturnsFalse()
        {
            // Arrange
            await InsertEmployeeAsync(NewEmployee("E1", uid: "zhangsan", name: "张三"));
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act
            var result = await ctrl.Exist("uid", "nobody");

            // Assert
            Assert.Equal("false", result);
        }

        [Fact]
        public async Task Exist_EmptyFieldOrValue_ReturnsError()
        {
            // Arrange
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act：field 与 value 同时为空
            var result = await ctrl.Exist("", "");
            var obj = JObject.Parse(result);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("field 和 value 不能同时为空", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Exist_InvalidField_ReturnsError()
        {
            // Arrange
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act：field 既不是 uid 也不是 name
            var result = await ctrl.Exist("phone", "13800000000");
            var obj = JObject.Parse(result);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("field 只能是 uid 或 name", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Exist_EditMode_ExcludesSelf()
        {
            // Arrange：只有一个员工使用 uid=zhangsan（即编辑者自身）
            await InsertEmployeeAsync(NewEmployee("E1", uid: "zhangsan", name: "张三"));
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act：排除自身 → 无冲突
            var resultExcluding = await ctrl.Exist("uid", "zhangsan", "E1");

            // Assert：排除自身时返回 false（可用）
            Assert.Equal("false", resultExcluding);

            // Act：不排除自身 → 冲突
            var resultIncluding = await ctrl.Exist("uid", "zhangsan");
            Assert.Equal("true", resultIncluding);
        }

        // ============ #11 getDefaultCity：读取员工默认城市 ============

        [Fact]
        public async Task GetDefaultCity_HasValue_ReturnsCity()
        {
            // Arrange：当前用户 TEST_USER 的 default_city = 北京
            await InsertEmployeeAsync(NewEmployee("TEST_USER", uid: "zhangsan", name: "张三", defaultCity: "北京"));
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act
            var json = await ctrl.getDefaultCity();
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal("北京", (string)obj["msg"]!);
        }

        [Fact]
        public async Task GetDefaultCity_EmptyValue_ReturnsEmptyString()
        {
            // Arrange：员工存在但 default_city 为空
            await InsertEmployeeAsync(NewEmployee("TEST_USER", uid: "zhangsan", name: "张三", defaultCity: ""));
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act
            var json = await ctrl.getDefaultCity();
            var obj = JObject.Parse(json);

            // Assert：业务成功，msg 为空字符串
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(string.Empty, (string)obj["msg"]!);
        }

        [Fact]
        public async Task GetDefaultCity_UserNotFound_ReturnsError()
        {
            // Arrange：空库
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act
            var json = await ctrl.getDefaultCity();
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("员工不存在", (string)obj["msg"]!);
        }

        // ============ #12 updateDefaultCity：更新员工默认城市 ============

        [Fact]
        public async Task UpdateDefaultCity_ValidCity_UpdatesSuccess()
        {
            // Arrange：更新指定员工 E1 的默认城市
            await InsertEmployeeAsync(NewEmployee("E1", uid: "zhangsan", name: "张三", defaultCity: "上海"));
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act
            var json = await ctrl.updateDefaultCity("北京", "E1");
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Contains("更新成功", (string)obj["msg"]!);

            var rows = await _fsql.Select<hr_employee>().Where(a => a.id == "E1").ToListAsync();
            Assert.Single(rows);
            Assert.Equal("北京", rows[0].default_city);
        }

        [Fact]
        public async Task UpdateDefaultCity_EmptyCity_ReturnsError()
        {
            // Arrange：city 为空
            await InsertEmployeeAsync(NewEmployee("E1", uid: "zhangsan", name: "张三", defaultCity: "上海"));
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act
            var json = await ctrl.updateDefaultCity("", "E1");
            var obj = JObject.Parse(json);

            // Assert：拒绝且数据未被改动
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("城市不能为空", (string)obj["msg"]!);
            var rows = await _fsql.Select<hr_employee>().Where(a => a.id == "E1").ToListAsync();
            Assert.Single(rows);
            Assert.Equal("上海", rows[0].default_city);
        }

        [Fact]
        public async Task UpdateDefaultCity_NonExistentUser_ReturnsFailure()
        {
            // Arrange：目标员工不存在
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act
            var json = await ctrl.updateDefaultCity("北京", "NO_SUCH_EMP");
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("更新失败", (string)obj["msg"]!);
        }

        [Fact]
        public async Task UpdateDefaultCity_OmittedId_UsesCurrentLoginUser()
        {
            // Arrange：不传 id 时默认更新当前登录用户（保持 A 侧 emp_id=this.emp_id 语义）
            await InsertEmployeeAsync(NewEmployee("TEST_USER", uid: "zhangsan", name: "张三", defaultCity: ""));
            var ctrl = CreateEmployeeController(CreateFullAccessAuth());

            // Act：只传 city，不传 id
            var json = await ctrl.updateDefaultCity("广州", null);
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(0, (int)obj["code"]!);
            var rows = await _fsql.Select<hr_employee>().Where(a => a.id == "TEST_USER").ToListAsync();
            Assert.Single(rows);
            Assert.Equal("广州", rows[0].default_city);
        }
    }
}
