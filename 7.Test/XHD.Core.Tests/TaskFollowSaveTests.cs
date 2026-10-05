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
    /// Phase 3 W4：任务跟进 TaskFollowController 真测试。
    /// 覆盖 DeleteWhere（按 task_id 级联删除）、Grid（按 task_id 过滤/分页/权限）、
    /// Save（新增默认值/更新/保留创建字段）。
    /// 走真实 SQLite 内存库 + 真实 Task_followService。
    /// </summary>
    public class TaskFollowSaveTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Task_followRepository _followRepo;

        private const string UserId = "TEST_USER";

        public TaskFollowSaveTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _followRepo = new Task_followRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static Task_follow NewFollow(string id, string taskId, string followId, DateTime? followTime, string content = "跟进内容", int? status = null)
        {
            return new Task_follow
            {
                id = id,
                task_id = taskId,
                follow_id = followId,
                follow_time = followTime,
                follow_content = content,
                follow_status = status
            };
        }

        private async Task InsertAsync(Task_follow f) => await _fsql.Insert(f).ExecuteAffrowsAsync();

        // ============ Controller 装配 ============

        private TaskFollowController CreateController(
            string queryString = "",
            int authtype = 5,
            List<string> authEmpList = null)
        {
            var authMock = new Mock<IDBAuthService>();
            authMock.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData
                {
                    authtype = authtype,
                    empList = authEmpList ?? new List<string>()
                });

            return TestControllerHelper.CreateWithHttpContext<TaskFollowController>(
                queryString, UserId, "Test User",
                new Mock<ILogger<TaskFollowController>>().Object,
                new Task_followService(_followRepo),
                authMock.Object);
        }

        // ============ DeleteWhere ============

        [Fact]
        public async Task DeleteWhere_RemovesAllFollowsOfTask()
        {
            await InsertAsync(NewFollow("F1", "T1", "E1", new DateTime(2024, 6, 1, 9, 0, 0)));
            await InsertAsync(NewFollow("F2", "T1", "E1", new DateTime(2024, 6, 2, 9, 0, 0)));
            await InsertAsync(NewFollow("F3", "T1", "E2", new DateTime(2024, 6, 3, 9, 0, 0)));
            await InsertAsync(NewFollow("F4", "T2", "E1", new DateTime(2024, 6, 4, 9, 0, 0)));

            var ctrl = CreateController();

            var json = await ctrl.DeleteWhere("T1");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal("删除成功！", (string)obj["msg"]!);

            var remaining = await _followRepo.GridAsync(a => true);
            Assert.Single(remaining);
            Assert.Equal("F4", remaining[0].id);
        }

        [Fact]
        public async Task DeleteWhere_EmptyTaskId_ReturnsError()
        {
            await InsertAsync(NewFollow("F1", "T1", "E1", new DateTime(2024, 6, 1)));

            var ctrl = CreateController();

            var json = await ctrl.DeleteWhere("");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数错误！", (string)obj["msg"]!);
            Assert.Single(await _followRepo.GridAsync(a => true));
        }

        [Fact]
        public async Task DeleteWhere_NonexistentTask_StillSuccess()
        {
            await InsertAsync(NewFollow("F1", "T1", "E1", new DateTime(2024, 6, 1)));

            var ctrl = CreateController();

            var json = await ctrl.DeleteWhere("T_NOT_EXIST");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal("删除成功！", (string)obj["msg"]!);
            Assert.Single(await _followRepo.GridAsync(a => true));
        }

        // ============ Grid ============

        [Fact]
        public async Task Grid_ByTaskId_ReturnsOnlyThatTask()
        {
            await InsertAsync(NewFollow("F1", "T1", "E1", new DateTime(2024, 6, 1)));
            await InsertAsync(NewFollow("F2", "T1", "E2", new DateTime(2024, 6, 2)));
            await InsertAsync(NewFollow("F3", "T2", "E1", new DateTime(2024, 6, 3)));

            var ctrl = CreateController(queryString: "?task_id=T1");

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Task_follow>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            Assert.All((JArray)obj["data"]!, d => Assert.Equal("T1", (string)d["task_id"]!));
        }

        [Fact]
        public async Task Grid_WithoutTaskId_ReturnsAllSortedByFollowTimeDesc()
        {
            await InsertAsync(NewFollow("F1", "T1", "E1", new DateTime(2024, 6, 1)));
            await InsertAsync(NewFollow("F2", "T1", "E1", new DateTime(2024, 6, 3)));
            await InsertAsync(NewFollow("F3", "T2", "E1", new DateTime(2024, 6, 2)));

            var ctrl = CreateController();

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Task_follow>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(3, (int)obj["count"]!);
            var ids = ((JArray)obj["data"]!).Select(d => (string)d["id"]!).ToList();
            Assert.Equal(new[] { "F2", "F3", "F1" }, ids);
        }

        // ============ Save（新增） ============

        [Fact]
        public async Task Save_New_FollowPersistedWithDefaults()
        {
            var ctrl = CreateController();
            var model = NewFollow(string.Empty, "T1", string.Empty, null, content: "电话回访");

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal("新增成功！", (string)obj["msg"]!);

            var all = await _followRepo.GridAsync(a => a.task_id == "T1");
            Assert.Single(all);
            Assert.Equal("电话回访", all[0].follow_content);
            Assert.Equal(UserId, all[0].follow_id);
            Assert.Equal(0, all[0].follow_status);
            Assert.NotNull(all[0].follow_time);
        }

        [Fact]
        public async Task Save_New_ProvidedValuesNotOverridden()
        {
            var ctrl = CreateController();
            var model = NewFollow(string.Empty, "T1", "E9", new DateTime(2024, 1, 1, 8, 0, 0), content: "上门拜访", status: 1);

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var all = await _followRepo.GridAsync(a => a.task_id == "T1");
            Assert.Single(all);
            Assert.Equal("E9", all[0].follow_id);
            Assert.Equal(1, all[0].follow_status);
            Assert.Equal(new DateTime(2024, 1, 1, 8, 0, 0), all[0].follow_time.Value);
        }

        [Fact]
        public async Task Save_New_EmptyTaskId_ReturnsError()
        {
            var ctrl = CreateController();
            var model = NewFollow(string.Empty, string.Empty, "E1", new DateTime(2024, 6, 1));

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("任务ID不能为空！", (string)obj["msg"]!);
            Assert.Empty(await _followRepo.GridAsync(a => true));
        }

        // ============ Save（更新） ============

        [Fact]
        public async Task Save_Update_ExistingFollow_Updated()
        {
            await InsertAsync(NewFollow("F1", "T1", "E1", new DateTime(2024, 6, 1), content: "旧内容", status: 0));

            var ctrl = CreateController();
            var model = NewFollow("F1", "T_FORGED", "E_FORGED", new DateTime(2024, 7, 1), content: "新内容", status: 1);

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal("保存成功！", (string)obj["msg"]!);

            var fetched = await _fsql.Select<Task_follow>().Where(a => a.id == "F1").FirstAsync();
            Assert.Equal("新内容", fetched.follow_content);
            Assert.Equal(1, fetched.follow_status);
            // 客户端伪造的 task_id / follow_id 被丢弃，沿用原记录
            Assert.Equal("T1", fetched.task_id);
            Assert.Equal("E1", fetched.follow_id);
        }

        [Fact]
        public async Task Save_Update_NotFound_ReturnsError()
        {
            var ctrl = CreateController();
            var model = NewFollow("NOT_EXIST", "T1", "E1", new DateTime(2024, 6, 1));

            var json = await ctrl.Save(model);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("找不到数据！", (string)obj["msg"]!);
        }
    }
}
