using FreeSql;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// #09-#12 应收单 CRUD + 三向联动更新 + 订单重算 单元测试。
    /// </summary>
    public class FinanceReceivableTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Finance_ReceivableRepository _recvRepo;
        private readonly Finance_ReceiveRepository _receiveRepo;
        private readonly Sale_orderRepository _orderRepo;

        public FinanceReceivableTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _recvRepo = new Finance_ReceivableRepository(_fsql);
            _receiveRepo = new Finance_ReceiveRepository(_fsql);
            _orderRepo = new Sale_orderRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ========== 测试数据工厂 ==========

        private static Sale_order NewOrder(
            string id,
            decimal orderAmount,
            string customer = "CUST001",
            string emp = "E001")
        {
            return new Sale_order
            {
                id = id,
                Order_amount = orderAmount,
                total_amount = orderAmount,
                arrears_money = orderAmount,
                receive_money = 0m,
                customer_id = customer,
                emp_id = emp,
                sn = $"SO-{id}",
                create_time = new DateTime(2024, 6, 1)
            };
        }

        private static Finance_Receivable NewReceivable(
            string id,
            string orderId,
            decimal receivableAmount,
            decimal receivedAmount = 0m,
            DateTime? receivableTime = null,
            DateTime? createTime = null,
            int isDelete = 0)
        {
            return new Finance_Receivable
            {
                id = id,
                receivable_no = $"YS-{id}",
                order_id = orderId,
                receivable_amount = receivableAmount,
                received_amount = receivedAmount,
                arrears_amount = receivableAmount - receivedAmount,
                receivable_time = receivableTime ?? new DateTime(2024, 6, 15),
                create_time = createTime ?? new DateTime(2024, 6, 14, 10, 0, 0),
                create_id = "E001",
                isDelete = isDelete
            };
        }

        private static Finance_Receive NewReceive(
            string id,
            string receivableId,
            string orderId,
            decimal receiveAmount)
        {
            return new Finance_Receive
            {
                id = id,
                Receivable_id = receivableId,
                order_id = orderId,
                Receive_amount = receiveAmount,
                Receive_date = new DateTime(2024, 6, 20),
                create_id = "E001",
                create_time = new DateTime(2024, 6, 20, 9, 0, 0),
                Receive_num = $"RC-{id}",
                isDelete = 0
            };
        }

        private async Task InsertOrderAsync(Sale_order order)
            => await _fsql.Insert(order).ExecuteAffrowsAsync();

        private async Task InsertReceivableAsync(Finance_Receivable r)
            => await _fsql.Insert(r).ExecuteAffrowsAsync();

        private async Task InsertReceiveAsync(Finance_Receive r)
            => await _fsql.Insert(r).ExecuteAffrowsAsync();

        // =========================================================
        // #09 Finance_Receivable CRUD 测试
        // =========================================================

        [Fact]
        public async Task Receivable_AddAsync_PersistsNewRecord()
        {
            // Arrange
            var r = NewReceivable("R1", "O1", 1000m);

            // Act
            var rows = await _recvRepo.AddAsync(r);

            // Assert
            Assert.Equal(1, rows);
            var fetched = await _fsql.Select<Finance_Receivable>().Where(a => a.id == "R1").FirstAsync();
            Assert.NotNull(fetched);
            Assert.Equal("YS-R1", fetched.receivable_no);
            Assert.Equal(1000m, fetched.receivable_amount);
            Assert.Equal("O1", fetched.order_id);
            Assert.NotNull(fetched.create_time);
        }

        [Fact]
        public async Task Receivable_UpdateAsync_PreservesCreateTime()
        {
            // Arrange
            var original = NewReceivable("R1", "O1", 1000m, createTime: new DateTime(2024, 1, 1, 8, 0, 0));
            await InsertReceivableAsync(original);

            // Act：修改金额和已收，create_time 应保持
            original.receivable_amount = 2000m;
            original.receivable_no = "YS-UPDATE";
            await _recvRepo.UpdateAsync(original);

            // Assert
            var fetched = await _fsql.Select<Finance_Receivable>().Where(a => a.id == "R1").FirstAsync();
            Assert.Equal(2000m, fetched.receivable_amount);
            Assert.Equal("YS-UPDATE", fetched.receivable_no);
            Assert.Equal(new DateTime(2024, 1, 1, 8, 0, 0), fetched.create_time.Value);
        }

        [Fact]
        public async Task Receivable_Grid_ReturnsXHDData_Paginated()
        {
            // Arrange
            for (int i = 0; i < 10; i++)
            {
                await InsertReceivableAsync(NewReceivable($"R{i:D2}", "O1", 100m + i,
                    createTime: new DateTime(2024, 6, 1).AddDays(i)));
            }

            // Act
            var result = await _recvRepo.GridAsync(a => a.isDelete == 0, 1, 5);

            // Assert
            Assert.Equal(10, result.count);
            Assert.Equal(5, result.data.Count);
        }

        [Fact]
        public async Task Receivable_Grid_ByOrderId_Filter()
        {
            // Arrange：3 张应收，2 张属于 O1，1 张属于 O2
            await InsertReceivableAsync(NewReceivable("R1", "O1", 100));
            await InsertReceivableAsync(NewReceivable("R2", "O1", 200));
            await InsertReceivableAsync(NewReceivable("R3", "O2", 300));

            // Act
            var result = await _recvRepo.GridAsync(a => a.order_id == "O1" && a.isDelete == 0, 1, 30);

            // Assert
            Assert.Equal(2, result.count);
            Assert.All(result.data, r => Assert.Equal("O1", r.order_id));
        }

        [Fact]
        public async Task Receivable_Grid_ByReceivableNo_ContainsFilter()
        {
            // Arrange
            await InsertReceivableAsync(NewReceivable("R1", "O1", 100));
            await InsertReceivableAsync(NewReceivable("R2", "O1", 200));
            await InsertReceivableAsync(NewReceivable("R3", "O1", 300));

            // Act
            var result = await _recvRepo.GridAsync(a => a.receivable_no.Contains("YS-R") && a.isDelete == 0, 1, 30);

            // Assert
            Assert.Equal(3, result.count);
        }

        [Fact]
        public async Task Receivable_Grid_DateRangeFilter_Works()
        {
            // Arrange
            var start = new DateTime(2024, 3, 1);
            var end = new DateTime(2024, 3, 31, 23, 59, 59);
            await InsertReceivableAsync(NewReceivable("R1", "O1", 100, receivableTime: new DateTime(2024, 3, 15)));
            await InsertReceivableAsync(NewReceivable("R2", "O1", 200, receivableTime: new DateTime(2024, 3, 20)));
            await InsertReceivableAsync(NewReceivable("R3", "O1", 300, receivableTime: new DateTime(2024, 4, 10)));
            await InsertReceivableAsync(NewReceivable("R4", "O1", 400, receivableTime: new DateTime(2024, 2, 25)));

            // Act
            var result = await _recvRepo.GridAsync(a =>
                    a.isDelete == 0
                    && a.receivable_time >= start
                    && a.receivable_time <= end, 1, 30);

            // Assert
            Assert.Equal(2, result.count);
            Assert.Contains(result.data, r => r.id == "R1");
            Assert.Contains(result.data, r => r.id == "R2");
        }

        [Fact]
        public async Task Receivable_Grid_MinAmountFilter_Works()
        {
            await InsertReceivableAsync(NewReceivable("R1", "O1", 100));
            await InsertReceivableAsync(NewReceivable("R2", "O1", 500));
            await InsertReceivableAsync(NewReceivable("R3", "O1", 1000));

            var result = await _recvRepo.GridAsync(a => a.isDelete == 0 && a.receivable_amount >= 500m, 1, 30);

            Assert.Equal(2, result.count);
        }

        [Fact]
        public async Task Receivable_Grid_EmptyDB_ReturnsEmpty()
        {
            var result = await _recvRepo.GridAsync(a => a.isDelete == 0, 1, 30);

            Assert.Equal(0, result.count);
            Assert.Empty(result.data);
        }

        [Fact]
        public async Task Receivable_Grid_ExcludesSoftDeleted()
        {
            await InsertReceivableAsync(NewReceivable("R1", "O1", 100, isDelete: 0));
            await InsertReceivableAsync(NewReceivable("R2", "O1", 200, isDelete: 1));
            await InsertReceivableAsync(NewReceivable("R3", "O1", 300, isDelete: 0));

            var result = await _recvRepo.GridAsync(a => a.isDelete == 0, 1, 30);

            Assert.Equal(2, result.count);
            Assert.DoesNotContain(result.data, r => r.id == "R2");
        }

        [Fact]
        public async Task Receivable_Delete_SoftDelete_RemovesFromGrid()
        {
            // Arrange
            var original = NewReceivable("R1", "O1", 100);
            await InsertReceivableAsync(original);

            // 验证初始 Grid 包含
            var before = await _recvRepo.GridAsync(a => a.isDelete == 0, 1, 30);
            Assert.Equal(1, before.count);

            // Act：软删除
            await _recvRepo.UpdateAsync(
                a => new Finance_Receivable
                {
                    isDelete = 1,
                    Delete_time = DateTime.Now,
                    Delete_id = "E001"
                },
                a => a.id == "R1"
            );

            // Assert
            var after = await _recvRepo.GridAsync(a => a.isDelete == 0, 1, 30);
            Assert.Equal(0, after.count);

            var raw = await _fsql.Select<Finance_Receivable>().Where(a => a.id == "R1").FirstAsync();
            Assert.Equal(1, raw.isDelete);
        }

        // =========================================================
        // #10 Finance_ReceivableController.Grid 端点测试
        // =========================================================

        [Fact]
        public async Task Controller_Grid_WithReceivableNoFilter_ReturnsMatched()
        {
            await InsertReceivableAsync(NewReceivable("R1", "O1", 100));
            await InsertReceivableAsync(NewReceivable("R2", "O1", 200));

            var ctrl = FinanceReceivableControllerFactory.Create(
                _recvRepo, _receiveRepo, _orderRepo,
                queryString: "receivable_no=YS-R1");
            var json = await ctrl.Grid(new PageView<Finance_Receivable> { Page = 1, Limit = 30 });

            var data = JsonConvert.DeserializeObject<XHDData<Finance_Receivable>>(JObject.Parse(json).ToString());
            Assert.Equal(1, data.count);
            Assert.Single(data.data);
            Assert.Equal("R1", data.data[0].id);
        }

        [Fact]
        public async Task Controller_Grid_WithOrderIdFilter_ReturnsMatched()
        {
            await InsertReceivableAsync(NewReceivable("R1", "O1", 100));
            await InsertReceivableAsync(NewReceivable("R2", "O2", 200));

            var ctrl = FinanceReceivableControllerFactory.Create(
                _recvRepo, _receiveRepo, _orderRepo,
                queryString: "order_id=O1");
            var json = await ctrl.Grid(new PageView<Finance_Receivable> { Page = 1, Limit = 30 });

            var data = JsonConvert.DeserializeObject<XHDData<Finance_Receivable>>(JObject.Parse(json).ToString());
            Assert.Equal(1, data.count);
            Assert.Equal("O1", data.data[0].order_id);
        }

        [Fact]
        public async Task Controller_Grid_WithDateRangeFilter_ReturnsMatched()
        {
            var r1 = NewReceivable("R1", "O1", 100, receivableTime: new DateTime(2024, 3, 15));
            var r2 = NewReceivable("R2", "O1", 200, receivableTime: new DateTime(2024, 3, 20));
            var r3 = NewReceivable("R3", "O1", 300, receivableTime: new DateTime(2024, 4, 10));
            await InsertReceivableAsync(r1);
            await InsertReceivableAsync(r2);
            await InsertReceivableAsync(r3);

            var ctrl = FinanceReceivableControllerFactory.Create(
                _recvRepo, _receiveRepo, _orderRepo,
                queryString: "date1=2024-03-01&date2=2024-03-31");
            var json = await ctrl.Grid(new PageView<Finance_Receivable> { Page = 1, Limit = 30 });

            var data = JsonConvert.DeserializeObject<XHDData<Finance_Receivable>>(JObject.Parse(json).ToString());
            Assert.Equal(2, data.count);
            Assert.Contains(data.data, r => r.id == "R1");
            Assert.Contains(data.data, r => r.id == "R2");
            Assert.DoesNotContain(data.data, r => r.id == "R3");
        }

        [Fact]
        public async Task Controller_Grid_WithMinAmountFilter_ReturnsMatched()
        {
            await InsertReceivableAsync(NewReceivable("R1", "O1", 100));
            await InsertReceivableAsync(NewReceivable("R2", "O1", 500));
            await InsertReceivableAsync(NewReceivable("R3", "O1", 1000));

            var ctrl = FinanceReceivableControllerFactory.Create(
                _recvRepo, _receiveRepo, _orderRepo,
                queryString: "min_amount=500");
            var json = await ctrl.Grid(new PageView<Finance_Receivable> { Page = 1, Limit = 30 });

            var data = JsonConvert.DeserializeObject<XHDData<Finance_Receivable>>(JObject.Parse(json).ToString());
            Assert.Equal(2, data.count);
        }

        [Fact]
        public async Task Controller_Grid_EmptyDB_ReturnsEmpty()
        {
            var ctrl = FinanceReceivableControllerFactory.Create(
                _recvRepo, _receiveRepo, _orderRepo);
            var json = await ctrl.Grid(new PageView<Finance_Receivable> { Page = 1, Limit = 30 });

            var data = JsonConvert.DeserializeObject<XHDData<Finance_Receivable>>(JObject.Parse(json).ToString());
            Assert.Equal(0, data.count);
        }

        // =========================================================
        // #11 Finance_ReceiveRepository.UpdateReceiveAsync 三向联动
        // =========================================================

        [Fact]
        public async Task UpdateReceive_SingleReceive_UpdatesReceivableAndOrder()
        {
            // Arrange：订单 1000，应收 1000，1 张收款 300
            await InsertOrderAsync(NewOrder("O1", 1000m));
            await InsertReceivableAsync(NewReceivable("R1", "O1", 1000m));
            await InsertReceiveAsync(NewReceive("F1", "R1", "O1", 300m));

            // Act
            var ok = await _receiveRepo.UpdateReceiveAsync("R1", "O1");

            // Assert
            Assert.True(ok);
            var r1 = await _fsql.Select<Finance_Receivable>().Where(a => a.id == "R1").FirstAsync();
            Assert.Equal(300m, r1.received_amount);
            Assert.Equal(700m, r1.arrears_amount);

            var o1 = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(300m, o1.receive_money);
            Assert.Equal(700m, o1.arrears_money);
        }

        [Fact]
        public async Task UpdateReceive_MultipleReceive_SumAll()
        {
            // Arrange：订单 1000，应收 1000，2 张收款 300 + 500
            await InsertOrderAsync(NewOrder("O1", 1000m));
            await InsertReceivableAsync(NewReceivable("R1", "O1", 1000m));
            await InsertReceiveAsync(NewReceive("F1", "R1", "O1", 300m));
            await InsertReceiveAsync(NewReceive("F2", "R1", "O1", 500m));

            // Act
            var ok = await _receiveRepo.UpdateReceiveAsync("R1", "O1");

            // Assert
            Assert.True(ok);
            var r1 = await _fsql.Select<Finance_Receivable>().Where(a => a.id == "R1").FirstAsync();
            Assert.Equal(800m, r1.received_amount);
            Assert.Equal(200m, r1.arrears_amount);

            var o1 = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(800m, o1.receive_money);
            Assert.Equal(200m, o1.arrears_money);
        }

        [Fact]
        public async Task UpdateReceive_WithNonexistentReceivableId_ReturnsFalseAndRollsBack()
        {
            // Arrange：只有订单，无对应应收单
            await InsertOrderAsync(NewOrder("O1", 1000m));
            await InsertReceivableAsync(NewReceivable("R_EXIST", "O1", 1000m));  // 其他应收单，用于验证不被改
            var rBefore = await _fsql.Select<Finance_Receivable>().Where(a => a.id == "R_EXIST").FirstAsync();
            var receivedBefore = rBefore.received_amount;

            // Act
            var ok = await _receiveRepo.UpdateReceiveAsync("R_NOT_EXIST", "O1");

            // Assert
            Assert.False(ok);
            var rAfter = await _fsql.Select<Finance_Receivable>().Where(a => a.id == "R_EXIST").FirstAsync();
            Assert.Equal(receivedBefore, rAfter.received_amount);  // 事务回滚，其他数据不受影响
        }

        [Fact]
        public async Task UpdateReceive_WithNonexistentOrderId_ReturnsFalse()
        {
            // Arrange
            await InsertReceivableAsync(NewReceivable("R1", "O1", 1000m));
            await InsertReceiveAsync(NewReceive("F1", "R1", "O1", 300m));

            // Act
            var ok = await _receiveRepo.UpdateReceiveAsync("R1", "O_NOT_EXIST");

            // Assert：应收单更新后订单更新失败，返回 false
            Assert.False(ok);
        }

        [Fact]
        public async Task UpdateReceive_MultipleReceivablesInSameOrder_AggregateCorrectly()
        {
            // Arrange：订单 2000，含 2 张应收单 R1(1000)、R2(1000)
            // R1 收到 300；R2 收到 400；应收 1 更新
            await InsertOrderAsync(NewOrder("O1", 2000m));
            await InsertReceivableAsync(NewReceivable("R1", "O1", 1000m));
            await InsertReceivableAsync(NewReceivable("R2", "O1", 1000m));
            await InsertReceiveAsync(NewReceive("F1", "R1", "O1", 300m));
            await InsertReceiveAsync(NewReceive("F2", "R2", "O1", 400m));

            // Act：只对 R1 触发更新
            var ok = await _receiveRepo.UpdateReceiveAsync("R1", "O1");

            // Assert
            Assert.True(ok);
            var r1 = await _fsql.Select<Finance_Receivable>().Where(a => a.id == "R1").FirstAsync();
            var r2 = await _fsql.Select<Finance_Receivable>().Where(a => a.id == "R2").FirstAsync();
            Assert.Equal(300m, r1.received_amount);
            Assert.Equal(700m, r1.arrears_amount);

            // R2 保持不变
            Assert.Equal(0m, r2.received_amount);

            // 订单汇总：300 + 400 = 700（含其他应收单 R2 的收款 400，但 R2 未更新其 received_amount，
            // 实际上 Finance_Receive 关联的是 Receivable_id，R2 的收款 400 未反映到 R2.received_amount，
            // 因此订单实际应显示 300（R1 已收）+ 0（R2 未反映）= 300）
            // 但 Sale_orderRepository 的算法是 SUM(Finance_Receivable.received_amount)
            // 故订单实际 receive_money = R1.received(300) + R2.received(0) = 300
            var o1 = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(300m, o1.receive_money);
            Assert.Equal(1700m, o1.arrears_money);
        }

        [Fact]
        public async Task UpdateReceive_WithNullReceiveableAmount_NegativeArrearsAllowed()
        {
            // Arrange：应收单金额为 0，收款 500 → 应收 500，未收 -500
            await InsertOrderAsync(NewOrder("O1", 100m));
            await InsertReceivableAsync(NewReceivable("R1", "O1", 0m));
            await InsertReceiveAsync(NewReceive("F1", "R1", "O1", 500m));

            // Act
            var ok = await _receiveRepo.UpdateReceiveAsync("R1", "O1");

            // Assert
            Assert.True(ok);
            var r1 = await _fsql.Select<Finance_Receivable>().Where(a => a.id == "R1").FirstAsync();
            Assert.Equal(500m, r1.received_amount);
            Assert.Equal(-500m, r1.arrears_amount);
        }

        [Fact]
        public async Task UpdateReceive_NoReceiveRecords_ReceivableReceivedRemainsZero()
        {
            // Arrange：无收款记录
            await InsertOrderAsync(NewOrder("O1", 1000m));
            await InsertReceivableAsync(NewReceivable("R1", "O1", 1000m));

            // Act
            var ok = await _receiveRepo.UpdateReceiveAsync("R1", "O1");

            // Assert
            Assert.True(ok);
            var r1 = await _fsql.Select<Finance_Receivable>().Where(a => a.id == "R1").FirstAsync();
            Assert.Equal(0m, r1.received_amount);
            Assert.Equal(1000m, r1.arrears_amount);

            var o1 = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(0m, o1.receive_money);
            Assert.Equal(1000m, o1.arrears_money);
        }

        [Fact]
        public async Task UpdateReceive_WithEmptyReceivableId_ReturnsFalse()
        {
            var ok = await _receiveRepo.UpdateReceiveAsync("", "O1");
            Assert.False(ok);
        }

        [Fact]
        public async Task UpdateReceive_WithWhitespaceOrderId_ReturnsFalse()
        {
            await InsertReceivableAsync(NewReceivable("R1", "O1", 100m));
            var ok = await _receiveRepo.UpdateReceiveAsync("R1", "   ");
            Assert.False(ok);
        }

        // =========================================================
        // #12 Sale_orderRepository.UpdateReceiveAsync 订单重算
        // =========================================================

        [Fact]
        public async Task Order_UpdateReceive_SingleReceivable_CalculatesCorrectly()
        {
            // Arrange
            await InsertOrderAsync(NewOrder("O1", 1000m));
            await InsertReceivableAsync(NewReceivable("R1", "O1", 1000m, receivedAmount: 500m));

            // Act
            var ok = await _orderRepo.UpdateReceiveAsync("O1");

            // Assert
            Assert.True(ok);
            var o1 = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(500m, o1.receive_money);
            Assert.Equal(500m, o1.arrears_money);
        }

        [Fact]
        public async Task Order_UpdateReceive_MultipleReceivables_AggregatesSum()
        {
            await InsertOrderAsync(NewOrder("O1", 1000m));
            await InsertReceivableAsync(NewReceivable("R1", "O1", 500m, receivedAmount: 300m));
            await InsertReceivableAsync(NewReceivable("R2", "O1", 500m, receivedAmount: 400m));

            var ok = await _orderRepo.UpdateReceiveAsync("O1");

            Assert.True(ok);
            var o1 = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(700m, o1.receive_money);
            Assert.Equal(300m, o1.arrears_money);
        }

        [Fact]
        public async Task Order_UpdateReceive_WithNonexistentOrder_ReturnsFalse()
        {
            var ok = await _orderRepo.UpdateReceiveAsync("O_NOT_EXIST");
            Assert.False(ok);
        }

        [Fact]
        public async Task Order_UpdateReceive_WithEmptyOrderId_ReturnsFalse()
        {
            var ok = await _orderRepo.UpdateReceiveAsync("");
            Assert.False(ok);
        }

        [Fact]
        public async Task Order_UpdateReceive_WithNullOrderAmount_TreatsAsZero()
        {
            // Arrange：Order_amount 为 null
            var order = NewOrder("O1", 1000m);
            order.Order_amount = null;
            await InsertOrderAsync(order);
            await InsertReceivableAsync(NewReceivable("R1", "O1", 500m, receivedAmount: 300m));

            // Act
            var ok = await _orderRepo.UpdateReceiveAsync("O1");

            // Assert
            Assert.True(ok);
            var o1 = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(300m, o1.receive_money);
            Assert.Equal(-300m, o1.arrears_money);  // 0 - 300
        }

        [Fact]
        public async Task Order_UpdateReceive_NoReceivables_ReceivedMoneyBecomesZero()
        {
            // Arrange：订单无应收单，但订单本身有已收金额（旧值）
            var order = NewOrder("O1", 1000m);
            order.receive_money = 500m;
            await InsertOrderAsync(order);

            // Act
            var ok = await _orderRepo.UpdateReceiveAsync("O1");

            // Assert
            Assert.True(ok);
            var o1 = await _fsql.Select<Sale_order>().Where(a => a.id == "O1").FirstAsync();
            Assert.Equal(0m, o1.receive_money);
            Assert.Equal(1000m, o1.arrears_money);
        }
    }

    /// <summary>
    /// 测试用 Finance_ReceivableController 构造助手：
    /// 用 Moq 把 IFinance_ReceivableService 桥接到真实 Repository，
    /// 组装带 HttpContext 的 Controller 实例。
    /// </summary>
    internal static class FinanceReceivableControllerFactory
    {
        public static Finance_ReceivableController Create(
            Finance_ReceivableRepository recvRepo,
            Finance_ReceiveRepository receiveRepo,
            Sale_orderRepository orderRepo,
            string queryString = "")
        {
            // 装配 IFinance_ReceivableService（真实 Repository 桥接）
            var svcMock = new Mock<IFinance_ReceivableService>();
            svcMock.Setup(s => s.GridAsync(
                    It.IsAny<Expression<Func<Finance_Receivable, bool>>>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<Finance_Receivable, bool>> e, int p, int l, string o)
                    => recvRepo.GridAsync(e, p, l, o));
            svcMock.Setup(s => s.GridAsync(
                    It.IsAny<Expression<Func<Finance_Receivable, bool>>>(),
                    It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Finance_Receivable, bool>> e, int p, int l)
                    => recvRepo.GridAsync(e, p, l));
            svcMock.Setup(s => s.UpdateReceiveAsync(It.IsAny<string>()))
                .Returns((string id) => orderRepo.UpdateReceiveAsync(id));

            var authMock = new Mock<IDBAuthService>();
            authMock.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 4, empList = new List<string>() });
            var logMock = new Mock<ISys_logService>();

            // 构造 Controller
            var ctrl = new Finance_ReceivableController(
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<Finance_ReceivableController>(),
                svcMock.Object,
                logMock.Object,
                authMock.Object);

            // 组装 HttpContext
            var httpCtx = new DefaultHttpContext();
            if (!string.IsNullOrEmpty(queryString))
            {
                var qs = queryString.StartsWith("?") ? queryString.Substring(1) : queryString;
                httpCtx.Request.QueryString = new QueryString($"?{qs}");
            }
            httpCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, "TEST_USER"),
                new Claim(ClaimTypes.Name, "Test User")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return ctrl;
        }
    }
}
