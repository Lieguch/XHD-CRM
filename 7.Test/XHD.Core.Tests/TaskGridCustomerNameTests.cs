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
using Newtonsoft.Json.Linq;

using Xunit;

using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.View.Controllers;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.42 单元测试：TaskController.Grid 的 customer_name 批量回填（P1-17）。
    /// 对应 A 版任务列表/表单显示客户名而非 customer_id（GUID）。
    /// 装配风格对齐 TaskRemindTests：真实 Repository + SQLite in-memory，
    /// Task/客户 Service 用 Mock 透传到真实仓储。
    /// </summary>
    public class TaskGridCustomerNameTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly TaskRepository _taskRepo;

        public TaskGridCustomerNameTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            // TestDbContextFactory 未同步 TaskInfo（只同步了 Task_follow），这里补齐；CRM_Customer 由 SyncSchema 同步
            _fsql.CodeFirst.SyncStructure<TaskInfo>();
            _taskRepo = new TaskRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static TaskInfo NewTask(string id, string customerId = "")
        {
            return new TaskInfo
            {
                id = id,
                task_title = $"任务-{id}",
                task_content = $"内容-{id}",
                customer_id = customerId,
                executive_id = "TEST_USER",
                executive_time = new DateTime(2024, 6, 15),
                task_status_id = 0,
                priority_id = 1,
                create_id = "ADMIN",
                create_time = new DateTime(2024, 6, 1)
            };
        }

        private static CRM_Customer NewCustomer(string id, string name)
        {
            return new CRM_Customer
            {
                id = id,
                cus_name = name
            };
        }

        // FreeSql.Insert<T1> 按【编译期】类型绑定 T1，必须用强类型集合，不能用 object[]（否则报
        // "data type ... is inconsistent with AsType (System Object)"）
        private async Task InsertCustomersAsync(params CRM_Customer[] rows)
        {
            foreach (var c in rows)
            {
                await _fsql.Insert(c).ExecuteAffrowsAsync();
            }
        }

        private async Task InsertTasksAsync(params TaskInfo[] rows)
        {
            foreach (var t in rows)
            {
                await _fsql.Insert(t).ExecuteAffrowsAsync();
            }
        }

        // ============ Controller 装配辅助 ============

        private TaskController CreateController()
        {
            var taskSvc = new Mock<ITaskService>();
            taskSvc.Setup(s => s.GridAsync(
                    It.IsAny<Expression<Func<TaskInfo, bool>>>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<string>()))
                .Returns((Expression<Func<TaskInfo, bool>> e, int p, int l, string ob)
                    => _taskRepo.GridAsync(e, p, l, ob));

            // 客户服务透传到同一 in-memory 仓储，真实触发按 id 批量取名的逻辑
            var custSvc = new Mock<ICRM_CustomerService>();
            custSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer, bool>>>()))
                .Returns((Expression<Func<CRM_Customer, bool>> e)
                    => Task.FromResult(new XHDData<CRM_Customer>
                    {
                        data = _fsql.Select<CRM_Customer>().Where(e).ToList()
                    }));

            var ctrl = new TaskController(
                new Mock<ILogger<TaskController>>().Object,
                taskSvc.Object,
                new Mock<ITask_followService>().Object,
                custSvc.Object,
                new Mock<ISys_ParamService>().Object,
                new Mock<IDBAuthService>().Object);

            var httpCtx = new DefaultHttpContext();
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

        private static async Task<Dictionary<string, string>> GridAsMapAsync(TaskController ctrl)
        {
            var json = await ctrl.Grid(new PageView<TaskInfo> { Page = 1, Limit = 100 });
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            var map = new Dictionary<string, string>();
            foreach (var t in data)
            {
                map[(string)t["id"]!] = (string)(t["customer_name"] ?? string.Empty);
            }
            return map;
        }

        // ============ 用例 ============

        [Fact]
        public async Task Grid_TaskWithCustomer_FillsCustomerName()
        {
            await InsertCustomersAsync(NewCustomer("C1", "测试客户A"));
            await InsertTasksAsync(NewTask("T1", "C1"));

            var map = await GridAsMapAsync(CreateController());

            Assert.Equal("测试客户A", map["T1"]);
        }

        [Fact]
        public async Task Grid_TaskWithoutCustomer_KeepsEmptyCustomerName()
        {
            await InsertCustomersAsync(NewCustomer("C1", "测试客户A"));
            await InsertTasksAsync(NewTask("T1", ""), NewTask("T2"));

            var map = await GridAsMapAsync(CreateController());

            Assert.Equal("", map["T1"]);
            Assert.Equal("", map["T2"]);
        }

        [Fact]
        public async Task Grid_CustomerMissing_LeavesEmptyCustomerName()
        {
            // customer_id 指向不存在的客户（历史脏数据），不得抛异常，且回填为空
            await InsertTasksAsync(NewTask("T1", "GONE"));

            var map = await GridAsMapAsync(CreateController());

            Assert.Equal("", map["T1"]);
        }

        [Fact]
        public async Task Grid_MultipleTasksShareCustomer_FillsAll()
        {
            await InsertCustomersAsync(NewCustomer("C1", "共享客户"), NewCustomer("C2", "另一客户"));
            await InsertTasksAsync(NewTask("T1", "C1"), NewTask("T2", "C2"), NewTask("T3", "C1"));

            var map = await GridAsMapAsync(CreateController());

            Assert.Equal("共享客户", map["T1"]);
            Assert.Equal("另一客户", map["T2"]);
            Assert.Equal("共享客户", map["T3"]);
        }
    }
}
