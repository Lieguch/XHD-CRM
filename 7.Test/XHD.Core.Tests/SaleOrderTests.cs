using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 4 Wave 1a 销售订单报表单元测试。
    /// 覆盖 #03 gridbycustomerid（端点）+ #07/#08/#09 报表（仓库 + 端点）。
    /// </summary>
    public class SaleOrderTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sale_orderRepository _orderRepo;

        public SaleOrderTests()
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

        private static CRM_Customer NewCustomer(string id, string empId = "E1")
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

        private static Sale_order NewOrder(string id, DateTime orderDate, string empId, string customerId, decimal amount = 1000m)
        {
            return new Sale_order
            {
                id = id,
                customer_id = customerId,
                emp_id = empId,
                Order_date = orderDate,
                Order_amount = amount,
                total_amount = amount,
                create_id = empId,
                create_time = orderDate,
                isDelete = 0
            };
        }

        private async Task InsertEmployeeAsync(hr_employee e) => await _fsql.Insert(e).ExecuteAffrowsAsync();
        private async Task InsertCustomerAsync(CRM_Customer c) => await _fsql.Insert(c).ExecuteAffrowsAsync();
        private async Task InsertOrderAsync(Sale_order o) => await _fsql.Insert(o).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        private SaleOrderController CreateController(
            string queryString = "",
            string userId = "TEST_USER",
            int authtype = 4,
            List<string> authEmpList = null)
        {
            // 用真实 Repository 的 Service mock，直接转发到 Repository
            var svcMock = new Mock<ISale_orderService>();
            // GridAsync 转发到真实 Repository（用于 GridByCustomerId 端点测试）
            svcMock.Setup(s => s.GridAsync(
                It.IsAny<Expression<Func<Sale_order, bool>>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<Sale_order, bool>> exp, int page, int limit, string orderby) =>
                    _orderRepo.GridAsync(exp, page, limit, orderby));
            svcMock.Setup(s => s.ComparedEmpCusOrderAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns((int y1, int m1, int y2, int m2, List<string> eids) =>
                    _orderRepo.ComparedEmpCusOrderAsync(y1, m1, y2, m2, eids));
            svcMock.Setup(s => s.ReportMonthEmpOrderAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<List<string>>()))
                .Returns((DateTime s, DateTime e, List<string> eids) =>
                    _orderRepo.ReportMonthEmpOrderAsync(s, e, eids));
            svcMock.Setup(s => s.ReportEmpOrderAsync(
                It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns((int y, List<string> eids) =>
                    _orderRepo.ReportEmpOrderAsync(y, eids));

            var logger = new Mock<ILogger<SaleOrderController>>().Object;
            var detailsSvc = new Mock<ISale_order_detailsService>().Object;
            var invoiceSvc = new Mock<IFinance_InvoiceService>().Object;
            var receiveSvc = new Mock<IFinance_ReceiveService>().Object;
            var logSvc = new Mock<ISys_logService>().Object;

            var authMock = new Mock<IDBAuthService>();
            authMock.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData
                {
                    authtype = authtype,
                    empList = authEmpList ?? new List<string>()
                });

            var ctrl = new SaleOrderController(
                logger, svcMock.Object, detailsSvc, invoiceSvc, receiveSvc, logSvc, authMock.Object);

            var httpCtx = new DefaultHttpContext();
            if (!string.IsNullOrEmpty(queryString))
            {
                var qs = queryString.StartsWith("?") ? queryString.Substring(1) : queryString;
                httpCtx.Request.QueryString = new QueryString($"?{qs}");
            }
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

        // =========================================================
        // #03 GridByCustomerId 端点（客户详情页订单查询）
        // =========================================================

        [Fact]
        public async Task Controller_GridByCustomerId_ValidId_ReturnsMatchingOrders()
        {
            // Arrange：两个客户，各有不同数量的订单（customer_id 必须是 GUID 才能通过 PageValidate.checkID）
            const string cusGuid1 = "11111111-1111-1111-1111-111111111111";
            const string cusGuid2 = "22222222-2222-2222-2222-222222222222";
            await InsertCustomerAsync(NewCustomer(cusGuid1, "E1"));
            await InsertCustomerAsync(NewCustomer(cusGuid2, "E1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 1), "E1", cusGuid1, 1000m));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 4, 1), "E1", cusGuid1, 2000m));
            await InsertOrderAsync(NewOrder("O3", new DateTime(2024, 5, 1), "E1", cusGuid2, 3000m));

            // Act
            var ctrl = CreateController();
            var json = await ctrl.GridByCustomerId(cusGuid1);
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(2, data.Count);
            var ids = data.Select(d => (string)d["id"]).ToList();
            Assert.Contains("O1", ids);
            Assert.Contains("O2", ids);
            Assert.DoesNotContain("O3", ids);
        }

        [Fact]
        public async Task Controller_GridByCustomerId_NoMatchingOrders_ReturnsEmpty()
        {
            // Arrange：只有一个客户（GUID），但无订单
            const string cusGuid = "33333333-3333-3333-3333-333333333333";
            await InsertCustomerAsync(NewCustomer(cusGuid, "E1"));

            var ctrl = CreateController();
            var json = await ctrl.GridByCustomerId(cusGuid);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Empty(data);
        }

        [Fact]
        public async Task Controller_GridByCustomerId_WithPagination_ReturnsPagedData()
        {
            // Arrange：3 条订单
            const string cusGuid = "44444444-4444-4444-4444-444444444444";
            await InsertCustomerAsync(NewCustomer(cusGuid, "E1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 1), "E1", cusGuid));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 4, 1), "E1", cusGuid));
            await InsertOrderAsync(NewOrder("O3", new DateTime(2024, 5, 1), "E1", cusGuid));

            var ctrl = CreateController();
            var json = await ctrl.GridByCustomerId(cusGuid, page: 1, limit: 2);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            // GridAsync 返回 XHDData 有 count 字段
            Assert.True(obj["count"] != null && (int)obj["count"]! == 3, $"count 应为 3，实际 {obj["count"]}");
            Assert.Equal(2, data.Count);
        }

        [Fact]
        public async Task Controller_GridByCustomerId_InvalidId_ReturnsError()
        {
            // Arrange：非 GUID 字符串
            var ctrl = CreateController();
            var json = await ctrl.GridByCustomerId("not-a-guid");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("客户ID无效", (string)obj["msg"]!);
        }

        // =========================================================
        // #07 ComparedEmpCusOrderAsync（员工双月订单对比）
        // =========================================================

        [Fact]
        public async Task ComparedEmpCusOrder_WithEmpIds_OnlyFilteredEmployees()
        {
            // Arrange：E1 3 月 2 条 + 4 月 1 条；E2 3 月 1 条 + 4 月 2 条
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 3, 15), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O3", new DateTime(2024, 4, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O4", new DateTime(2024, 3, 10), "E2", "C1"));
            await InsertOrderAsync(NewOrder("O5", new DateTime(2024, 4, 10), "E2", "C1"));
            await InsertOrderAsync(NewOrder("O6", new DateTime(2024, 4, 20), "E2", "C1"));

            // Act：只统计 E2
            var arr = await _orderRepo.ComparedEmpCusOrderAsync(2024, 3, 2024, 4, new List<string> { "E2" });

            // Assert
            Assert.Single(arr);
            Assert.Equal("李四", (string)arr[0]["yy"]!);
            Assert.Equal(1, (int)arr[0]["dt1"]!);
            Assert.Equal(2, (int)arr[0]["dt2"]!);
        }

        [Fact]
        public async Task ComparedEmpCusOrder_EmptyEmpIds_ReturnsAllEmployees()
        {
            // Arrange
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 4, 5), "E2", "C1"));

            // Act：empIds=null 表示全部
            var arr = await _orderRepo.ComparedEmpCusOrderAsync(2024, 3, 2024, 4, null);

            // Assert
            Assert.Equal(2, arr.Count);
            var e1 = arr.FirstOrDefault(o => (string)o["yy"] == "张三");
            var e2 = arr.FirstOrDefault(o => (string)o["yy"] == "李四");
            Assert.NotNull(e1);
            Assert.NotNull(e2);
            Assert.Equal(1, (int)e1["dt1"]!);
            Assert.Equal(0, (int)e1["dt2"]!);
            Assert.Equal(0, (int)e2["dt1"]!);
            Assert.Equal(1, (int)e2["dt2"]!);
        }

        [Fact]
        public async Task ComparedEmpCusOrder_SameMonthTwice_ReturnsSameCountInBothSlots()
        {
            // Arrange：同月对比（year1=year2, month1=month2）
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 3, 15), "E1", "C1"));

            var arr = await _orderRepo.ComparedEmpCusOrderAsync(2024, 3, 2024, 3, null);

            Assert.Single(arr);
            Assert.Equal(2, (int)arr[0]["dt1"]!);
            Assert.Equal(2, (int)arr[0]["dt2"]!);
        }

        [Fact]
        public async Task ComparedEmpCusOrder_EmptyDB_ReturnsEmptyArray()
        {
            var arr = await _orderRepo.ComparedEmpCusOrderAsync(2024, 3, 2024, 4, null);

            Assert.Empty(arr);
        }

        [Fact]
        public async Task ComparedEmpCusOrder_CrossYear_CorrectCounts()
        {
            // Arrange：跨年对比 — 2023-12 vs 2024-01
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2023, 12, 15), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 1, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O3", new DateTime(2024, 1, 20), "E1", "C1"));

            var arr = await _orderRepo.ComparedEmpCusOrderAsync(2023, 12, 2024, 1, null);

            Assert.Single(arr);
            Assert.Equal(1, (int)arr[0]["dt1"]!);
            Assert.Equal(2, (int)arr[0]["dt2"]!);
        }

        // =========================================================
        // #08 ReportMonthEmpOrderAsync（员工月度订单矩阵）
        // =========================================================

        [Fact]
        public async Task ReportMonthEmpOrder_MultipleEmployees_ReturnsMatrix()
        {
            // Arrange：E1 3 月 2 条 + 4 月 1 条；E2 3 月 1 条
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 3, 15), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O3", new DateTime(2024, 4, 10), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O4", new DateTime(2024, 3, 20), "E2", "C1"));

            // Act
            var arr = await _orderRepo.ReportMonthEmpOrderAsync(
                new DateTime(2024, 1, 1),
                new DateTime(2024, 12, 31, 23, 59, 59),
                null);

            // Assert
            Assert.Equal(2, arr.Count);
            var e1 = arr.FirstOrDefault(o => (string)o["name"] == "张三");
            var e2 = arr.FirstOrDefault(o => (string)o["name"] == "李四");
            Assert.NotNull(e1);
            Assert.NotNull(e2);
            Assert.Equal(2024, (int)e1["yy"]!);
            Assert.Equal(2, (int)e1["m3"]!);
            Assert.Equal(1, (int)e1["m4"]!);
            Assert.Equal(1, (int)e2["m3"]!);
            Assert.Equal(0, (int)e2["m4"]!);
        }

        [Fact]
        public async Task ReportMonthEmpOrder_WithEmpIdsFilter_OnlyFiltered()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 3, 10), "E2", "C1"));

            var arr = await _orderRepo.ReportMonthEmpOrderAsync(
                new DateTime(2024, 1, 1),
                new DateTime(2024, 12, 31, 23, 59, 59),
                new List<string> { "E2" });

            Assert.Single(arr);
            Assert.Equal("李四", (string)arr[0]["name"]!);
            Assert.Equal(1, (int)arr[0]["m3"]!);
        }

        [Fact]
        public void ReportMonthEmpOrder_StartAfterEnd_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                _orderRepo.ReportMonthEmpOrderAsync(
                    new DateTime(2024, 6, 1),
                    new DateTime(2024, 3, 1),
                    null).GetAwaiter().GetResult());
        }

        [Fact]
        public async Task ReportMonthEmpOrder_EmptyDB_ReturnsEmptyArray()
        {
            var arr = await _orderRepo.ReportMonthEmpOrderAsync(
                new DateTime(2024, 1, 1),
                new DateTime(2024, 12, 31, 23, 59, 59),
                null);

            Assert.Empty(arr);
        }

        // =========================================================
        // #09 ReportEmpOrderAsync（员工年度订单矩阵）
        // =========================================================

        [Fact]
        public async Task ReportEmpOrder_YearlyMatrix_CorrectCounts()
        {
            // Arrange：E1 全年 3 月 2 条 + 6 月 3 条
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 3, 15), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O3", new DateTime(2024, 6, 10), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O4", new DateTime(2024, 6, 20), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O5", new DateTime(2024, 6, 25), "E1", "C1"));

            // Act
            var arr = await _orderRepo.ReportEmpOrderAsync(2024, null);

            // Assert
            Assert.Single(arr);
            Assert.Equal("张三", (string)arr[0]["name"]!);
            Assert.Equal(2024, (int)arr[0]["yy"]!);
            Assert.Equal(2, (int)arr[0]["m3"]!);
            Assert.Equal(3, (int)arr[0]["m6"]!);
            Assert.Equal(0, (int)arr[0]["m1"]!);
            Assert.Equal(0, (int)arr[0]["m12"]!);
        }

        [Fact]
        public async Task ReportEmpOrder_WithEmpIdsFilter_OnlyFiltered()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 3, 10), "E2", "C1"));

            var arr = await _orderRepo.ReportEmpOrderAsync(2024, new List<string> { "E2" });

            Assert.Single(arr);
            Assert.Equal("李四", (string)arr[0]["name"]!);
            Assert.Equal(1, (int)arr[0]["m3"]!);
        }

        [Fact]
        public async Task ReportEmpOrder_CrossYear_FiltersCorrectYear()
        {
            // Arrange：E1 有 2023 和 2024 两批订单
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2023, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2023, 6, 10), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O3", new DateTime(2024, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O4", new DateTime(2024, 11, 15), "E1", "C1"));

            // Act：只统计 2024
            var arr2024 = await _orderRepo.ReportEmpOrderAsync(2024, null);
            var arr2023 = await _orderRepo.ReportEmpOrderAsync(2023, null);

            // Assert
            Assert.Single(arr2024);
            Assert.Single(arr2023);
            Assert.Equal(1, (int)arr2024[0]["m3"]!);
            Assert.Equal(1, (int)arr2024[0]["m11"]!);
            Assert.Equal(0, (int)arr2024[0]["m6"]!);
            Assert.Equal(1, (int)arr2023[0]["m3"]!);
            Assert.Equal(1, (int)arr2023[0]["m6"]!);
            Assert.Equal(0, (int)arr2023[0]["m11"]!);
        }

        // =========================================================
        // Controller 端点校验（#07 #08 #09 边界）
        // =========================================================

        [Fact]
        public async Task Controller_ComparedEmpCusOrder_InvalidMonth_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.ComparedEmpCusOrder(null!, 2024, 13, 2024, 4);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("月份", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Controller_EmpMonthCusOrder_InvalidDates_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.EmpMonthCusOrder(null!, "2024-06-01", "2024-03-01");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("开始时间", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Controller_EmpCusOrder_InvalidYear_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.EmpCusOrder(null!, 1900);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("年份", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Controller_ComparedEmpCusOrder_ValidParams_ReturnsSuccess()
        {
            // Arrange：E1 3 月 1 条 + 4 月 1 条
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 4, 5), "E1", "C1"));

            var ctrl = CreateController();

            var json = await ctrl.ComparedEmpCusOrder(null!, 2024, 3, 2024, 4);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("张三", (string)data[0]["yy"]!);
            Assert.Equal(1, (int)data[0]["dt1"]!);
            Assert.Equal(1, (int)data[0]["dt2"]!);
        }

        [Fact]
        public async Task Controller_EmpMonthCusOrder_ValidParams_ReturnsMatrix()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 4, 10), "E1", "C1"));

            var ctrl = CreateController();

            var json = await ctrl.EmpMonthCusOrder(null!, "2024-01-01", "2024-06-30");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("张三", (string)data[0]["name"]!);
            Assert.Equal(1, (int)data[0]["m3"]!);
            Assert.Equal(1, (int)data[0]["m4"]!);
        }

        [Fact]
        public async Task Controller_EmpCusOrder_ValidParams_ReturnsYearMatrix()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertOrderAsync(NewOrder("O1", new DateTime(2024, 3, 5), "E1", "C1"));
            await InsertOrderAsync(NewOrder("O2", new DateTime(2024, 6, 15), "E1", "C1"));

            var ctrl = CreateController();

            var json = await ctrl.EmpCusOrder(null!, 2024);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("张三", (string)data[0]["name"]!);
            Assert.Equal(2024, (int)data[0]["yy"]!);
            Assert.Equal(1, (int)data[0]["m3"]!);
            Assert.Equal(1, (int)data[0]["m6"]!);
        }
    }
}
