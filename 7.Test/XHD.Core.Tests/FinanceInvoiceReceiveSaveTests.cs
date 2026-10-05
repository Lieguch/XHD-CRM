using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.AspNetCore.Mvc;
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
    /// Phase 3 W4：发票 FinanceInvoiceController + 收款 FinanceReceiveController 真测试。
    /// 覆盖 Grid（过滤/权限）、Save（新增/更新/数据归属/订单金额联动）、
    /// Delete（存在/不存在/数据归属/订单金额回算）。
    /// 走真实 SQLite 内存库 + 真实仓储/服务，IFreeSql 直传测试库实例（不 Mock）。
    /// </summary>
    public class FinanceInvoiceReceiveSaveTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Finance_InvoiceRepository _invoiceRepo;
        private readonly Finance_ReceiveRepository _receiveRepo;
        private readonly Sale_orderRepository _orderRepo;

        private const string UserId = "TEST_USER";
        private const string OtherUser = "OTHER_USER";

        public FinanceInvoiceReceiveSaveTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _invoiceRepo = new Finance_InvoiceRepository(_fsql);
            _receiveRepo = new Finance_ReceiveRepository(_fsql);
            _orderRepo = new Sale_orderRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static CRM_Customer NewCustomer(string id, string empId)
        {
            return new CRM_Customer
            {
                id = id,
                cus_name = $"客户-{id}",
                emp_id = empId,
                create_id = empId,
                create_time = new DateTime(2024, 1, 1),
                state = 0,
                isDelete = 0,
                isPrivate = 1,
                sn = $"CU-{id}"
            };
        }

        private static Sale_order NewOrder(string id, string customerId, string empId, decimal totalAmount, string createId = null)
        {
            return new Sale_order
            {
                id = id,
                customer_id = customerId,
                emp_id = empId,
                Order_date = new DateTime(2024, 5, 1),
                Order_amount = totalAmount,
                total_amount = totalAmount,
                receive_money = 0m,
                invoice_money = 0m,
                sn = $"SO-{id}",
                create_id = createId ?? empId,
                create_time = new DateTime(2024, 5, 1),
                isDelete = 0
            };
        }

        private static Finance_Invoice NewInvoice(string id, string orderId, string num, decimal amount, DateTime date, string createId = null)
        {
            return new Finance_Invoice
            {
                id = id,
                order_id = orderId,
                invoice_num = num,
                invoice_amount = amount,
                invoice_date = date,
                invoice_content = "服务费",
                invoice_type_id = string.Empty,
                emp_id = string.Empty,
                create_id = createId ?? UserId,
                create_time = date,
                isDelete = 0
            };
        }

        private static Finance_Receive NewReceive(string id, string orderId, string num, decimal amount, DateTime date, string createId = null)
        {
            return new Finance_Receive
            {
                id = id,
                order_id = orderId,
                Receivable_id = string.Empty,
                Receive_num = num,
                Receive_amount = amount,
                Receive_date = date,
                Payee_id = "E_PAYEE",
                Pay_type_id = string.Empty,
                create_id = createId ?? UserId,
                create_time = date,
                isDelete = 0
            };
        }

        private async Task InsertCustomerAsync(CRM_Customer c) => await _fsql.Insert(c).ExecuteAffrowsAsync();
        private async Task InsertOrderAsync(Sale_order o) => await _fsql.Insert(o).ExecuteAffrowsAsync();
        private async Task InsertInvoiceAsync(Finance_Invoice i) => await _fsql.Insert(i).ExecuteAffrowsAsync();
        private async Task InsertReceiveAsync(Finance_Receive r) => await _fsql.Insert(r).ExecuteAffrowsAsync();

        // ============ Controller 装配 ============

        private static Mock<IDBAuthService> CreateAuthMock(int authtype, List<string> empList)
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = authtype, empList = empList ?? new List<string>() });
            return auth;
        }

        private static Mock<ISys_logService> CreateLogMock()
        {
            var logMock = new Mock<ISys_logService>();
            logMock.Setup(l => l.UpdateLog(It.IsAny<Sys_log>())).ReturnsAsync(1);
            logMock.Setup(l => l.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);
            return logMock;
        }

        private FinanceInvoiceController CreateInvoiceController(
            string queryString = "",
            Mock<IDBAuthService> authMock = null,
            Mock<ISys_logService> logMock = null,
            string userId = UserId)
        {
            authMock ??= CreateAuthMock(5, new List<string>());
            logMock ??= CreateLogMock();

            return TestControllerHelper.CreateWithHttpContext<FinanceInvoiceController>(
                queryString, userId, "Test User",
                new Mock<ILogger<FinanceInvoiceController>>().Object,
                new Finance_InvoiceService(_invoiceRepo),
                logMock.Object,
                authMock.Object,
                new Sale_orderService(_orderRepo));
        }

        private FinanceReceiveController CreateReceiveController(
            string queryString = "",
            Mock<IDBAuthService> authMock = null,
            Mock<ISys_logService> logMock = null,
            string userId = UserId)
        {
            authMock ??= CreateAuthMock(5, new List<string>());
            logMock ??= CreateLogMock();

            // 第 3 个构造参数 IFreeSql 直传测试库实例（不 Mock）
            return TestControllerHelper.CreateWithHttpContext<FinanceReceiveController>(
                queryString, userId, "Test User",
                new Mock<ILogger<FinanceReceiveController>>().Object,
                new Finance_ReceiveService(_receiveRepo),
                _fsql,
                new Sale_orderService(_orderRepo),
                logMock.Object,
                authMock.Object);
        }

        // =========================================================
        // FinanceInvoiceController.Grid
        // =========================================================

        [Fact]
        public async Task Invoice_Grid_EmptyTable_ReturnsEmpty()
        {
            var ctrl = CreateInvoiceController();

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Finance_Invoice>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task Invoice_Grid_FilterByInvoiceNum_ReturnsMatched()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 3000m));
            await InsertInvoiceAsync(NewInvoice("I1", "O1", "INV-001", 1000m, new DateTime(2024, 6, 1)));
            await InsertInvoiceAsync(NewInvoice("I2", "O1", "INV-002", 2000m, new DateTime(2024, 7, 1)));

            var ctrl = CreateInvoiceController(queryString: "?invoice_num=INV-001");

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Finance_Invoice>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal("I1", (string)((JArray)obj["data"]!)[0]["id"]!);
        }

        [Fact]
        public async Task Invoice_Grid_FilterByCusName_ReturnsMatched()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertCustomerAsync(NewCustomer("C2", "E_B"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 3000m));
            await InsertOrderAsync(NewOrder("O2", "C2", "E_B", 3000m));
            await InsertInvoiceAsync(NewInvoice("I1", "O1", "INV-001", 1000m, new DateTime(2024, 6, 1)));
            await InsertInvoiceAsync(NewInvoice("I2", "O2", "INV-002", 2000m, new DateTime(2024, 7, 1)));

            var ctrl = CreateInvoiceController(queryString: "?cus_name=客户-C2");

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Finance_Invoice>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal("I2", (string)((JArray)obj["data"]!)[0]["id"]!);
        }

        [Fact]
        public async Task Invoice_Grid_Authtype0_ReturnsEmpty()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 3000m));
            await InsertInvoiceAsync(NewInvoice("I1", "O1", "INV-001", 1000m, new DateTime(2024, 6, 1)));

            var ctrl = CreateInvoiceController(authMock: CreateAuthMock(0, new List<string>()));

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Finance_Invoice>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task Invoice_Grid_NonFullAuth_FiltersByOrderCustomerEmpId()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertCustomerAsync(NewCustomer("C2", "E_B"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 3000m));
            await InsertOrderAsync(NewOrder("O2", "C2", "E_B", 3000m));
            await InsertInvoiceAsync(NewInvoice("I1", "O1", "INV-001", 1000m, new DateTime(2024, 6, 1)));
            await InsertInvoiceAsync(NewInvoice("I2", "O2", "INV-002", 2000m, new DateTime(2024, 7, 1)));

            var ctrl = CreateInvoiceController(authMock: CreateAuthMock(1, new List<string> { "E_A" }));

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Finance_Invoice>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal("I1", (string)((JArray)obj["data"]!)[0]["id"]!);
        }

        // =========================================================
        // FinanceInvoiceController.Save
        // =========================================================

        [Fact]
        public async Task Invoice_Save_New_InvoicePersistedAndOrderInvoiceMoneyUpdated()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 3000m));
            await InsertInvoiceAsync(NewInvoice("I1", "O1", "INV-001", 1000m, new DateTime(2024, 6, 1)));

            var ctrl = CreateInvoiceController();
            var model = NewInvoice(string.Empty, "O1", "INV-002", 1500m, new DateTime(2024, 6, 5), string.Empty);

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var invoices = await _invoiceRepo.GridAsync(a => a.order_id == "O1");
            Assert.Equal(2, invoices.Count);

            // 订单开票汇总联动：1000 + 1500 = 2500，未开票余额 3000 - 2500 = 500
            var order = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(2500m, order.invoice_money);
            Assert.Equal(500m, order.arrears_invoice);
        }

        [Fact]
        public async Task Invoice_Save_Update_ExistingInvoice_Updated()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 3000m));
            await InsertInvoiceAsync(NewInvoice("I1", "O1", "INV-001", 1000m, new DateTime(2024, 6, 1)));

            var ctrl = CreateInvoiceController();
            var model = NewInvoice("I1", "O1", "INV-001-X", 2000m, new DateTime(2024, 6, 2), "FORGED");

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<Finance_Invoice>().Where(a => a.id == "I1").FirstAsync();
            Assert.Equal("INV-001-X", fetched.invoice_num);
            Assert.Equal(2000m, fetched.invoice_amount);
            Assert.Equal(UserId, fetched.create_id);

            // 金额变动同步到订单
            var order = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(2000m, order.invoice_money);
        }

        [Fact]
        public async Task Invoice_Save_Update_NotFound_ReturnsError()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 3000m));

            var ctrl = CreateInvoiceController();
            var model = NewInvoice("NOT_EXIST", "O1", "INV-999", 1000m, new DateTime(2024, 6, 2));

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("找不到数据！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Invoice_Save_Update_OutOfScope_ReturnsError()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_B"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_B", 3000m));
            await InsertInvoiceAsync(NewInvoice("I1", "O1", "INV-001", 1000m, new DateTime(2024, 6, 1), OtherUser));

            var ctrl = CreateInvoiceController(authMock: CreateAuthMock(1, new List<string> { UserId }));
            var model = NewInvoice("I1", "O1", "INV-001-X", 2000m, new DateTime(2024, 6, 2));

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无权限！", (string)obj["msg"]!);
        }

        // =========================================================
        // FinanceInvoiceController.Delete
        // =========================================================

        [Fact]
        public async Task Invoice_Delete_NotFound_ReturnsError()
        {
            var ctrl = CreateInvoiceController();

            var json = await ctrl.Delete("NOT_EXIST");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("找不到数据！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Invoice_Delete_Success_OrderInvoiceMoneyRecalculated()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 3000m));
            await InsertInvoiceAsync(NewInvoice("I1", "O1", "INV-001", 1000m, new DateTime(2024, 6, 1)));
            await InsertInvoiceAsync(NewInvoice("I2", "O1", "INV-002", 2000m, new DateTime(2024, 7, 1)));

            var ctrl = CreateInvoiceController();

            var json = await ctrl.Delete("I2");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Null(await _fsql.Select<Finance_Invoice>().Where(a => a.id == "I2").FirstAsync());

            var order = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(1000m, order.invoice_money);
            Assert.Equal(2000m, order.arrears_invoice);
        }

        [Fact]
        public async Task Invoice_Delete_OutOfScope_ReturnsError()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_B"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_B", 3000m));
            await InsertInvoiceAsync(NewInvoice("I1", "O1", "INV-001", 1000m, new DateTime(2024, 6, 1), OtherUser));

            var ctrl = CreateInvoiceController(authMock: CreateAuthMock(1, new List<string> { UserId }));

            var json = await ctrl.Delete("I1");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无权限！", (string)obj["msg"]!);
            Assert.NotNull(await _fsql.Select<Finance_Invoice>().Where(a => a.id == "I1").FirstAsync());
        }

        // =========================================================
        // FinanceReceiveController.Grid
        // =========================================================

        [Fact]
        public async Task Receive_Grid_FilterByPayeeId_ReturnsMatched()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 3000m));
            await InsertReceiveAsync(NewReceive("R1", "O1", "RC-001", 1000m, new DateTime(2024, 6, 10)));
            var r2 = NewReceive("R2", "O1", "RC-002", 2000m, new DateTime(2024, 7, 10));
            r2.Payee_id = "E_OTHER";
            await InsertReceiveAsync(r2);

            var ctrl = CreateReceiveController(queryString: "?emp_id=E_OTHER");

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Finance_Receive>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal("R2", (string)((JArray)obj["data"]!)[0]["id"]!);
        }

        [Fact]
        public async Task Receive_Grid_NonFullAuth_FiltersByOrderCustomerEmpId()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertCustomerAsync(NewCustomer("C2", "E_B"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 3000m));
            await InsertOrderAsync(NewOrder("O2", "C2", "E_B", 3000m));
            await InsertReceiveAsync(NewReceive("R1", "O1", "RC-001", 1000m, new DateTime(2024, 6, 10)));
            await InsertReceiveAsync(NewReceive("R2", "O2", "RC-002", 2000m, new DateTime(2024, 7, 10)));

            var ctrl = CreateReceiveController(authMock: CreateAuthMock(1, new List<string> { "E_B" }));

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Finance_Receive>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal("R2", (string)((JArray)obj["data"]!)[0]["id"]!);
        }

        // =========================================================
        // FinanceReceiveController.Save
        // =========================================================

        [Fact]
        public async Task Receive_Save_New_ReceivePersistedAndOrderReceiveMoneyUpdated()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 2000m));

            var ctrl = CreateReceiveController();
            var model = NewReceive(string.Empty, "O1", "RC-001", 800m, new DateTime(2024, 6, 10), string.Empty);

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var receives = await _receiveRepo.GridAsync(a => a.order_id == "O1");
            Assert.Single(receives);
            Assert.Equal(800m, receives[0].Receive_amount);

            // 订单收款汇总联动：已收 800，欠款 2000 - 800 = 1200
            var order = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(800m, order.receive_money);
            Assert.Equal(1200m, order.arrears_money);
        }

        [Fact]
        public async Task Receive_Save_Update_ExistingReceive_Updated()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 2000m));
            await InsertReceiveAsync(NewReceive("R1", "O1", "RC-001", 800m, new DateTime(2024, 6, 10)));

            var ctrl = CreateReceiveController();
            var model = NewReceive("R1", "O1", "RC-001-X", 1500m, new DateTime(2024, 6, 11), "FORGED");

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<Finance_Receive>().Where(a => a.id == "R1").FirstAsync();
            Assert.Equal("RC-001-X", fetched.Receive_num);
            Assert.Equal(1500m, fetched.Receive_amount);
            Assert.Equal(UserId, fetched.create_id);

            var order = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(1500m, order.receive_money);
            Assert.Equal(500m, order.arrears_money);
        }

        // =========================================================
        // FinanceReceiveController.Delete
        // =========================================================

        [Fact]
        public async Task Receive_Delete_NotFound_ReturnsError()
        {
            var ctrl = CreateReceiveController();

            var json = await ctrl.Delete("NOT_EXIST");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("找不到数据！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Receive_Delete_Success_OrderReceiveMoneyRecalculated()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertOrderAsync(NewOrder("O1", "C1", "E_A", 2000m));
            await InsertReceiveAsync(NewReceive("R1", "O1", "RC-001", 800m, new DateTime(2024, 6, 10)));
            await InsertReceiveAsync(NewReceive("R2", "O1", "RC-002", 1200m, new DateTime(2024, 7, 10)));

            var ctrl = CreateReceiveController();

            var json = await ctrl.Delete("R2");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Null(await _fsql.Select<Finance_Receive>().Where(a => a.id == "R2").FirstAsync());

            var order = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(800m, order.receive_money);
            Assert.Equal(1200m, order.arrears_money);
        }
    }
}
