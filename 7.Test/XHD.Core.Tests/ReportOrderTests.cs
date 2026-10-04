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
    /// Report_OrderController（7 actions）+ Report_ComparedController（1 action）单元测试。
    /// 装配方式参照同项目既有真测试 7.Test/XHD.Core.Tests/ReportTests.cs：
    /// Mock service 桥接真实 repository + SQLite in-memory，直接调用端点断言返回 JSON。
    /// </summary>
    public class ReportOrderTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sale_orderRepository _orderRepo;

        public ReportOrderTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _orderRepo = new Sale_orderRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static hr_employee NewEmployee(string id, string name)
        {
            return new hr_employee
            {
                id = id,
                name = name,
                create_id = "SYSTEM",
                create_time = new DateTime(2023, 1, 1),
                isDelete = 0
            };
        }

        private static Sale_order NewOrder(
            string id,
            string empId,
            DateTime date,
            decimal amount = 100m,
            string statusId = "",
            string payTypeId = "")
        {
            return new Sale_order
            {
                id = id,
                emp_id = empId,
                Order_date = date,
                Order_amount = amount,
                Order_status_id = statusId,
                pay_type_id = payTypeId,
                create_id = "SYSTEM",
                create_time = new DateTime(2023, 1, 1),
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

        private async Task InsertOrderAsync(Sale_order o)
            => await _fsql.Insert(o).ExecuteAffrowsAsync();

        private async Task InsertParamAsync(Sys_Param p)
            => await _fsql.Insert(p).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        /// <summary>
        /// 创建桥接到真实 Sale_orderRepository 的 Report_OrderController。
        /// roleData 为 null 时默认放行全量（authtype=4 + 空 empList）。
        /// </summary>
        private static Report_OrderController CreateController(
            Sale_orderRepository orderRepo,
            XHDRoleData? roleData = null,
            string queryString = "",
            string userId = "TEST_USER")
        {
            var svcMock = new Mock<ISale_orderService>();
            svcMock.Setup(s => s.ReportYear(It.IsAny<Expression<Func<Sale_order, bool>>>()))
                .Returns((Expression<Func<Sale_order, bool>> e) => orderRepo.ReportYear(e));
            svcMock.Setup(s => s.ReportYearSum(It.IsAny<Expression<Func<Sale_order, bool>>>()))
                .Returns((Expression<Func<Sale_order, bool>> e) => orderRepo.ReportYearSum(e));
            svcMock.Setup(s => s.ReportStatus(It.IsAny<Expression<Func<Sale_order, bool>>>()))
                .Returns((Expression<Func<Sale_order, bool>> e) => orderRepo.ReportStatus(e));
            svcMock.Setup(s => s.ReportPayType(It.IsAny<Expression<Func<Sale_order, bool>>>()))
                .Returns((Expression<Func<Sale_order, bool>> e) => orderRepo.ReportPayType(e));
            svcMock.Setup(s => s.ReportEmpOrderAsync(It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns((int year, List<string> empIds) => orderRepo.ReportEmpOrderAsync(year, empIds));
            svcMock.Setup(s => s.ReportMonthEmpOrderAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<List<string>>()))
                .Returns((DateTime start, DateTime end, List<string> empIds)
                    => orderRepo.ReportMonthEmpOrderAsync(start, end, empIds));
            svcMock.Setup(s => s.ComparedEmpCusOrderAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns((int year1, int month1, int year2, int month2, List<string> empIds)
                    => orderRepo.ComparedEmpCusOrderAsync(year1, month1, year2, month2, empIds));

            var authMock = new Mock<IDBAuthService>();
            authMock.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(roleData ?? new XHDRoleData { authtype = 4, empList = new List<string>() });

            return TestControllerHelper.CreateWithHttpContext<Report_OrderController>(
                queryString, userId, "Test User",
                Mock.Of<ILogger<Report_OrderController>>(),
                svcMock.Object,
                Mock.Of<ISale_order_detailsService>(),
                Mock.Of<IFinance_InvoiceService>(),
                Mock.Of<IFinance_ReceiveService>(),
                Mock.Of<ISys_logService>(),
                authMock.Object);
        }

        private static JToken MonthOf(JArray arr, int month) => arr.First(o => (int)o["xmonth"]! == month);

        // =========================================================
        // ReportYear（年度订单曲线，固定 12 月）
        // =========================================================

        [Fact]
        public async Task ReportYear_WithData_ReturnsTwelveMonthMatrix()
        {
            // Arrange：当前年 3 月 2 单、6 月 1 单；上一年年底的单应被年份过滤
            int year = DateTime.Now.Year;
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertOrderAsync(NewOrder("O1", "E1", new DateTime(year, 3, 10)));
            await InsertOrderAsync(NewOrder("O2", "E1", new DateTime(year, 3, 20)));
            await InsertOrderAsync(NewOrder("O3", "E1", new DateTime(year, 6, 15)));
            await InsertOrderAsync(NewOrder("O4", "E1", new DateTime(year - 1, 12, 31)));

            // Act
            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportYear());

            // Assert
            Assert.Equal(12, arr.Count);
            Assert.Equal(2, (int)MonthOf(arr, 3)["count"]!);
            Assert.Equal(1, (int)MonthOf(arr, 6)["count"]!);
            Assert.Equal(0, (int)MonthOf(arr, 12)["count"]!);
            Assert.Equal(0, (int)MonthOf(arr, 1)["count"]!);
        }

        [Fact]
        public async Task ReportYear_EmptyDB_ReturnsAllZero()
        {
            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportYear());

            Assert.Equal(12, arr.Count);
            Assert.All(arr, o => Assert.Equal(0, (int)o["count"]!));
        }

        [Fact]
        public async Task ReportYear_AuthType0_EmptyEmpList_ReturnsNoPermission()
        {
            var ctrl = CreateController(_orderRepo,
                roleData: new XHDRoleData { authtype = 0, empList = new List<string>() });

            var obj = JObject.Parse(await ctrl.ReportYear());

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限", (string)obj["msg"]!);
        }

        [Fact]
        public async Task ReportYear_AuthType2_EmpListScoped_OnlyCountsScopedEmployee()
        {
            // Arrange：E1 两单 + E2 一单，数据权限范围只含 E1
            int year = DateTime.Now.Year;
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertOrderAsync(NewOrder("O1", "E1", new DateTime(year, 3, 10)));
            await InsertOrderAsync(NewOrder("O2", "E1", new DateTime(year, 3, 11)));
            await InsertOrderAsync(NewOrder("O3", "E2", new DateTime(year, 3, 12)));

            var ctrl = CreateController(_orderRepo,
                roleData: new XHDRoleData { authtype = 2, empList = new List<string> { "E1" } });

            var arr = JArray.Parse(await ctrl.ReportYear());

            Assert.Equal(2, (int)MonthOf(arr, 3)["count"]!);
        }

        // =========================================================
        // ReportYearSum（年度订单金额汇总，固定 12 月）
        // =========================================================

        [Fact]
        public async Task ReportYearSum_WithData_ReturnsMonthlyAmountSum()
        {
            int year = DateTime.Now.Year;
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertOrderAsync(NewOrder("O1", "E1", new DateTime(year, 3, 10), 100.5m));
            await InsertOrderAsync(NewOrder("O2", "E1", new DateTime(year, 3, 20), 200m));
            await InsertOrderAsync(NewOrder("O3", "E1", new DateTime(year, 6, 15), 50.25m));

            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportYearSum());

            Assert.Equal(12, arr.Count);
            Assert.Equal(300.5m, (decimal)MonthOf(arr, 3)["count"]!);
            Assert.Equal(50.25m, (decimal)MonthOf(arr, 6)["count"]!);
            Assert.Equal(0, (int)MonthOf(arr, 9)["count"]!);
        }

        [Fact]
        public async Task ReportYearSum_EmptyDB_ReturnsAllZero()
        {
            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportYearSum());

            Assert.Equal(12, arr.Count);
            Assert.All(arr, o => Assert.Equal(0, (int)o["count"]!));
        }

        // =========================================================
        // ReportStatus（按订单状态聚合）
        // =========================================================

        [Fact]
        public async Task ReportStatus_WithData_GroupsByStatusName()
        {
            int year = DateTime.Now.Year;
            await InsertParamAsync(NewParam("OS1", "已完成", 1, "order_status"));
            await InsertParamAsync(NewParam("OS2", "已取消", 2, "order_status"));
            await InsertOrderAsync(NewOrder("O1", "E1", new DateTime(year, 3, 10), statusId: "OS1"));
            await InsertOrderAsync(NewOrder("O2", "E1", new DateTime(year, 4, 10), statusId: "OS1"));
            await InsertOrderAsync(NewOrder("O3", "E1", new DateTime(year, 5, 10), statusId: "OS2"));

            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportStatus());

            Assert.Equal(2, arr.Count);
            var done = arr.First(o => (string)o["name"]! == "已完成");
            var cancelled = arr.First(o => (string)o["name"]! == "已取消");
            Assert.Equal(2, (int)done["value"]!);
            Assert.Equal(1, (int)cancelled["value"]!);
        }

        [Fact]
        public async Task ReportStatus_EmptyDB_ReturnsEmptyArray()
        {
            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportStatus());

            Assert.Empty(arr);
        }

        // =========================================================
        // ReportPayType（按支付方式聚合）
        // =========================================================

        [Fact]
        public async Task ReportPayType_WithData_GroupsByPayTypeName()
        {
            int year = DateTime.Now.Year;
            await InsertParamAsync(NewParam("PT1", "微信", 1, "pay_type"));
            await InsertParamAsync(NewParam("PT2", "支付宝", 2, "pay_type"));
            await InsertOrderAsync(NewOrder("O1", "E1", new DateTime(year, 3, 10), payTypeId: "PT1"));
            await InsertOrderAsync(NewOrder("O2", "E1", new DateTime(year, 3, 11), payTypeId: "PT1"));
            await InsertOrderAsync(NewOrder("O3", "E1", new DateTime(year, 4, 10), payTypeId: "PT2"));

            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportPayType());

            Assert.Equal(2, arr.Count);
            var wechat = arr.First(o => (string)o["name"]! == "微信");
            var alipay = arr.First(o => (string)o["name"]! == "支付宝");
            Assert.Equal(2, (int)wechat["value"]!);
            Assert.Equal(1, (int)alipay["value"]!);
        }

        [Fact]
        public async Task ReportPayType_EmptyDB_ReturnsEmptyArray()
        {
            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportPayType());

            Assert.Empty(arr);
        }

        // =========================================================
        // ReportEmpOrder（员工年度订单矩阵）
        // =========================================================

        [Fact]
        public async Task ReportEmpOrder_WithYear_ReturnsEmployeeMatrix()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertOrderAsync(NewOrder("O1", "E1", new DateTime(2024, 3, 10)));
            await InsertOrderAsync(NewOrder("O2", "E1", new DateTime(2024, 3, 11)));
            await InsertOrderAsync(NewOrder("O3", "E1", new DateTime(2024, 7, 5)));
            await InsertOrderAsync(NewOrder("O4", "E2", new DateTime(2024, 3, 12)));
            await InsertOrderAsync(NewOrder("O5", "E1", new DateTime(2023, 3, 10))); // 跨年过滤

            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportEmpOrder(2024, null));

            Assert.Equal(2, arr.Count);
            var e1 = arr.First(o => (string)o["name"]! == "张三");
            var e2 = arr.First(o => (string)o["name"]! == "李四");
            Assert.Equal(2024, (int)e1["yy"]!);
            Assert.Equal(2, (int)e1["m3"]!);
            Assert.Equal(1, (int)e1["m7"]!);
            Assert.Equal(0, (int)e1["m1"]!);
            Assert.Equal(1, (int)e2["m3"]!);
        }

        [Fact]
        public async Task ReportEmpOrder_EmptyDB_ReturnsEmptyArray()
        {
            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportEmpOrder(2024, null));

            Assert.Empty(arr);
        }

        // =========================================================
        // ReportMonthEmpOrder（员工月度订单矩阵，按时间区间）
        // =========================================================

        [Fact]
        public async Task ReportMonthEmpOrder_WithRange_ReturnsEmployeeMatrix()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertOrderAsync(NewOrder("O1", "E1", new DateTime(2024, 3, 5)));
            await InsertOrderAsync(NewOrder("O2", "E1", new DateTime(2024, 3, 25)));
            await InsertOrderAsync(NewOrder("O3", "E1", new DateTime(2024, 4, 5))); // 区间外
            await InsertOrderAsync(NewOrder("O4", "E1", new DateTime(2024, 2, 28))); // 区间外

            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportMonthEmpOrder(
                new DateTime(2024, 3, 1), new DateTime(2024, 3, 31, 23, 59, 59), null));

            Assert.Single(arr);
            var e1 = arr[0];
            Assert.Equal("张三", (string)e1["name"]!);
            Assert.Equal(2, (int)e1["m3"]!);
            Assert.Equal(0, (int)e1["m4"]!);
            Assert.Equal(0, (int)e1["m2"]!);
        }

        [Fact]
        public async Task ReportMonthEmpOrder_EmptyDB_ReturnsEmptyArray()
        {
            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ReportMonthEmpOrder(
                new DateTime(2024, 3, 1), new DateTime(2024, 3, 31), null));

            Assert.Empty(arr);
        }

        // =========================================================
        // ComparedEmpCusOrder（员工双月订单对比）
        // =========================================================

        [Fact]
        public async Task ComparedEmpCusOrder_TwoMonths_ReturnsDiff()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertOrderAsync(NewOrder("O11", "E1", new DateTime(2024, 3, 5)));
            await InsertOrderAsync(NewOrder("O12", "E1", new DateTime(2024, 3, 6)));
            await InsertOrderAsync(NewOrder("O21", "E1", new DateTime(2024, 4, 5)));
            await InsertOrderAsync(NewOrder("O22", "E1", new DateTime(2024, 4, 6)));
            await InsertOrderAsync(NewOrder("O23", "E1", new DateTime(2024, 4, 7)));
            await InsertOrderAsync(NewOrder("O24", "E1", new DateTime(2024, 4, 8)));
            await InsertOrderAsync(NewOrder("O25", "E1", new DateTime(2024, 4, 9)));

            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ComparedEmpCusOrder(2024, 3, 4, null));

            Assert.Single(arr);
            Assert.Equal("张三", (string)arr[0]["yy"]!);
            Assert.Equal(2, (int)arr[0]["dt1"]!);
            Assert.Equal(5, (int)arr[0]["dt2"]!);
        }

        [Fact]
        public async Task ComparedEmpCusOrder_EmptyDB_ReturnsEmptyArray()
        {
            var ctrl = CreateController(_orderRepo);
            var arr = JArray.Parse(await ctrl.ComparedEmpCusOrder(2024, 3, 4, null));

            Assert.Empty(arr);
        }

        // =========================================================
        // Report_ComparedController.Index（对比报表页，仅返回视图）
        // =========================================================

        [Fact]
        public void Compared_Index_ReturnsDefaultView()
        {
            var ctrl = TestControllerHelper.CreateWithHttpContext<Report_ComparedController>(
                "", "TEST_USER", "Test User",
                Mock.Of<ILogger<Report_ComparedController>>());

            var result = ctrl.Index();

            var viewResult = Assert.IsType<ViewResult>(result);
            // 未显式指定 ViewName，走默认视图 Views/Report_Compared/Index.cshtml
            Assert.Null(viewResult.ViewName);
        }
    }
}
