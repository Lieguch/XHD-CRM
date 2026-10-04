using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
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
    /// Phase 3 W4：客户批量转移 CustomerBatchController + 任务 JobsController 真测试。
    /// 覆盖 CustomerBatch 的 Grid（权限范围过滤）/ Save（真实转移客户 + 批次落库 + 逐条日志），
    /// 以及 Jobs 的 Grid（仅本人）/ Save（新增/更新）/ Delete（存在/不存在）。
    /// 走真实 SQLite 内存库 + 真实仓储/服务，IFreeSql 直传测试库实例（不 Mock）。
    /// </summary>
    public class CustomerBatchJobsTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly CRM_Customer_BathRepository _bathRepo;
        private readonly CRM_CustomerRepository _custRepo;
        private readonly JobsRepository _jobsRepo;

        private const string UserId = "TEST_USER";
        private const string OtherUser = "OTHER_USER";

        public CustomerBatchJobsTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _bathRepo = new CRM_Customer_BathRepository(_fsql);
            _custRepo = new CRM_CustomerRepository(_fsql);
            _jobsRepo = new JobsRepository(_fsql);
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

        private static CRM_Customer_Bath NewBatch(string id, string oldEmp, string newEmp, DateTime createTime)
        {
            return new CRM_Customer_Bath
            {
                id = id,
                old_emp_id = oldEmp,
                new_emp_id = newEmp,
                cus_count = 1,
                create_id = UserId,
                create_time = createTime,
                Remarks = "批量转移"
            };
        }

        private static Jobs NewJob(string id, string title, string createId, DateTime createTime)
        {
            return new Jobs
            {
                id = id,
                job_title = title,
                job_content = "回访客户",
                create_id = createId,
                create_time = createTime,
                executive_time = createTime.AddDays(1),
                priority = 1,
                status = 0,
                customer_id = "C1"
            };
        }

        private async Task InsertCustomerAsync(CRM_Customer c) => await _fsql.Insert(c).ExecuteAffrowsAsync();
        private async Task InsertBatchAsync(CRM_Customer_Bath b) => await _fsql.Insert(b).ExecuteAffrowsAsync();
        private async Task InsertJobAsync(Jobs j) => await _fsql.Insert(j).ExecuteAffrowsAsync();

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

        private CustomerBatchController CreateBatchController(
            string queryString = "",
            Mock<IDBAuthService> authMock = null,
            Mock<ISys_logService> logMock = null)
        {
            authMock ??= CreateAuthMock(4, new List<string>());
            logMock ??= CreateLogMock();

            // 构造顺序：service, dBAuthService, paramService, provincesService,
            // customerService, fsql, logService
            return TestControllerHelper.CreateWithHttpContext<CustomerBatchController>(
                queryString, UserId, "Test User",
                new CRM_Customer_BathService(_bathRepo),
                authMock.Object,
                new Sys_ParamService(new Sys_ParamRepository(_fsql)),
                new Sys_Param_ProvincesService(new Sys_Param_ProvincesRepository(_fsql)),
                new CRM_CustomerService(_custRepo),
                _fsql,
                logMock.Object);
        }

        private static void SetForm(CustomerBatchController ctrl, string customerIds)
        {
            var dict = new Dictionary<string, StringValues>();
            if (!string.IsNullOrEmpty(customerIds))
            {
                dict["customer_ids"] = customerIds;
            }
            ctrl.HttpContext.Request.Form = new FormCollection(dict);
        }

        private JobsController CreateJobsController(string queryString = "")
        {
            return TestControllerHelper.CreateWithHttpContext<JobsController>(
                queryString, UserId, "Test User",
                new JobsService(_jobsRepo));
        }

        // =========================================================
        // CustomerBatchController.Grid
        // =========================================================

        [Fact]
        public async Task Batch_Grid_EmptyTable_ReturnsEmpty()
        {
            var ctrl = CreateBatchController();

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<CRM_Customer_Bath>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task Batch_Grid_NonFullAuth_FiltersByOldOrNewEmpId()
        {
            await InsertBatchAsync(NewBatch("B1", "E_A", "E_B", new DateTime(2024, 6, 1)));
            await InsertBatchAsync(NewBatch("B2", "E_C", "E_D", new DateTime(2024, 6, 2)));

            // 只能见 old_emp_id 或 new_emp_id 在可见员工范围内的批次
            var ctrl = CreateBatchController(authMock: CreateAuthMock(1, new List<string> { "E_A" }));

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<CRM_Customer_Bath>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal("B1", (string)((JArray)obj["data"]!)[0]["id"]!);
        }

        // =========================================================
        // CustomerBatchController.Save
        // =========================================================

        [Fact]
        public async Task Batch_Save_TransfersCustomersAndPersistsBatch()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_OLD"));
            await InsertCustomerAsync(NewCustomer("C2", "E_OLD"));

            var logMock = CreateLogMock();
            var ctrl = CreateBatchController(logMock: logMock);
            SetForm(ctrl, "C1,C2");

            var model = new CRM_Customer_Bath
            {
                old_emp_id = "E_OLD",
                new_emp_id = "E_NEW",
                Remarks = "部门调整"
            };

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            // 客户归属真实转移到新员工
            var c1 = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").FirstAsync();
            var c2 = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C2").FirstAsync();
            Assert.Equal("E_NEW", c1.emp_id);
            Assert.Equal("E_NEW", c2.emp_id);

            // 批次记录落库，转出数量 = 2
            var batches = await _bathRepo.GridAsync(a => true);
            Assert.Single(batches);
            Assert.Equal(2, batches[0].cus_count);
            Assert.Equal("E_NEW", batches[0].new_emp_id);
            Assert.Equal(UserId, batches[0].create_id);

            // 每个被转移客户写一条日志
            logMock.Verify(l => l.UpdateLog(It.Is<Sys_log>(
                x => x.EventType == "[客户]批量转移" && x.cus_id == "C1")), Times.Once);
            logMock.Verify(l => l.UpdateLog(It.Is<Sys_log>(
                x => x.EventType == "[客户]批量转移" && x.cus_id == "C2")), Times.Once);
        }

        [Fact]
        public async Task Batch_Save_NoCustomerIds_CreatesBatchWithZeroCount()
        {
            await InsertCustomerAsync(NewCustomer("C1", "E_OLD"));

            var ctrl = CreateBatchController();
            SetForm(ctrl, string.Empty);

            var model = new CRM_Customer_Bath
            {
                old_emp_id = "E_OLD",
                new_emp_id = "E_NEW"
            };

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            // 未传 customer_ids：客户不动，批次 cus_count = 0
            var c1 = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").FirstAsync();
            Assert.Equal("E_OLD", c1.emp_id);

            var batches = await _bathRepo.GridAsync(a => true);
            Assert.Single(batches);
            Assert.Equal(0, batches[0].cus_count);
        }

        // =========================================================
        // JobsController.Grid
        // =========================================================

        [Fact]
        public async Task Jobs_Grid_ReturnsOnlyOwnJobs()
        {
            await InsertJobAsync(NewJob("J1", "我的任务A", UserId, new DateTime(2024, 6, 1)));
            await InsertJobAsync(NewJob("J2", "我的任务B", UserId, new DateTime(2024, 6, 2)));
            await InsertJobAsync(NewJob("J3", "他人任务", OtherUser, new DateTime(2024, 6, 3)));

            var ctrl = CreateJobsController();

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Jobs>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var ids = ((JArray)obj["data"]!).Select(d => (string)d["id"]!).ToList();
            Assert.Contains("J1", ids);
            Assert.Contains("J2", ids);
            Assert.DoesNotContain("J3", ids);
        }

        // =========================================================
        // JobsController.Save
        // =========================================================

        [Fact]
        public async Task Jobs_Save_New_PersistedWithCurrentUser()
        {
            var ctrl = CreateJobsController();
            var model = NewJob(string.Empty, "新任务", string.Empty, default);

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal("新增成功！", (string)obj["msg"]!);

            var all = await _jobsRepo.GridAsync(a => true);
            Assert.Single(all);
            Assert.Equal("新任务", all[0].job_title);
            Assert.Equal(UserId, all[0].create_id);
            Assert.NotNull(all[0].create_time);
        }

        [Fact]
        public async Task Jobs_Save_Update_ExistingJob_Updated()
        {
            await InsertJobAsync(NewJob("J1", "旧任务", UserId, new DateTime(2024, 6, 1)));

            var ctrl = CreateJobsController();
            var model = NewJob("J1", "新任务名", UserId, new DateTime(2024, 6, 1));
            model.job_content = "更新后的内容";

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<Jobs>().Where(a => a.id == "J1").FirstAsync();
            Assert.Equal("新任务名", fetched.job_title);
            Assert.Equal("更新后的内容", fetched.job_content);
        }

        [Fact]
        public async Task Jobs_Save_Update_NotFound_ReturnsError()
        {
            var ctrl = CreateJobsController();
            var model = NewJob("NOT_EXIST", "新任务名", UserId, new DateTime(2024, 6, 1));

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("找不到数据！", (string)obj["msg"]!);
        }

        // =========================================================
        // JobsController.Delete
        // =========================================================

        [Fact]
        public async Task Jobs_Delete_Existing_Removed()
        {
            await InsertJobAsync(NewJob("J1", "任务A", UserId, new DateTime(2024, 6, 1)));

            var ctrl = CreateJobsController();

            var json = await ctrl.Delete("J1");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Null(await _fsql.Select<Jobs>().Where(a => a.id == "J1").FirstAsync());
        }

        [Fact]
        public async Task Jobs_Delete_NotFound_ReturnsError()
        {
            var ctrl = CreateJobsController();

            var json = await ctrl.Delete("NOT_EXIST");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("找不到此数据！", (string)obj["msg"]!);
        }
    }
}
