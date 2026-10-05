using FreeSql;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
    /// Report_EmployeeController（5 actions）单元测试。
    /// 装配方式参照同项目既有真测试 7.Test/XHD.Core.Tests/ReportTests.cs：
    /// Mock service 桥接真实 repository + SQLite in-memory，直接调用端点断言返回 JSON。
    /// </summary>
    public class ReportEmployeeTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly hr_employeeRepository _empRepo;
        private readonly CRM_followRepository _followRepo;

        public ReportEmployeeTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _empRepo = new hr_employeeRepository(_fsql);
            _followRepo = new CRM_followRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static hr_employee NewEmployee(string id, string name, int createDayOffset = 0)
        {
            return new hr_employee
            {
                id = id,
                name = name,
                create_id = "SYSTEM",
                create_time = new DateTime(2023, 1, 1).AddDays(createDayOffset),
                isDelete = 0
            };
        }

        private static CRM_follow NewFollow(
            string id,
            string empId,
            DateTime time,
            string typeId = "",
            string aimId = "")
        {
            return new CRM_follow
            {
                id = id,
                employee_id = empId,
                follow_time = time,
                follow_type_id = typeId,
                follow_aim_id = aimId,
                customer_id = string.Empty,
                contact_id = string.Empty,
                follow_content = "跟进内容",
                isDelete = 0
            };
        }

        private static Sys_Param NewParam(string id, string name, int order, string type)
        {
            return new Sys_Param
            {
                id = id,
                params_name = name,
                params_order = order,
                params_type = type,
                create_id = "SYSTEM",
                create_time = new DateTime(2023, 1, 1),
                isDelete = 0
            };
        }

        private async Task InsertEmployeeAsync(hr_employee e)
            => await _fsql.Insert(e).ExecuteAffrowsAsync();

        private async Task InsertFollowAsync(CRM_follow f)
            => await _fsql.Insert(f).ExecuteAffrowsAsync();

        private async Task InsertParamAsync(Sys_Param p)
            => await _fsql.Insert(p).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        /// <summary>
        /// 创建桥接到真实 hr_employeeRepository / CRM_followRepository 的 Report_EmployeeController。
        /// roleData 为 null 时默认放行全量（authtype=5 + 空 empList）。
        /// </summary>
        private Report_EmployeeController CreateController(
            XHDRoleData? roleData = null,
            string queryString = "",
            string userId = "TEST_USER")
        {
            var empMock = new Mock<Ihr_employeeService>();
            empMock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<hr_employee, bool>>>(),
                                           It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<hr_employee, bool>> e, int page, int limit, string orderby)
                    => _empRepo.GridAsync(e, page, limit, orderby));

            var followMock = new Mock<ICRM_followService>();
            followMock.Setup(s => s.ReportYear(It.IsAny<Expression<Func<CRM_follow, bool>>>()))
                .Returns((Expression<Func<CRM_follow, bool>> e) => _followRepo.ReportYear(e));
            followMock.Setup(s => s.ReportsYearAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Expression<Func<CRM_follow, bool>>>()))
                .Returns((string items, int year, Expression<Func<CRM_follow, bool>> e)
                    => _followRepo.ReportsYearAsync(items, year, e));
            followMock.Setup(s => s.ReportMonthEmpFollowAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<List<string>>()))
                .Returns((DateTime start, DateTime end, List<string> empIds)
                    => _followRepo.ReportMonthEmpFollowAsync(start, end, empIds));
            followMock.Setup(s => s.ReportEmpFollowAsync(It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns((int year, List<string> empIds)
                    => _followRepo.ReportEmpFollowAsync(year, empIds));

            var authMock = new Mock<IDBAuthService>();
            authMock.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(roleData ?? new XHDRoleData { authtype = 5, empList = new List<string>() });

            return TestControllerHelper.CreateWithHttpContext<Report_EmployeeController>(
                queryString, userId, "Test User",
                Mock.Of<ILogger<Report_EmployeeController>>(),
                empMock.Object,
                followMock.Object,
                authMock.Object);
        }

        private static JToken MonthOf(JArray arr, int month) => arr.First(o => (int)o["xmonth"]! == month);

        // =========================================================
        // Grid（员工分页，GetDataAuth 数据权限闸门）
        // =========================================================

        [Fact]
        public async Task Grid_AuthType4_ReturnsAllEmployees()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三", createDayOffset: 1));
            await InsertEmployeeAsync(NewEmployee("E2", "李四", createDayOffset: 2));
            await InsertEmployeeAsync(NewEmployee("E3", "王五", createDayOffset: 3));

            var ctrl = CreateController();
            var json = await ctrl.Grid(new PageView<hr_employee> { Page = 1, Limit = 30 });

            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(3, (int)obj["count"]!);
            var names = ((JArray)obj["data"]!).Select(e => (string)e["name"]!).ToList();
            Assert.Contains("张三", names);
            Assert.Contains("李四", names);
            Assert.Contains("王五", names);
        }

        [Fact]
        public async Task Grid_AuthType0_EmptyEmpList_ReturnsEmpty()
        {
            // authtype!=5 且 empList 为空 → empList.Contains(a.id) 命中空集，返回空
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));

            var ctrl = CreateController(roleData: new XHDRoleData { authtype = 0, empList = new List<string>() });
            var json = await ctrl.Grid(new PageView<hr_employee> { Page = 1, Limit = 30 });

            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task Grid_AuthType2_EmpListScoped_OnlyReturnsScopedEmployee()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));

            var ctrl = CreateController(roleData: new XHDRoleData { authtype = 2, empList = new List<string> { "E1" } });
            var json = await ctrl.Grid(new PageView<hr_employee> { Page = 1, Limit = 30 });

            var obj = JObject.Parse(json);
            Assert.Equal(1, (int)obj["count"]!);
            var names = ((JArray)obj["data"]!).Select(e => (string)e["name"]!).ToList();
            Assert.Contains("张三", names);
            Assert.DoesNotContain("李四", names);
        }

        [Fact]
        public async Task Grid_WithEmpIdFilter_ReturnsOnlyRequestedEmployee()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertEmployeeAsync(NewEmployee("E3", "王五"));

            var ctrl = CreateController(queryString: "emp_id=E2,E3");
            var json = await ctrl.Grid(new PageView<hr_employee> { Page = 1, Limit = 30 });

            var obj = JObject.Parse(json);
            Assert.Equal(2, (int)obj["count"]!);
            var names = ((JArray)obj["data"]!).Select(e => (string)e["name"]!).ToList();
            Assert.Contains("李四", names);
            Assert.Contains("王五", names);
            Assert.DoesNotContain("张三", names);
        }

        [Fact]
        public async Task Grid_EmptyDB_ReturnsEmpty()
        {
            var ctrl = CreateController();
            var json = await ctrl.Grid(new PageView<hr_employee> { Page = 1, Limit = 30 });

            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        // =========================================================
        // ReportYear（年度跟进曲线，固定 12 月）
        // =========================================================

        [Fact]
        public async Task Follow_ReportYear_WithYear_ReturnsTwelveMonthMatrix()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertFollowAsync(NewFollow("F1", "E1", new DateTime(2024, 3, 10)));
            await InsertFollowAsync(NewFollow("F2", "E1", new DateTime(2024, 6, 15)));
            await InsertFollowAsync(NewFollow("F3", "E2", new DateTime(2024, 3, 12)));
            await InsertFollowAsync(NewFollow("F4", "E1", new DateTime(2023, 12, 31))); // 跨年过滤

            var ctrl = CreateController(queryString: "year=2024");
            var arr = JArray.Parse(await ctrl.ReportYear());

            Assert.Equal(12, arr.Count);
            Assert.Equal(2, (int)MonthOf(arr, 3)["count"]!);
            Assert.Equal(1, (int)MonthOf(arr, 6)["count"]!);
            Assert.Equal(0, (int)MonthOf(arr, 12)["count"]!);
        }

        [Fact]
        public async Task Follow_ReportYear_EmptyDB_ReturnsAllZero()
        {
            var ctrl = CreateController(queryString: "year=2024");
            var arr = JArray.Parse(await ctrl.ReportYear());

            Assert.Equal(12, arr.Count);
            Assert.All(arr, o => Assert.Equal(0, (int)o["count"]!));
        }

        // =========================================================
        // ReportFollowYear（按跟进类型分组的年度矩阵，含权限闸门）
        // =========================================================

        [Fact]
        public async Task ReportFollowYear_WithData_ReturnsGroupedMatrix()
        {
            await InsertParamAsync(NewParam("FT1", "电话", 1, "follow_type"));
            await InsertParamAsync(NewParam("FT2", "邮件", 2, "follow_type"));
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertFollowAsync(NewFollow("F1", "E1", new DateTime(2024, 3, 10), typeId: "FT1"));
            await InsertFollowAsync(NewFollow("F2", "E1", new DateTime(2024, 3, 11), typeId: "FT1"));
            await InsertFollowAsync(NewFollow("F3", "E1", new DateTime(2024, 6, 15), typeId: "FT2"));

            var ctrl = CreateController(queryString: "syear=2024&items=Follow_Type");
            var arr = JArray.Parse(await ctrl.ReportFollowYear());

            Assert.Equal(2, arr.Count);
            var phone = arr.First(o => (string)o["params_name"]! == "电话");
            var email = arr.First(o => (string)o["params_name"]! == "邮件");
            Assert.Equal(2, (int)phone["m3"]!);
            Assert.Equal(1, (int)email["m6"]!);
        }

        [Fact]
        public async Task ReportFollowYear_EmptyDB_ReturnsEmptyArray()
        {
            var ctrl = CreateController(queryString: "syear=2024&items=Follow_Type");
            var arr = JArray.Parse(await ctrl.ReportFollowYear());

            Assert.Empty(arr);
        }

        [Fact]
        public async Task ReportFollowYear_AuthType0_EmptyEmpList_ReturnsPermissionError()
        {
            // authtype!=5 且 empList 为空 → 返回"权限不足！"
            await InsertFollowAsync(NewFollow("F1", "E1", new DateTime(2024, 3, 10)));

            var ctrl = CreateController(
                roleData: new XHDRoleData { authtype = 0, empList = new List<string>() },
                queryString: "syear=2024&items=Follow_Type");

            var obj = JObject.Parse(await ctrl.ReportFollowYear());

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("权限", (string)obj["msg"]!);
        }

        [Fact]
        public async Task ReportFollowYear_AuthType2_EmpListScoped_OnlyCountsScopedRows()
        {
            await InsertParamAsync(NewParam("FT1", "电话", 1, "follow_type"));
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertFollowAsync(NewFollow("F1", "E1", new DateTime(2024, 3, 10), typeId: "FT1"));
            await InsertFollowAsync(NewFollow("F2", "E2", new DateTime(2024, 3, 11), typeId: "FT1"));

            var ctrl = CreateController(
                roleData: new XHDRoleData { authtype = 2, empList = new List<string> { "E1" } },
                queryString: "syear=2024&items=Follow_Type");

            var arr = JArray.Parse(await ctrl.ReportFollowYear());

            Assert.Single(arr);
            Assert.Equal("电话", (string)arr[0]["params_name"]!);
            Assert.Equal(1, (int)arr[0]["m3"]!);
        }

        [Fact]
        public async Task ReportFollowYear_InvalidItems_ReturnsError()
        {
            var ctrl = CreateController(queryString: "syear=2024&items=InvalidType");

            var obj = JObject.Parse(await ctrl.ReportFollowYear());

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("items", (string)obj["msg"]!);
        }

        // =========================================================
        // ReportMonth（员工月度跟进矩阵，按 start/end 区间）
        // =========================================================

        [Fact]
        public async Task ReportMonth_WithDateRange_ReturnsMatrix()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertFollowAsync(NewFollow("F1", "E1", new DateTime(2024, 3, 5)));
            await InsertFollowAsync(NewFollow("F2", "E1", new DateTime(2024, 3, 25)));
            await InsertFollowAsync(NewFollow("F3", "E1", new DateTime(2024, 4, 5))); // 区间外
            await InsertFollowAsync(NewFollow("F4", "E1", new DateTime(2024, 2, 28))); // 区间外

            var ctrl = CreateController(queryString: "start=2024-03-01&end=2024-03-31");
            var arr = JArray.Parse(await ctrl.ReportMonth());

            Assert.Single(arr);
            Assert.Equal("张三", (string)arr[0]["name"]!);
            Assert.Equal(2, (int)arr[0]["m3"]!);
            Assert.Equal(0, (int)arr[0]["m4"]!);
            Assert.Equal(0, (int)arr[0]["m2"]!);
        }

        [Fact]
        public async Task ReportMonth_EmptyDB_ReturnsEmptyArray()
        {
            var ctrl = CreateController(queryString: "start=2024-03-01&end=2024-03-31");
            var arr = JArray.Parse(await ctrl.ReportMonth());

            Assert.Empty(arr);
        }

        // =========================================================
        // ReportEmpFollow（员工年度跟进排行矩阵）
        // =========================================================

        [Fact]
        public async Task ReportEmpFollow_WithYear_ReturnsMatrix()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertFollowAsync(NewFollow("F1", "E1", new DateTime(2024, 3, 10)));
            await InsertFollowAsync(NewFollow("F2", "E1", new DateTime(2024, 3, 11)));
            await InsertFollowAsync(NewFollow("F3", "E1", new DateTime(2024, 7, 5)));
            await InsertFollowAsync(NewFollow("F4", "E2", new DateTime(2024, 3, 12)));
            await InsertFollowAsync(NewFollow("F5", "E1", new DateTime(2023, 3, 10))); // 跨年过滤

            var ctrl = CreateController(queryString: "year=2024");
            var arr = JArray.Parse(await ctrl.ReportEmpFollow());

            Assert.Equal(2, arr.Count);
            var e1 = arr.First(o => (string)o["name"]! == "张三");
            var e2 = arr.First(o => (string)o["name"]! == "李四");
            Assert.Equal(2, (int)e1["m3"]!);
            Assert.Equal(1, (int)e1["m7"]!);
            Assert.Equal(0, (int)e1["m1"]!);
            Assert.Equal(1, (int)e2["m3"]!);
        }

        [Fact]
        public async Task ReportEmpFollow_EmptyDB_ReturnsEmptyArray()
        {
            var ctrl = CreateController(queryString: "year=2024");
            var arr = JArray.Parse(await ctrl.ReportEmpFollow());

            Assert.Empty(arr);
        }
    }
}
