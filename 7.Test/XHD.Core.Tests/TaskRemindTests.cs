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

using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.View.Controllers;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.38 单元测试：TaskController.Remind（门户任务提醒）。
    /// 对应 A 侧 Server.MSG_Task.TaskRemind：本人 + 未完成，按 executive_time asc 取前 N 条。
    /// Controller 装配风格对齐 MyNoteMessageTests：真实 Repository + SQLite in-memory，
    /// Service 层用 Mock 透传到真实仓储，ControllerContext 注入 ClaimTypes.Sid/Name。
    /// </summary>
    public class TaskRemindTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly TaskRepository _taskRepo;

        public TaskRemindTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            // TestDbContextFactory 未同步 TaskInfo（只同步了 Task_follow），这里补齐
            _fsql.CodeFirst.SyncStructure<TaskInfo>();
            _taskRepo = new TaskRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static TaskInfo NewTask(
            string id,
            string execId = "TEST_USER",
            int? status = 0,
            DateTime? execTime = null)
        {
            return new TaskInfo
            {
                id = id,
                task_title = $"任务-{id}",
                task_content = $"内容-{id}",
                executive_id = execId,
                executive_time = execTime ?? new DateTime(2024, 6, 15),
                task_status_id = status,
                priority_id = 1,
                is_check = status == 1 ? 1 : 0,
                create_id = "ADMIN",
                create_time = new DateTime(2024, 6, 1)
            };
        }

        private async Task InsertTaskAsync(TaskInfo t) => await _fsql.Insert(t).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        private TaskController CreateController(string userId = "TEST_USER")
        {
            // Service 层用 Mock 透传到真实仓储，与 MyNoteMessageTests 的装配方式一致
            var taskSvc = new Mock<ITaskService>();
            taskSvc.Setup(s => s.GridAsync(
                    It.IsAny<Expression<Func<TaskInfo, bool>>>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<string>()))
                .Returns((Expression<Func<TaskInfo, bool>> e, int p, int l, string ob)
                    => _taskRepo.GridAsync(e, p, l, ob));

            var ctrl = new TaskController(
                new Mock<ILogger<TaskController>>().Object,
                taskSvc.Object,
                new Mock<ITask_followService>().Object,
                new Mock<ICRM_CustomerService>().Object,
                new Mock<ISys_ParamService>().Object,
                new Mock<IDBAuthService>().Object);

            var httpCtx = new DefaultHttpContext();
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

        // ============ Remind：未完成任务按 executive_time asc 返回 ============

        [Fact]
        public async Task Remind_UnfinishedTasks_ReturnsSortedByExecutiveTimeAsc()
        {
            // 3 条未完成，执行时间各不相同；1 条已完成应被排除
            await InsertTaskAsync(NewTask("T1", execTime: new DateTime(2024, 6, 10)));
            await InsertTaskAsync(NewTask("T2", execTime: new DateTime(2024, 6, 1)));
            await InsertTaskAsync(NewTask("T3", execTime: new DateTime(2024, 6, 20)));
            await InsertTaskAsync(NewTask("DONE1", status: 1, execTime: new DateTime(2024, 5, 1)));

            var ctrl = CreateController();

            var json = await ctrl.Remind();
            var obj = JObject.Parse(json);

            // XHDResult.Success(arr) => code=0, data 为数组
            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;

            // 已完成的不出现
            Assert.Equal(3, data.Count);
            Assert.DoesNotContain(data, t => (string)t["id"]! == "DONE1");

            // 升序：T2(6/1) > T1(6/10) > T3(6/20)
            Assert.Equal("T2", (string)data[0]["id"]!);
            Assert.Equal("T1", (string)data[1]["id"]!);
            Assert.Equal("T3", (string)data[2]["id"]!);
            // 执行时间随顺序单调不降（JSON 里是字符串，比较前先转回 DateTime）
            Assert.True(ParseExecTime(data[0]["executive_time"]) <= ParseExecTime(data[1]["executive_time"]));
            Assert.True(ParseExecTime(data[1]["executive_time"]) <= ParseExecTime(data[2]["executive_time"]));
        }

        /// <summary>
        /// 把 JSON 里的 executive_time（可能是 ISO 字符串或 /Date(...)/）解析回 DateTime 用于比较。
        /// </summary>
        private static DateTime ParseExecTime(object? val)
        {
            var s = val?.ToString() ?? string.Empty;

            if (s.Contains("Date("))
            {
                // /Date(1717200000000+0800)/
                var inner = s.Substring(s.IndexOf('(') + 1);
                inner = inner.Substring(0, inner.IndexOf(')'));
                var parts = inner.Split('+');
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddMilliseconds(long.Parse(parts[0]));
            }

            return DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal);
        }

        [Fact]
        public async Task Remind_Empty_ReturnsEmptyArray()
        {
            var ctrl = CreateController();

            var json = await ctrl.Remind();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Empty(data);
        }

        [Fact]
        public async Task Remind_OtherEmployeeTasks_NotReturned()
        {
            // 本人 1 条未完成；另一员工 2 条未完成（含更早的执行时间）
            await InsertTaskAsync(NewTask("OWN1", execTime: new DateTime(2024, 6, 15)));
            await InsertTaskAsync(NewTask("OTHER1", execId: "OTHER_EMP", execTime: new DateTime(2024, 6, 1)));
            await InsertTaskAsync(NewTask("OTHER2", execId: "OTHER_EMP", execTime: new DateTime(2024, 6, 2)));

            var ctrl = CreateController("TEST_USER");

            var json = await ctrl.Remind();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("OWN1", (string)data[0]["id"]!);
            Assert.DoesNotContain(data, t => (string)t["executive_id"]! == "OTHER_EMP");
        }

        [Fact]
        public async Task Remind_LimitClampedToOne_WhenBelowRange()
        {
            // limit < 1 夹逼到 1
            for (int i = 1; i <= 3; i++)
            {
                await InsertTaskAsync(NewTask($"T{i}", execTime: new DateTime(2024, 6, 1) + TimeSpan.FromDays(i)));
            }

            var ctrl = CreateController();

            var json = await ctrl.Remind(limit: 0);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            // 夹逼到 1 后仍取最紧急（执行时间最早）的那条
            Assert.Equal("T1", (string)data[0]["id"]!);
        }

        [Fact]
        public async Task Remind_LimitClampedToFifty_WhenAboveRange()
        {
            // limit > 50 夹逼到 50：插入 55 条未完成任务
            for (int i = 1; i <= 55; i++)
            {
                await InsertTaskAsync(NewTask($"T{i:D2}", execTime: new DateTime(2024, 6, 1) + TimeSpan.FromDays(i)));
            }

            var ctrl = CreateController();

            var json = await ctrl.Remind(limit: 100);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(50, data.Count);
            // 升序首个应为执行时间最早的 T01
            Assert.Equal("T01", (string)data[0]["id"]!);
        }

        [Fact]
        public async Task Remind_LimitDefaultIsSeven()
        {
            // 默认 limit=7（对齐 A 侧 GetList(7, ...)）：插入 10 条
            for (int i = 1; i <= 10; i++)
            {
                await InsertTaskAsync(NewTask($"T{i:D2}", execTime: new DateTime(2024, 6, 1) + TimeSpan.FromDays(i)));
            }

            var ctrl = CreateController();

            var json = await ctrl.Remind();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(7, data.Count);
            Assert.Equal("T01", (string)data[0]["id"]!);
            Assert.Equal("T07", (string)data[6]["id"]!);
        }

        [Fact]
        public async Task Remind_DoesNotModifyTaskStatus()
        {
            // 纯查询：调用前后状态不应变化（区别于 NoticeRemind 的"读即标记"）
            await InsertTaskAsync(NewTask("T1", execTime: new DateTime(2024, 6, 1)));
            await InsertTaskAsync(NewTask("T2", status: 1, execTime: new DateTime(2024, 6, 2)));

            var ctrl = CreateController();

            await ctrl.Remind();
            await ctrl.Remind();

            var rows = await _fsql.Select<TaskInfo>().ToListAsync();
            Assert.All(rows, r => Assert.Equal(r.id == "T2" ? (int?)1 : (int?)0, r.task_status_id));
            // 未完成任务仍为未完成
            Assert.Single(rows.Where(r => r.task_status_id == 0));
        }
    }
}
