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
    /// Phase 3 W4：销售合同 SaleContractController 真测试。
    /// 覆盖 Grid（含权限/过滤）、Save（新增/更新/权限闸门/数据归属）、
    /// Delete（存在/不存在/权限/数据归属/附件级联）。
    /// 走真实 SQLite 内存库 + 真实 Sale_contractService / Sale_contract_attaService。
    /// </summary>
    public class SaleContractSaveTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sale_contractRepository _contractRepo;
        private readonly Sale_contract_attaRepository _attaRepo;

        private const string UserId = "TEST_USER";
        private const string OtherUser = "OTHER_USER";

        public SaleContractSaveTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _contractRepo = new Sale_contractRepository(_fsql);
            _attaRepo = new Sale_contract_attaRepository(_fsql);
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

        private static Sale_contract NewContract(
            string id, string customerId, string name, decimal amount,
            DateTime signDate, string createId)
        {
            return new Sale_contract
            {
                id = id,
                customer_id = customerId,
                Contract_name = name,
                Contract_amount = amount,
                Sign_date = signDate,
                Start_date = signDate,
                End_date = signDate.AddYears(1),
                sn = $"HT-{id}",
                Our_Contractor_id = "E_SALE",
                create_id = createId,
                create_time = signDate,
                isDelete = 0
            };
        }

        private async Task InsertCustomerAsync(CRM_Customer c) => await _fsql.Insert(c).ExecuteAffrowsAsync();
        private async Task InsertContractAsync(Sale_contract c) => await _fsql.Insert(c).ExecuteAffrowsAsync();

        // ============ Controller 装配 ============

        private static Mock<IDBAuthService> CreateAuthMock(int authtype, List<string> empList, bool grantButtons = true)
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = authtype, empList = empList ?? new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(grantButtons);
            return auth;
        }

        private SaleContractController CreateController(
            string queryString = "",
            Mock<IDBAuthService> authMock = null,
            Mock<ISys_logService> logMock = null,
            string userId = UserId)
        {
            authMock ??= CreateAuthMock(4, new List<string>());
            logMock ??= new Mock<ISys_logService>();
            logMock.Setup(l => l.UpdateLog(It.IsAny<Sys_log>())).ReturnsAsync(1);
            logMock.Setup(l => l.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);

            return TestControllerHelper.CreateWithHttpContext<SaleContractController>(
                queryString, userId, "Test User",
                new Mock<ILogger<SaleContractController>>().Object,
                new Sale_contractService(_contractRepo),
                new Sale_contract_attaService(_attaRepo),
                logMock.Object,
                authMock.Object);
        }

        // ============ Grid ============

        [Fact]
        public async Task Grid_EmptyTable_ReturnsEmpty()
        {
            var ctrl = CreateController();

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_contract>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task Grid_WithData_ReturnsAll()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertContractAsync(NewContract("H1", "C1", "合同甲", 1000m, new DateTime(2024, 3, 1), UserId));
            await InsertContractAsync(NewContract("H2", "C1", "合同乙", 2000m, new DateTime(2024, 5, 1), UserId));

            var ctrl = CreateController();

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_contract>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var ids = ((JArray)obj["data"]!).Select(d => (string)d["id"]!).ToList();
            Assert.Contains("H1", ids);
            Assert.Contains("H2", ids);
        }

        [Fact]
        public async Task Grid_FilterByCustomerName_ReturnsMatched()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertCustomerAsync(NewCustomer("C2", "E_A"));
            await InsertContractAsync(NewContract("H1", "C1", "合同甲", 1000m, new DateTime(2024, 3, 1), UserId));
            await InsertContractAsync(NewContract("H2", "C2", "合同乙", 2000m, new DateTime(2024, 3, 1), UserId));

            var ctrl = CreateController(queryString: "?cus_name=客户-C1");

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_contract>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal("H1", (string)((JArray)obj["data"]!)[0]["id"]!);
        }

        [Fact]
        public async Task Grid_NonFullAuth_FiltersByCustomerEmpId()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertCustomerAsync(NewCustomer("C2", "E_B"));
            await InsertContractAsync(NewContract("H1", "C1", "合同甲", 1000m, new DateTime(2024, 3, 1), UserId));
            await InsertContractAsync(NewContract("H2", "C2", "合同乙", 2000m, new DateTime(2024, 3, 1), UserId));

            // 只能见客户归属员工 E_A 的合同
            var ctrl = CreateController(authMock: CreateAuthMock(1, new List<string> { "E_A" }));

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Sale_contract>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal("H1", (string)((JArray)obj["data"]!)[0]["id"]!);
        }

        // ============ Save（新增） ============

        [Fact]
        public async Task Save_New_ContractPersisted()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));

            var ctrl = CreateController();
            var model = NewContract(string.Empty, "C1", "新合同", 5000m, new DateTime(2024, 6, 1), string.Empty);

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var newId = (string)obj["msg"]!;
            Assert.False(string.IsNullOrEmpty(newId));

            var fetched = await _fsql.Select<Sale_contract>().Where(a => a.id == newId).FirstAsync();
            Assert.NotNull(fetched);
            Assert.Equal("新合同", fetched.Contract_name);
            Assert.Equal(5000m, fetched.Contract_amount);
            Assert.Equal(UserId, fetched.create_id);
        }

        [Fact]
        public async Task Save_New_NoAddPermission_ReturnsError()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));

            var ctrl = CreateController(authMock: CreateAuthMock(4, new List<string>(), grantButtons: false));
            var model = NewContract(string.Empty, "C1", "新合同", 5000m, new DateTime(2024, 6, 1), string.Empty);

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无权限！", (string)obj["msg"]!);

            // 未落库
            Assert.Empty(await _contractRepo.GridAsync(a => a.Contract_name == "新合同"));
        }

        // ============ Save（更新） ============

        [Fact]
        public async Task Save_Update_ExistingContract_Updated()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertContractAsync(NewContract("H1", "C1", "旧合同", 1000m, new DateTime(2024, 3, 1), UserId));

            var ctrl = CreateController();
            var model = NewContract("H1", "C1", "新名称", 3000m, new DateTime(2024, 7, 1), "FORGED");

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<Sale_contract>().Where(a => a.id == "H1").FirstAsync();
            Assert.Equal("新名称", fetched.Contract_name);
            Assert.Equal(3000m, fetched.Contract_amount);
            // create_id 被 IgnoreColumns 保护，伪造值不生效
            Assert.Equal(UserId, fetched.create_id);
        }

        [Fact]
        public async Task Save_Update_NotFound_ReturnsError()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));

            var ctrl = CreateController();
            var model = NewContract("NOT_EXIST", "C1", "新名称", 3000m, new DateTime(2024, 7, 1), UserId);

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("找不到数据！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Save_Update_NoEditPermission_ReturnsError()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertContractAsync(NewContract("H1", "C1", "旧合同", 1000m, new DateTime(2024, 3, 1), UserId));

            var ctrl = CreateController(authMock: CreateAuthMock(4, new List<string>(), grantButtons: false));
            var model = NewContract("H1", "C1", "新名称", 3000m, new DateTime(2024, 7, 1), UserId);

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无权限！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Save_Update_OutOfScope_ReturnsError()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertContractAsync(NewContract("H1", "C1", "旧合同", 1000m, new DateTime(2024, 3, 1), OtherUser));

            var ctrl = CreateController(authMock: CreateAuthMock(1, new List<string> { UserId }));
            var model = NewContract("H1", "C1", "新名称", 3000m, new DateTime(2024, 7, 1), UserId);

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无权限！", (string)obj["msg"]!);
        }

        // ============ Delete ============

        [Fact]
        public async Task Delete_Existing_ContractAndAttasRemoved()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertContractAsync(NewContract("H1", "C1", "合同甲", 1000m, new DateTime(2024, 3, 1), UserId));
            await _fsql.Insert(new Sale_contract_atta
            {
                id = Guid.NewGuid().ToString(),
                contract_id = "H1",
                file_name = "a.pdf",
                real_name = "a.pdf",
                file_size = 100,
                create_id = UserId,
                create_time = new DateTime(2024, 3, 1)
            }).ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Delete("H1");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            Assert.Null(await _fsql.Select<Sale_contract>().Where(a => a.id == "H1").FirstAsync());
            Assert.Empty(await _fsql.Select<Sale_contract_atta>().Where(a => a.contract_id == "H1").ToListAsync());
        }

        [Fact]
        public async Task Delete_NotFound_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Delete("NOT_EXIST");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("找不到此数据！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Delete_NoPermission_ReturnsError()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertContractAsync(NewContract("H1", "C1", "合同甲", 1000m, new DateTime(2024, 3, 1), UserId));

            var ctrl = CreateController(authMock: CreateAuthMock(4, new List<string>(), grantButtons: false));

            var json = await ctrl.Delete("H1");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无权限！", (string)obj["msg"]!);
            Assert.NotNull(await _fsql.Select<Sale_contract>().Where(a => a.id == "H1").FirstAsync());
        }

        [Fact]
        public async Task Delete_OutOfScope_ReturnsError()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_A"));
            await InsertContractAsync(NewContract("H1", "C1", "合同甲", 1000m, new DateTime(2024, 3, 1), OtherUser));

            var ctrl = CreateController(authMock: CreateAuthMock(1, new List<string> { UserId }));

            var json = await ctrl.Delete("H1");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无权限！", (string)obj["msg"]!);
            Assert.NotNull(await _fsql.Select<Sale_contract>().Where(a => a.id == "H1").FirstAsync());
        }
    }
}
