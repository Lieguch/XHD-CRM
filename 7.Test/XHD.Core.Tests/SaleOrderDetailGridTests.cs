using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
    /// Phase 3 W4：销售订单明细 SaleOrderDetailController.Grid 真测试。
    /// 覆盖按 order_id 过滤、有/无明细两态、分页、非全员权限下的主单归属校验。
    /// 走真实 SQLite 内存库 + 真实 Sale_order_detailsService / Sale_orderService。
    /// </summary>
    public class SaleOrderDetailGridTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sale_order_detailsRepository _detailRepo;
        private readonly Sale_orderRepository _orderRepo;

        private const string UserId = "TEST_USER";

        public SaleOrderDetailGridTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _detailRepo = new Sale_order_detailsRepository(_fsql);
            _orderRepo = new Sale_orderRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static Sale_order NewOrder(string id, string empId, string customerId = "C1")
        {
            return new Sale_order
            {
                id = id,
                customer_id = customerId,
                emp_id = empId,
                Order_date = new DateTime(2024, 5, 1),
                Order_amount = 1000m,
                total_amount = 1000m,
                sn = $"SO-{id}",
                create_id = empId,
                create_time = new DateTime(2024, 5, 1),
                isDelete = 0
            };
        }

        private static Sale_order_details NewDetail(string id, string orderId, decimal price, int qty, string productId = "P1")
        {
            return new Sale_order_details
            {
                id = id,
                order_id = orderId,
                product_id = productId,
                price = price,
                quantity = qty,
                amount = price * qty
            };
        }

        private async Task InsertOrderAsync(Sale_order o) => await _fsql.Insert(o).ExecuteAffrowsAsync();
        private async Task InsertDetailAsync(Sale_order_details d) => await _fsql.Insert(d).ExecuteAffrowsAsync();

        // ============ Controller 装配 ============

        private SaleOrderDetailController CreateController(
            string queryString = "",
            int authtype = 4,
            List<string> authEmpList = null)
        {
            var authMock = new Mock<IDBAuthService>();
            authMock.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData
                {
                    authtype = authtype,
                    empList = authEmpList ?? new List<string>()
                });

            return TestControllerHelper.CreateWithHttpContext<SaleOrderDetailController>(
                queryString, UserId, "Test User",
                new Mock<ILogger<SaleOrderDetailController>>().Object,
                new Sale_order_detailsService(_detailRepo),
                new Sale_orderService(_orderRepo),
                authMock.Object);
        }

        // ============ Grid ============

        [Fact]
        public async Task Grid_OrderWithDetails_ReturnsRows()
        {
            await InsertOrderAsync(NewOrder("O1", "E_A"));
            await InsertDetailAsync(NewDetail("D1", "O1", 100m, 2));
            await InsertDetailAsync(NewDetail("D2", "O1", 50m, 4));

            var ctrl = CreateController(queryString: "?id=O1");

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_order_details>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var amounts = ((JArray)obj["data"]!).Select(d => (decimal)d["amount"]!).ToList();
            Assert.Equal(2, amounts.Count);
            Assert.All(amounts, a => Assert.Equal(200m, a));
        }

        [Fact]
        public async Task Grid_OrderWithoutDetails_ReturnsEmpty()
        {
            await InsertOrderAsync(NewOrder("O1", "E_A"));

            var ctrl = CreateController(queryString: "?id=O1");

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_order_details>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task Grid_FilterIsolateFromOtherOrders()
        {
            await InsertOrderAsync(NewOrder("O1", "E_A"));
            await InsertOrderAsync(NewOrder("O2", "E_A"));
            await InsertDetailAsync(NewDetail("D1", "O1", 100m, 1));
            await InsertDetailAsync(NewDetail("D2", "O2", 300m, 1));

            var ctrl = CreateController(queryString: "?id=O1");

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_order_details>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal("D1", (string)((JArray)obj["data"]!)[0]["id"]!);
        }

        [Fact]
        public async Task Grid_MissingOrderIdParam_ReturnsError()
        {
            // 无 id 参数：admin 与非 admin 分支口径一致，都拒绝空 id
            var ctrl = CreateController();

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_order_details>());
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数错误！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Grid_Pagination_ReturnsPagedData()
        {
            await InsertOrderAsync(NewOrder("O1", "E_A"));
            for (int i = 0; i < 3; i++)
            {
                await InsertDetailAsync(NewDetail($"D{i}", "O1", 10m * (i + 1), 1));
            }

            var ctrl = CreateController(queryString: "?id=O1");

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_order_details>(page: 1, limit: 2));
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(3, (int)obj["count"]!);
            Assert.Equal(2, ((JArray)obj["data"]!).Count);
        }

        [Fact]
        public async Task Grid_NonFullAuth_OrderInScope_ReturnsRows()
        {
            await InsertOrderAsync(NewOrder("O1", "E_A"));
            await InsertDetailAsync(NewDetail("D1", "O1", 100m, 1));

            var ctrl = CreateController(queryString: "?id=O1", authtype: 1, authEmpList: new List<string> { "E_A" });

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_order_details>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
        }

        [Fact]
        public async Task Grid_NonFullAuth_OrderOutOfScope_ReturnsError()
        {
            await InsertOrderAsync(NewOrder("O1", "E_B"));
            await InsertDetailAsync(NewDetail("D1", "O1", 100m, 1));

            var ctrl = CreateController(queryString: "?id=O1", authtype: 1, authEmpList: new List<string> { "E_A" });

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_order_details>());
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无操作权限", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Grid_DecimalAmounts_PreservedExactly()
        {
            await InsertOrderAsync(NewOrder("O1", "E_A"));
            await InsertDetailAsync(NewDetail("D1", "O1", 12.5m, 3));

            var ctrl = CreateController(queryString: "?id=O1");

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_order_details>());
            var obj = JObject.Parse(json);
            var row = ((JArray)obj["data"]!)[0];

            Assert.Equal(12.5m, (decimal)row["price"]!);
            Assert.Equal(3, (int)row["quantity"]!);
            Assert.Equal(37.5m, (decimal)row["amount"]!);
        }
    }
}
