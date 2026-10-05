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
    /// Report_FinanceController（6 actions）单元测试。
    /// 装配方式参照同项目既有真测试 7.Test/XHD.Core.Tests/ReportTests.cs：
    /// Mock service 桥接真实 repository + SQLite in-memory，直接调用端点断言返回 JSON。
    /// </summary>
    public class ReportFinanceTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Finance_ReceiveRepository _receiveRepo;
        private readonly Finance_InvoiceRepository _invoiceRepo;

        public ReportFinanceTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _receiveRepo = new Finance_ReceiveRepository(_fsql);
            _invoiceRepo = new Finance_InvoiceRepository(_fsql);
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

        private static Finance_Receive NewReceive(
            string id,
            string payeeId,
            DateTime date,
            decimal amount = 100m,
            string payTypeId = "")
        {
            return new Finance_Receive
            {
                id = id,
                Payee_id = payeeId,
                Receive_date = date,
                Receive_amount = amount,
                Pay_type_id = payTypeId,
                order_id = string.Empty,
                create_id = "SYSTEM",
                create_time = new DateTime(2023, 1, 1),
                isDelete = 0
            };
        }

        private static Finance_Invoice NewInvoice(
            string id,
            string empId,
            DateTime date,
            decimal amount = 100m,
            string typeId = "")
        {
            return new Finance_Invoice
            {
                id = id,
                emp_id = empId,
                invoice_date = date,
                invoice_amount = amount,
                invoice_type_id = typeId,
                order_id = string.Empty,
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

        private async Task InsertReceiveAsync(Finance_Receive r)
            => await _fsql.Insert(r).ExecuteAffrowsAsync();

        private async Task InsertInvoiceAsync(Finance_Invoice i)
            => await _fsql.Insert(i).ExecuteAffrowsAsync();

        private async Task InsertParamAsync(Sys_Param p)
            => await _fsql.Insert(p).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        /// <summary>
        /// 创建桥接到真实 Finance_*Repository 的 Report_FinanceController。
        /// roleData 为 null 时默认放行全量（authtype=5 + 空 empList）。
        /// </summary>
        private Report_FinanceController CreateController(
            XHDRoleData? roleData = null,
            string queryString = "",
            string userId = "TEST_USER")
        {
            var receiveMock = new Mock<IFinance_ReceiveService>();
            receiveMock.Setup(s => s.ReportYear(It.IsAny<Expression<Func<Finance_Receive, bool>>>()))
                .Returns((Expression<Func<Finance_Receive, bool>> e) => _receiveRepo.ReportYear(e));
            receiveMock.Setup(s => s.ReportYearSum(It.IsAny<Expression<Func<Finance_Receive, bool>>>()))
                .Returns((Expression<Func<Finance_Receive, bool>> e) => _receiveRepo.ReportYearSum(e));
            receiveMock.Setup(s => s.ReportPayType(It.IsAny<Expression<Func<Finance_Receive, bool>>>()))
                .Returns((Expression<Func<Finance_Receive, bool>> e) => _receiveRepo.ReportPayType(e));

            var invoiceMock = new Mock<IFinance_InvoiceService>();
            invoiceMock.Setup(s => s.ReportYear(It.IsAny<Expression<Func<Finance_Invoice, bool>>>()))
                .Returns((Expression<Func<Finance_Invoice, bool>> e) => _invoiceRepo.ReportYear(e));
            invoiceMock.Setup(s => s.ReportYearSum(It.IsAny<Expression<Func<Finance_Invoice, bool>>>()))
                .Returns((Expression<Func<Finance_Invoice, bool>> e) => _invoiceRepo.ReportYearSum(e));
            invoiceMock.Setup(s => s.ReportInvoiceType(It.IsAny<Expression<Func<Finance_Invoice, bool>>>()))
                .Returns((Expression<Func<Finance_Invoice, bool>> e) => _invoiceRepo.ReportInvoiceType(e));

            var authMock = new Mock<IDBAuthService>();
            authMock.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(roleData ?? new XHDRoleData { authtype = 5, empList = new List<string>() });

            return TestControllerHelper.CreateWithHttpContext<Report_FinanceController>(
                queryString, userId, "Test User",
                Mock.Of<ILogger<Report_FinanceController>>(),
                invoiceMock.Object,
                receiveMock.Object,
                Mock.Of<ISys_logService>(),
                authMock.Object);
        }

        private static JToken MonthOf(JArray arr, int month) => arr.First(o => (int)o["xmonth"]! == month);

        // =========================================================
        // Finance_Receive.ReportYear（收款年度曲线，含 GetDataAuth 权限闸门）
        // =========================================================

        [Fact]
        public async Task Receive_ReportYear_WithData_ReturnsTwelveMonthMatrix()
        {
            // Arrange：当前年 3 月 2 笔（E1/E2）、6 月 1 笔；上一年年底的记录应被过滤
            int year = DateTime.Now.Year;
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertReceiveAsync(NewReceive("R1", "E1", new DateTime(year, 3, 10)));
            await InsertReceiveAsync(NewReceive("R2", "E2", new DateTime(year, 3, 11)));
            await InsertReceiveAsync(NewReceive("R3", "E1", new DateTime(year, 6, 15)));
            await InsertReceiveAsync(NewReceive("R4", "E1", new DateTime(year - 1, 12, 31)));

            // Act（authtype=5 放行全量）
            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportYear());

            // Assert
            Assert.Equal(12, arr.Count);
            Assert.Equal(2, (int)MonthOf(arr, 3)["count"]!);
            Assert.Equal(1, (int)MonthOf(arr, 6)["count"]!);
            Assert.Equal(0, (int)MonthOf(arr, 12)["count"]!);
        }

        [Fact]
        public async Task Receive_ReportYear_EmptyDB_ReturnsAllZero()
        {
            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportYear());

            Assert.Equal(12, arr.Count);
            Assert.All(arr, o => Assert.Equal(0, (int)o["count"]!));
        }

        [Fact]
        public async Task Receive_ReportYear_AuthType0_EmptyEmpList_ReturnsNoPermission()
        {
            // authtype!=5 且 empList 为空 → 闸门直接返回"无权限"，不查库
            await InsertReceiveAsync(NewReceive("R1", "E1", new DateTime(DateTime.Now.Year, 3, 10)));

            var ctrl = CreateController(roleData: new XHDRoleData { authtype = 0, empList = new List<string>() });

            var obj = JObject.Parse(await ctrl.ReportYear());

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Receive_ReportYear_AuthType2_EmpListScoped_OnlyCountsScopedEmployee()
        {
            // Arrange：E1 一笔 + E2 一笔，数据权限范围只含 E1
            int year = DateTime.Now.Year;
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertReceiveAsync(NewReceive("R1", "E1", new DateTime(year, 3, 10)));
            await InsertReceiveAsync(NewReceive("R2", "E2", new DateTime(year, 3, 11)));

            var ctrl = CreateController(roleData: new XHDRoleData { authtype = 2, empList = new List<string> { "E1" } });

            var arr = JArray.Parse(await ctrl.ReportYear());

            Assert.Equal(1, (int)MonthOf(arr, 3)["count"]!);
        }

        // =========================================================
        // Finance_Receive.ReportYearSum（收款金额汇总）
        // =========================================================

        [Fact]
        public async Task Receive_ReportYearSum_WithData_ReturnsMonthlyAmountSum()
        {
            int year = DateTime.Now.Year;
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertReceiveAsync(NewReceive("R1", "E1", new DateTime(year, 3, 10), 100.5m));
            await InsertReceiveAsync(NewReceive("R2", "E1", new DateTime(year, 3, 20), 200m));
            await InsertReceiveAsync(NewReceive("R3", "E1", new DateTime(year, 6, 15), 50.25m));

            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportYearSum());

            Assert.Equal(12, arr.Count);
            Assert.Equal(300.5m, (decimal)MonthOf(arr, 3)["count"]!);
            Assert.Equal(50.25m, (decimal)MonthOf(arr, 6)["count"]!);
            Assert.Equal(0, (int)MonthOf(arr, 9)["count"]!);
        }

        [Fact]
        public async Task Receive_ReportYearSum_EmptyDB_ReturnsAllZero()
        {
            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportYearSum());

            Assert.Equal(12, arr.Count);
            Assert.All(arr, o => Assert.Equal(0, (int)o["count"]!));
        }

        // =========================================================
        // Finance_Receive.ReportPayType（按收款方式聚合）
        // =========================================================

        [Fact]
        public async Task Receive_ReportPayType_WithData_GroupsByPayTypeName()
        {
            int year = DateTime.Now.Year;
            await InsertParamAsync(NewParam("PT1", "微信", 1, "pay_type"));
            await InsertParamAsync(NewParam("PT2", "支付宝", 2, "pay_type"));
            await InsertReceiveAsync(NewReceive("R1", "E1", new DateTime(year, 3, 10), payTypeId: "PT1"));
            await InsertReceiveAsync(NewReceive("R2", "E1", new DateTime(year, 3, 11), payTypeId: "PT1"));
            await InsertReceiveAsync(NewReceive("R3", "E1", new DateTime(year, 4, 10), payTypeId: "PT2"));

            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportPayType());

            Assert.Equal(2, arr.Count);
            var wechat = arr.First(o => (string)o["name"]! == "微信");
            var alipay = arr.First(o => (string)o["name"]! == "支付宝");
            Assert.Equal(2, (int)wechat["value"]!);
            Assert.Equal(1, (int)alipay["value"]!);
        }

        [Fact]
        public async Task Receive_ReportPayType_EmptyDB_ReturnsEmptyArray()
        {
            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportPayType());

            Assert.Empty(arr);
        }

        // =========================================================
        // Finance_Invoice.ReportYear（发票年度曲线）
        // =========================================================

        [Fact]
        public async Task Invoice_ReportYear_WithData_ReturnsTwelveMonthMatrix()
        {
            int year = DateTime.Now.Year;
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertInvoiceAsync(NewInvoice("I1", "E1", new DateTime(year, 3, 10)));
            await InsertInvoiceAsync(NewInvoice("I2", "E1", new DateTime(year, 3, 20)));
            await InsertInvoiceAsync(NewInvoice("I3", "E2", new DateTime(year, 6, 15)));
            await InsertInvoiceAsync(NewInvoice("I4", "E1", new DateTime(year - 1, 12, 31)));

            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportInvoice());

            Assert.Equal(12, arr.Count);
            Assert.Equal(2, (int)MonthOf(arr, 3)["count"]!);
            Assert.Equal(1, (int)MonthOf(arr, 6)["count"]!);
            Assert.Equal(0, (int)MonthOf(arr, 12)["count"]!);
        }

        [Fact]
        public async Task Invoice_ReportYear_EmptyDB_ReturnsAllZero()
        {
            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportInvoice());

            Assert.Equal(12, arr.Count);
            Assert.All(arr, o => Assert.Equal(0, (int)o["count"]!));
        }

        // =========================================================
        // Finance_Invoice.ReportInvoiceSum（发票金额汇总）
        // =========================================================

        [Fact]
        public async Task Invoice_ReportYearSum_WithData_ReturnsMonthlyAmountSum()
        {
            int year = DateTime.Now.Year;
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertInvoiceAsync(NewInvoice("I1", "E1", new DateTime(year, 3, 10), 1000m));
            await InsertInvoiceAsync(NewInvoice("I2", "E1", new DateTime(year, 3, 20), 2500.5m));
            await InsertInvoiceAsync(NewInvoice("I3", "E1", new DateTime(year, 5, 15), 800m));

            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportInvoiceSum());

            Assert.Equal(12, arr.Count);
            Assert.Equal(3500.5m, (decimal)MonthOf(arr, 3)["count"]!);
            Assert.Equal(800m, (decimal)MonthOf(arr, 5)["count"]!);
            Assert.Equal(0, (int)MonthOf(arr, 9)["count"]!);
        }

        [Fact]
        public async Task Invoice_ReportYearSum_EmptyDB_ReturnsAllZero()
        {
            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportInvoiceSum());

            Assert.Equal(12, arr.Count);
            Assert.All(arr, o => Assert.Equal(0, (int)o["count"]!));
        }

        // =========================================================
        // Finance_Invoice.ReportInvoiceType（按发票类型聚合）
        // =========================================================

        [Fact]
        public async Task Invoice_ReportInvoiceType_WithData_GroupsByTypeName()
        {
            int year = DateTime.Now.Year;
            await InsertParamAsync(NewParam("IT1", "增值税专用发票", 1, "invoice_type"));
            await InsertParamAsync(NewParam("IT2", "增值税普通发票", 2, "invoice_type"));
            await InsertInvoiceAsync(NewInvoice("I1", "E1", new DateTime(year, 3, 10), typeId: "IT1"));
            await InsertInvoiceAsync(NewInvoice("I2", "E1", new DateTime(year, 3, 11), typeId: "IT1"));
            await InsertInvoiceAsync(NewInvoice("I3", "E1", new DateTime(year, 4, 10), typeId: "IT2"));

            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportInvoiceType());

            Assert.Equal(2, arr.Count);
            var special = arr.First(o => (string)o["name"]! == "增值税专用发票");
            var normal = arr.First(o => (string)o["name"]! == "增值税普通发票");
            Assert.Equal(2, (int)special["value"]!);
            Assert.Equal(1, (int)normal["value"]!);
        }

        [Fact]
        public async Task Invoice_ReportInvoiceType_EmptyDB_ReturnsEmptyArray()
        {
            var ctrl = CreateController();
            var arr = JArray.Parse(await ctrl.ReportInvoiceType());

            Assert.Empty(arr);
        }
    }
}
