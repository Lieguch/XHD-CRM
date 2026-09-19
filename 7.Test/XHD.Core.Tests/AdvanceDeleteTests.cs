using FreeSql;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
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
    /// Sprint 3 Wave 2 单元测试：
    ///   #13 CRM_CustomerRepository.AdvanceDeleteAsync（Repository 软删）
    ///   #14 CustomerController.AdvanceDelete（预删除端点）
    /// </summary>
    public class AdvanceDeleteTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly CRM_CustomerRepository _custRepo;

        public AdvanceDeleteTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _custRepo = new CRM_CustomerRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static CRM_Customer NewCustomer(string id, string empId = "E1", int isDelete = 0)
        {
            return new CRM_Customer
            {
                id = id,
                cus_name = $"客户-{id}",
                emp_id = empId,
                create_id = empId,
                create_time = new DateTime(2024, 6, 15),
                isDelete = isDelete,
                isPrivate = 1,
                sn = $"CU-{id}"
            };
        }

        private async Task InsertCustomerAsync(CRM_Customer c)
            => await _fsql.Insert(c).ExecuteAffrowsAsync();

        // ============ #13 Repository：AdvanceDeleteAsync ============

        [Fact]
        public async Task AdvanceDeleteRepository_ExistingCustomer_SetsDeleteFieldsAndReturnsTrue()
        {
            // Arrange
            await InsertCustomerAsync(NewCustomer("C1"));

            // Act
            DateTime before = DateTime.Now.AddSeconds(-1);
            var ok = await _custRepo.AdvanceDeleteAsync("C1", "OP1");

            // Assert
            Assert.True(ok);

            // 重新读取验证：isDelete=1、Delete_time 已写入、Delete_id 已写入
            var rows = await _fsql.Select<CRM_Customer>()
                .Where(a => a.id == "C1")
                .ToListAsync();
            Assert.Single(rows);
            var c = rows[0];
            Assert.Equal(1, c.isDelete);
            Assert.NotNull(c.Delete_time);
            Assert.True(c.Delete_time.Value >= before, $"Delete_time {c.Delete_time} < {before}");
            Assert.Equal("OP1", c.Delete_id);
        }

        [Fact]
        public async Task AdvanceDeleteRepository_NonExistentCustomer_ReturnsFalse()
        {
            // Arrange：空库

            // Act
            var ok = await _custRepo.AdvanceDeleteAsync("NOT_EXIST", "OP1");

            // Assert
            Assert.False(ok);
        }

        [Fact]
        public async Task AdvanceDeleteRepository_AlreadyDeletedCustomer_ReturnsTrueIdempotent()
        {
            // Arrange：已软删记录再次调用应仍返回 true（幂等）
            await InsertCustomerAsync(NewCustomer("C1", isDelete: 1));

            // Act
            var ok1 = await _custRepo.AdvanceDeleteAsync("C1", "OP1");
            var ok2 = await _custRepo.AdvanceDeleteAsync("C1", "OP2");

            // Assert
            Assert.True(ok1);
            Assert.True(ok2);

            // 最终状态：isDelete=1，Delete_id 被最后一次调用覆盖
            var rows = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").ToListAsync();
            Assert.Single(rows);
            Assert.Equal(1, rows[0].isDelete);
            Assert.Equal("OP2", rows[0].Delete_id);
        }

        [Fact]
        public async Task AdvanceDeleteRepository_EmptyId_ReturnsFalseWithoutError()
        {
            // Arrange：传空 ID 直接返回 false（防御性检查）

            // Act
            var ok = await _custRepo.AdvanceDeleteAsync("", "OP1");

            // Assert
            Assert.False(ok);
        }

        // ============ #14 Controller：AdvanceDelete ============

        /// <summary>
        /// 构造 CustomerController 并装配 AdvanceDelete 所需的全部依赖：
        ///   ICRM_CustomerService 桥接到真实 Repository；
        ///   Contact/Follow/Order 的 GridAsync 桥接到真实 FreeSql 查询（用 count 表达数量）；
        ///   IDBAuthService 由外部注入；
        ///   ISys_logService 由外部注入（可捕获写入）。
        /// </summary>
        private CustomerController CreateAdvanceDeleteController(
            Mock<IDBAuthService> authMock,
            Mock<ISys_logService> logMock,
            string userId = "TEST_USER")
        {
            // --- 桥接 ICRM_CustomerService 到真实 Repository ---
            var customerSvc = new Mock<ICRM_CustomerService>();
            customerSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer, bool>>>(),
                                              It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<CRM_Customer, bool>> e, int p, int l)
                    => _custRepo.GridAsync(e, p, l));
            customerSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer, bool>>>(),
                                              It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<CRM_Customer, bool>> e, int p, int l, string o)
                    => _custRepo.GridAsync(e, p, l, o));
            customerSvc.Setup(s => s.AdvanceDeleteAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string id, string op) => _custRepo.AdvanceDeleteAsync(id, op));

            // --- 桥接 Contact/Follow/Order 到真实 FreeSql（返回 XHDData 携带 count）---
            var contactSvc = new Mock<ICRM_ContactService>();
            contactSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Contact, bool>>>(),
                                              It.IsAny<int>(), It.IsAny<int>()))
                .Returns(async (Expression<Func<CRM_Contact, bool>> e, int p, int l) =>
                {
                    long cnt = await _fsql.Select<CRM_Contact>().Where(e).CountAsync();
                    return new XHDData<CRM_Contact> { count = cnt, data = new List<CRM_Contact>() };
                });

            var followSvc = new Mock<ICRM_followService>();
            followSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_follow, bool>>>(),
                                             It.IsAny<int>(), It.IsAny<int>()))
                .Returns(async (Expression<Func<CRM_follow, bool>> e, int p, int l) =>
                {
                    long cnt = await _fsql.Select<CRM_follow>().Where(e).CountAsync();
                    return new XHDData<CRM_follow> { count = cnt, data = new List<CRM_follow>() };
                });

            var orderSvc = new Mock<ISale_orderService>();
            orderSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sale_order, bool>>>(),
                                            It.IsAny<int>(), It.IsAny<int>()))
                .Returns(async (Expression<Func<Sale_order, bool>> e, int p, int l) =>
                {
                    long cnt = await _fsql.Select<Sale_order>().Where(e).CountAsync();
                    return new XHDData<Sale_order> { count = cnt, data = new List<Sale_order>() };
                });

            var ctrl = new CustomerController(
                customerSvc.Object,
                contactSvc.Object,
                followSvc.Object,
                orderSvc.Object,
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
        /// 全公司权限（authtype=4）+ GetAuth 通过
        /// </summary>
        private Mock<IDBAuthService> CreateFullAccessAuth(bool grantDelButton = true)
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 4, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.Is<string>(x => x == "CRM_Customer|del")))
                .ReturnsAsync(grantDelButton);
            return auth;
        }

        /// <summary>
        /// 无权限（authtype=0）
        /// </summary>
        private Mock<IDBAuthService> CreateNoAuth()
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 0, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            return auth;
        }

        [Fact]
        public async Task AdvanceDeleteController_NonExistentCustomer_ReturnsNotFoundError()
        {
            // Arrange：空库
            var logMock = new Mock<ISys_logService>();

            var ctrl = CreateAdvanceDeleteController(CreateFullAccessAuth(), logMock);

            // Act
            var json = await ctrl.AdvanceDelete("NOT_EXIST");
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("系统错误", (string)obj["msg"]!);
        }

        [Fact]
        public async Task AdvanceDeleteController_NoPermission_ReturnsPermissionDenied()
        {
            // Arrange
            await InsertCustomerAsync(NewCustomer("C1"));
            var logMock = new Mock<ISys_logService>();

            var ctrl = CreateAdvanceDeleteController(CreateNoAuth(), logMock);

            // Act
            var json = await ctrl.AdvanceDelete("C1");
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限", (string)obj["msg"]!);

            // 关键：无权限不应产生 Sys_log 写入
            logMock.Verify(l => l.DeleteLog(It.IsAny<Sys_log>()), Times.Never);
        }

        [Fact]
        public async Task AdvanceDeleteController_NoRelatedData_ReturnsRecycledMessage()
        {
            // Arrange：客户存在、无任何关联数据
            await InsertCustomerAsync(NewCustomer("C1"));
            var logMock = new Mock<ISys_logService>();
            logMock.Setup(l => l.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);

            var ctrl = CreateAdvanceDeleteController(CreateFullAccessAuth(), logMock);

            // Act
            var json = await ctrl.AdvanceDelete("C1");
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Contains("此客户已放入回收站", (string)obj["msg"]!);
            Assert.DoesNotContain("联系人", (string)obj["msg"]!);

            // 关键：Sys_log 已写入且 cus_id 已填充
            logMock.Verify(l => l.DeleteLog(It.Is<Sys_log>(x =>
                x.cus_id == "C1" && x.EventType == "[客户]预删除")), Times.Once);

            // 关键：客户已被软删
            var rows = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").ToListAsync();
            Assert.Single(rows);
            Assert.Equal(1, rows[0].isDelete);
        }

        [Fact]
        public async Task AdvanceDeleteController_WithRelatedData_ReturnsCountMessage()
        {
            // Arrange：客户下挂 2 联系人、1 跟进、3 订单
            await InsertCustomerAsync(NewCustomer("C1"));
            await _fsql.Insert(new CRM_Contact { id = "CT1", customer_id = "C1" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new CRM_Contact { id = "CT2", customer_id = "C1" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new CRM_follow { id = "FL1", customer_id = "C1" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sale_order { id = "O1", customer_id = "C1" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sale_order { id = "O2", customer_id = "C1" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sale_order { id = "O3", customer_id = "C1" }).ExecuteAffrowsAsync();

            var logMock = new Mock<ISys_logService>();
            logMock.Setup(l => l.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);

            var ctrl = CreateAdvanceDeleteController(CreateFullAccessAuth(), logMock);

            // Act
            var json = await ctrl.AdvanceDelete("C1");
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(0, (int)obj["code"]!);
            var msg = (string)obj["msg"]!;
            Assert.Contains("此客户已放入回收站", msg);
            Assert.Contains("2 个联系人", msg);
            Assert.Contains("1 条跟进", msg);
            Assert.Contains("3 个订单", msg);
            Assert.Contains("6 项关联数据", msg);

            // 客户已被软删
            var rows = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").ToListAsync();
            Assert.Single(rows);
            Assert.Equal(1, rows[0].isDelete);
        }

        [Fact]
        public async Task AdvanceDeleteController_EmptyId_ReturnsNotFoundError()
        {
            // Arrange
            var logMock = new Mock<ISys_logService>();

            var ctrl = CreateAdvanceDeleteController(CreateFullAccessAuth(), logMock);

            // Act
            var json = await ctrl.AdvanceDelete("");
            var obj = JObject.Parse(json);

            // Assert
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("系统错误", (string)obj["msg"]!);
        }
    }
}
