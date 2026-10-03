using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json.Linq;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 4 Wave 2 单元测试：便签/公告提醒 + 日程快速新增。
    /// 覆盖 #13 MyCalendarController.QuickAdd + #14 MyNoteController.Remind
    /// + #15 MessageNewsController.NoticeRemind + Schema 字段存在性验证（C5）。
    /// </summary>
    public class MyNoteMessageTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly My_NoteRepository _noteRepo;
        private readonly Message_newsRepository _newsRepo;
        private readonly My_CalendarRepository _calRepo;

        public MyNoteMessageTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _noteRepo = new My_NoteRepository(_fsql);
            _newsRepo = new Message_newsRepository(_fsql);
            _calRepo = new My_CalendarRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static My_Note NewNote(string id, string empId = "TEST_USER", bool isRead = false, DateTime? noteTime = null)
        {
            return new My_Note
            {
                id = id,
                emp_id = empId,
                Note_time = noteTime ?? new DateTime(2024, 6, 15),
                content = $"便签-{id}",
                color = "#fff",
                isRead = isRead,
                read_time = isRead ? DateTime.Now : null
            };
        }

        private static Message_news NewNews(string id, bool isRead = false, DateTime? createTime = null)
        {
            return new Message_news
            {
                id = id,
                create_id = "ADMIN",
                create_time = createTime ?? new DateTime(2024, 6, 15),
                news_title = $"公告-{id}",
                news_content = "内容",
                isRead = isRead,
                read_time = isRead ? DateTime.Now : null
            };
        }

        private static My_Calendar NewCal(string id, string empId = "TEST_USER", string title = "测试日程")
        {
            return new My_Calendar
            {
                id = id,
                emp_id = empId,
                title = title,
                startDate = "2024-07-01",
                endDate = "2024-07-01",
                startTime = "10:00",
                endTime = "11:00",
                description = "描述"
            };
        }

        private async Task InsertNoteAsync(My_Note n) => await _fsql.Insert(n).ExecuteAffrowsAsync();
        private async Task InsertNewsAsync(Message_news n) => await _fsql.Insert(n).ExecuteAffrowsAsync();
        private async Task InsertCalAsync(My_Calendar c) => await _fsql.Insert(c).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        private static Mock<IDBAuthService> CreateFullAuth()
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            // Sprint 10.38：MyNoteController.UpdateXY/Delete 的归属校验会调用 GetDataAuth，
            // 不 mock 的话 loose mock 返回 null，authtype 访问会 NullReferenceException。
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHD.Core.Common.XHDRoleData
                { authtype = 4, empList = new List<string>() });
            return auth;
        }

        /// <summary>
        /// Sprint 10.38：受限数据权限（默认本部 authtype=2），用于验证便签归属校验。
        /// </summary>
        private static Mock<IDBAuthService> CreateRestrictedAuth(
            List<string> empList, int authtype = 2)
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHD.Core.Common.XHDRoleData { authtype = authtype, empList = empList });
            return auth;
        }

        private MyCalendarController CreateCalendarController(string userId = "TEST_USER")
        {
            var calSvc = new Mock<IMy_CalendarService>();
            calSvc.Setup(s => s.AddAsync(It.IsAny<My_Calendar>()))
                .Returns((My_Calendar m) => _calRepo.AddAsync(m));
            calSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<My_Calendar, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<My_Calendar, bool>> e, int p, int l) => _calRepo.GridAsync(e, p, l));

            var ctrl = new MyCalendarController(calSvc.Object, new Moq.Mock<IDBAuthService>().Object);
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

        private MyNoteController CreateNoteController(
            string userId = "TEST_USER", Mock<IDBAuthService> authMock = null)
        {
            var noteSvc = new Mock<IMy_NoteService>();
            noteSvc.Setup(s => s.RemindAsync(It.IsAny<string>(), It.IsAny<int>()))
                .Returns((string empId, int limit) => _noteRepo.RemindAsync(empId, limit));
            // Sprint 10.38：UpdateXY/Delete 新增归属校验，走单参 GridAsync。
            // 注意 Service 单参重载返回 XHDData<T>，Repository 单参重载返回 List<T>，
            // 故改用仓储分页重载（Limit 取大值即等价全量）。
            noteSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<My_Note, bool>>>()))
                .Returns((Expression<Func<My_Note, bool>> e) => _noteRepo.GridAsync(e, 1, 100000));
            noteSvc.Setup(s => s.UpdateAsync(
                    It.IsAny<Expression<Func<My_Note, My_Note>>>(),
                    It.IsAny<Expression<Func<My_Note, bool>>>()))
                .Returns((Expression<Func<My_Note, My_Note>> set, Expression<Func<My_Note, bool>> w)
                    => _noteRepo.UpdateAsync(set, w));
            noteSvc.Setup(s => s.DeleteAsync(It.IsAny<string>()))
                .Returns((string id) => _noteRepo.DeleteAsync(id));

            authMock ??= CreateFullAuth();
            var ctrl = new MyNoteController(noteSvc.Object, authMock.Object);
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

        private MessageNewsController CreateNewsController(string userId = "TEST_USER")
        {
            var newsSvc = new Mock<IMessage_newsService>();
            newsSvc.Setup(s => s.NoticeRemindAsync(It.IsAny<int>()))
                .Returns((int limit) => _newsRepo.NoticeRemindAsync(limit));

            var logMock = new Mock<ISys_logService>();
            logMock.Setup(l => l.UpdateLog(It.IsAny<Sys_log>())).ReturnsAsync(1);
            logMock.Setup(l => l.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);

            var ctrl = new MessageNewsController(
                new Moq.Mock<Microsoft.Extensions.Logging.ILogger<MessageNewsController>>().Object,
                newsSvc.Object,
                logMock.Object,
                CreateFullAuth().Object);

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

        // ============ Schema 字段存在性（C5 补强验证） ============

        [Fact]
        public void Schema_My_Note_HasIsReadField()
        {
            var prop = typeof(My_Note).GetProperty("isRead");
            Assert.NotNull(prop);
            Assert.Equal(typeof(bool), prop.PropertyType);
        }

        [Fact]
        public void Schema_My_Note_HasReadTimeField()
        {
            var prop = typeof(My_Note).GetProperty("read_time");
            Assert.NotNull(prop);
            Assert.Equal(typeof(DateTime?), prop.PropertyType);
        }

        [Fact]
        public void Schema_Message_news_HasIsReadField()
        {
            var prop = typeof(Message_news).GetProperty("isRead");
            Assert.NotNull(prop);
            Assert.Equal(typeof(bool), prop.PropertyType);
        }

        [Fact]
        public void Schema_Message_news_HasReadTimeField()
        {
            var prop = typeof(Message_news).GetProperty("read_time");
            Assert.NotNull(prop);
            Assert.Equal(typeof(DateTime?), prop.PropertyType);
        }

        // ============ #13 QuickAdd：日程快速新增 ============

        [Fact]
        public async Task QuickAdd_ValidModel_AddsSuccess()
        {
            var ctrl = CreateCalendarController();

            var model = new My_Calendar
            {
                title = "快速会议",
                startDate = "2024-07-01",
                endDate = "2024-07-01",
                startTime = "14:00",
                endTime = "15:00",
                description = "临时安排"
            };

            var json = await ctrl.QuickAdd(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.False(string.IsNullOrWhiteSpace((string)obj["msg"]!));

            // 断言持久化成功
            var row = (await _fsql.Select<My_Calendar>().FirstAsync())!;
            Assert.Equal("快速会议", row.title);
            Assert.Equal("TEST_USER", row.emp_id);
            Assert.Equal("2024-07-01", row.startDate);
        }

        [Fact]
        public async Task QuickAdd_EmptyTitle_ReturnsError()
        {
            var ctrl = CreateCalendarController();

            var model = new My_Calendar
            {
                title = "",
                startDate = "2024-07-01"
            };

            var json = await ctrl.QuickAdd(model);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("标题", (string)obj["msg"]!);
            Assert.Contains("不能为空", (string)obj["msg"]!);
        }

        [Fact]
        public async Task QuickAdd_InvalidStartDate_ReturnsError()
        {
            var ctrl = CreateCalendarController();

            var model = new My_Calendar
            {
                title = "标题",
                startDate = "not-a-date"
            };

            var json = await ctrl.QuickAdd(model);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("日期", (string)obj["msg"]!);
        }

        [Fact]
        public async Task QuickAdd_EmptyEndDate_DefaultsToStartDate()
        {
            var ctrl = CreateCalendarController();

            var model = new My_Calendar
            {
                title = "单天日程",
                startDate = "2024-08-15",
                endDate = ""
            };

            var json = await ctrl.QuickAdd(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var row = (await _fsql.Select<My_Calendar>().FirstAsync())!;
            Assert.Equal("2024-08-15", row.endDate);
            Assert.Equal("2024-08-15", row.startDate);
        }

        [Fact]
        public async Task QuickAdd_SetsEmpIdToCurrentUser()
        {
            var ctrl = CreateCalendarController("TEST_USER");

            var model = new My_Calendar
            {
                title = "归属测试",
                startDate = "2024-09-01"
            };

            var json = await ctrl.QuickAdd(model);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var row = (await _fsql.Select<My_Calendar>().FirstAsync())!;
            Assert.Equal("TEST_USER", row.emp_id);
            // 断言 ID 自动补齐（非空 GUID 格式）
            Assert.False(string.IsNullOrWhiteSpace(row.id));
            Assert.True(Guid.TryParse(row.id, out _));
        }

        // ============ #14 Remind：便签未读提醒 ============

        [Fact]
        public async Task Remind_UnreadNotes_ReturnsTopN()
        {
            // Arrange：3 条未读，按时间降序
            await InsertNoteAsync(NewNote("N1", "TEST_USER", noteTime: new DateTime(2024, 6, 1)));
            await InsertNoteAsync(NewNote("N2", "TEST_USER", noteTime: new DateTime(2024, 6, 15)));
            await InsertNoteAsync(NewNote("N3", "TEST_USER", noteTime: new DateTime(2024, 6, 10)));

            var ctrl = CreateNoteController();

            var json = await ctrl.Remind();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(3, data.Count);
            // 排序：N2 > N3 > N1
            Assert.Equal("N2", (string)data[0]["id"]!);
            Assert.Equal("N3", (string)data[1]["id"]!);
            Assert.Equal("N1", (string)data[2]["id"]!);
        }

        [Fact]
        public async Task Remind_Empty_ReturnsEmptyArray()
        {
            var ctrl = CreateNoteController();

            var json = await ctrl.Remind();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Empty(data);
        }

        [Fact]
        public async Task Remind_MarksReadAndNextCallEmpty()
        {
            await InsertNoteAsync(NewNote("N1", "TEST_USER"));
            await InsertNoteAsync(NewNote("N2", "TEST_USER"));

            var ctrl = CreateNoteController();

            var json1 = await ctrl.Remind();
            var obj1 = JObject.Parse(json1);
            Assert.Equal(0, (int)obj1["code"]!);
            Assert.Equal(2, ((JArray)obj1["data"]!).Count);

            // 数据层已标记为已读
            var persisted = await _fsql.Select<My_Note>().ToListAsync();
            Assert.All(persisted, n => Assert.True(n.isRead));
            Assert.All(persisted, n => Assert.NotNull(n.read_time));

            // 第二次调用：应返回空
            var json2 = await ctrl.Remind();
            var obj2 = JObject.Parse(json2);
            Assert.Equal(0, (int)obj2["code"]!);
            Assert.Empty((JArray)obj2["data"]!);
        }

        [Fact]
        public async Task Remind_OtherEmployeeNotes_NotReturned()
        {
            // 只查自己的便签，隔离员工
            await InsertNoteAsync(NewNote("OWN1", "TEST_USER"));
            await InsertNoteAsync(NewNote("OTHER1", "OTHER_EMP"));
            await InsertNoteAsync(NewNote("OTHER2", "OTHER_EMP"));

            var ctrl = CreateNoteController("TEST_USER");

            var json = await ctrl.Remind();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("OWN1", (string)data[0]["id"]!);

            // 其他员工便签未被标记
            var otherRows = await _fsql.Select<My_Note>()
                .Where(a => a.emp_id == "OTHER_EMP").ToListAsync();
            Assert.All(otherRows, n => Assert.False(n.isRead));
            Assert.All(otherRows, n => Assert.Null(n.read_time));
        }

        [Fact]
        public async Task Remind_LimitRespectsUpperBound()
        {
            // 插入 15 条，limit=5 只取前 5
            for (int i = 1; i <= 15; i++)
            {
                await InsertNoteAsync(NewNote($"N{i:D2}", "TEST_USER", noteTime: new DateTime(2024, 6, 1) + TimeSpan.FromDays(i)));
            }

            var ctrl = CreateNoteController();

            var json = await ctrl.Remind(limit: 5);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(5, ((JArray)obj["data"]!).Count);

            // 剩余 10 条仍未读
            int unreadCount = (int)await _fsql.Select<My_Note>().Where(a => !a.isRead).CountAsync();
            Assert.Equal(10, unreadCount);
        }

        // ============ #14b Sprint 10.38 回归：UpdateXY / Delete 归属校验 ============

        [Fact]
        public async Task UpdateXY_OtherEmpNote_RestrictedUser_ReturnsError()
        {
            // 受限用户移动他人便签应被拒（越权写）
            await InsertNoteAsync(NewNote("OTHER1", "OTHER_EMP"));

            var ctrl = CreateNoteController("TEST_USER",
                CreateRestrictedAuth(new List<string> { "TEST_USER" }));

            var json = await ctrl.UpdateXY(new My_Note { id = "OTHER1", top = 10, left = 20 });

            // XHDResult.Error(msg) → code = -1（非 1），与既有 Save/Update 错误断言口径一致
            var obj = JObject.Parse(json);
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限", (string)obj["msg"]!);
        }

        [Fact]
        public async Task UpdateXY_OwnNote_UpdatesCoordinates()
        {
            await InsertNoteAsync(NewNote("OWN1", "TEST_USER"));

            var ctrl = CreateNoteController("TEST_USER",
                CreateRestrictedAuth(new List<string> { "TEST_USER" }));

            var json = await ctrl.UpdateXY(new My_Note { id = "OWN1", top = 100, left = 200 });

            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<My_Note>().Where(a => a.id == "OWN1").FirstAsync();
            Assert.Equal(100, row.top);
            Assert.Equal(200, row.left);
        }

        [Fact]
        public async Task UpdateXY_NonexistentNote_ReturnsNotFound()
        {
            var ctrl = CreateNoteController("TEST_USER",
                CreateRestrictedAuth(new List<string> { "TEST_USER" }));

            var json = await ctrl.UpdateXY(new My_Note { id = "NOPE", top = 1, left = 2 });

            var obj = JObject.Parse(json);
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("找不到数据", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Delete_OtherEmpNote_RestrictedUser_ReturnsError()
        {
            // 受限用户删除他人便签应被拒（越权删除）
            await InsertNoteAsync(NewNote("OTHER1", "OTHER_EMP"));

            var ctrl = CreateNoteController("TEST_USER",
                CreateRestrictedAuth(new List<string> { "TEST_USER" }));

            var json = await ctrl.Delete("OTHER1");

            var obj = JObject.Parse(json);
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限", (string)obj["msg"]!);

            // 数据未被删除
            Assert.Single(await _fsql.Select<My_Note>().ToListAsync());
        }

        [Fact]
        public async Task Delete_OwnNote_DeletesRow()
        {
            await InsertNoteAsync(NewNote("OWN1", "TEST_USER"));

            var ctrl = CreateNoteController("TEST_USER",
                CreateRestrictedAuth(new List<string> { "TEST_USER" }));

            var json = await ctrl.Delete("OWN1");

            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Empty(await _fsql.Select<My_Note>().ToListAsync());
        }

        [Fact]
        public async Task Delete_FullAccessUser_CanDeleteOtherEmpNote()
        {
            // 全公司权限可删除任意人便签（设计意图，与 DeleteAsync 原有语义一致）
            await InsertNoteAsync(NewNote("OTHER1", "OTHER_EMP"));

            var ctrl = CreateNoteController("TEST_USER", CreateFullAuth());

            var json = await ctrl.Delete("OTHER1");

            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Empty(await _fsql.Select<My_Note>().ToListAsync());
        }

        // ============ #15 NoticeRemind：公告未读提醒 ============

        [Fact]
        public async Task NoticeRemind_UnreadNotices_ReturnsTopN()
        {
            await InsertNewsAsync(NewNews("A1", createTime: new DateTime(2024, 6, 1)));
            await InsertNewsAsync(NewNews("A2", createTime: new DateTime(2024, 6, 15)));
            await InsertNewsAsync(NewNews("A3", createTime: new DateTime(2024, 6, 10)));

            var ctrl = CreateNewsController();

            var json = await ctrl.NoticeRemind();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(3, data.Count);
            Assert.Equal("A2", (string)data[0]["id"]!);
            Assert.Equal("A3", (string)data[1]["id"]!);
            Assert.Equal("A1", (string)data[2]["id"]!);
        }

        [Fact]
        public async Task NoticeRemind_Empty_ReturnsEmptyArray()
        {
            var ctrl = CreateNewsController();

            var json = await ctrl.NoticeRemind();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task NoticeRemind_MarksReadAndNextCallEmpty()
        {
            await InsertNewsAsync(NewNews("A1"));
            await InsertNewsAsync(NewNews("A2"));

            var ctrl = CreateNewsController();

            var json1 = await ctrl.NoticeRemind();
            var obj1 = JObject.Parse(json1);
            Assert.Equal(0, (int)obj1["code"]!);
            Assert.Equal(2, ((JArray)obj1["data"]!).Count);

            var persisted = await _fsql.Select<Message_news>().ToListAsync();
            Assert.All(persisted, n => Assert.True(n.isRead));
            Assert.All(persisted, n => Assert.NotNull(n.read_time));

            var json2 = await ctrl.NoticeRemind();
            var obj2 = JObject.Parse(json2);
            Assert.Equal(0, (int)obj2["code"]!);
            Assert.Empty((JArray)obj2["data"]!);
        }

        [Fact]
        public async Task NoticeRemind_AlreadyRead_NoticesNotReturned()
        {
            // 混合场景：2 条已读 + 1 条未读
            await InsertNewsAsync(NewNews("READ1", isRead: true, createTime: new DateTime(2024, 6, 1)));
            await InsertNewsAsync(NewNews("READ2", isRead: true, createTime: new DateTime(2024, 6, 2)));
            await InsertNewsAsync(NewNews("UNREAD1", isRead: false, createTime: new DateTime(2024, 6, 3)));

            var ctrl = CreateNewsController();

            var json = await ctrl.NoticeRemind();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("UNREAD1", (string)data[0]["id"]!);

            // 已读公告保持不变
            var read1 = (await _fsql.Select<Message_news>().Where(a => a.id == "READ1").FirstAsync())!;
            Assert.True(read1.isRead);
        }

        [Fact]
        public async Task NoticeRemind_GlobalVisibility_AllUsersCanSee()
        {
            // 公告全局可见：不同用户调用 NoticeRemind 应看到相同列表
            await InsertNewsAsync(NewNews("A1", createTime: new DateTime(2024, 6, 15)));
            await InsertNewsAsync(NewNews("A2", createTime: new DateTime(2024, 6, 10)));

            var ctrlUser1 = CreateNewsController("USER_1");
            var ctrlUser2 = CreateNewsController("USER_2");

            var json1 = await ctrlUser1.NoticeRemind();
            var obj1 = JObject.Parse(json1);
            Assert.Equal(0, (int)obj1["code"]!);
            Assert.Equal(2, ((JArray)obj1["data"]!).Count);

            // 第二次调用（USER_2）：由于第一条已标记为已读，应返回空
            var json2 = await ctrlUser2.NoticeRemind();
            var obj2 = JObject.Parse(json2);
            Assert.Equal(0, (int)obj2["code"]!);
            Assert.Empty((JArray)obj2["data"]!);
        }
    }
}