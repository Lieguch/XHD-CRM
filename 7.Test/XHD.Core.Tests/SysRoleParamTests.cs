using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    /// Sprint 6 Wave 1 单元测试：消息/日历/员工角色 9 函数。
    ///   #93  Personal_Calendar.quickupdate
    ///   #94  Personal_Calendar.quickdel
    ///   #95  Personal_Calendar.Today
    ///   #98  Public_news.newsremind
    ///   #110 Sys_role_emp.add
    ///   #111 Sys_role_emp.remove
    ///   #112 Sys_role_emp.emplist
    ///   #113 Sys_role_emp.get
    ///   #116 Sys_Param.validate
    /// 全部走 Repository + SQLite 内存库，避免 Controller 装配复杂度；
    /// 关键 Controller 级路径（QuickUpdate/QuickDel 的归属校验）另用 Moq 补测。
    /// </summary>
    public class SysRoleParamTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly My_CalendarRepository _calRepo;
        private readonly Message_newsRepository _newsRepo;
        private readonly Sys_role_empRepository _roleEmpRepo;
        private readonly Sys_ParamRepository _paramRepo;
        private readonly hr_employeeRepository _empRepo;

        public SysRoleParamTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _calRepo = new My_CalendarRepository(_fsql);
            _newsRepo = new Message_newsRepository(_fsql);
            _roleEmpRepo = new Sys_role_empRepository(_fsql);
            _paramRepo = new Sys_ParamRepository(_fsql);
            _empRepo = new hr_employeeRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static My_Calendar NewCalendar(string id, string empId, string title = "日程",
            string startDate = null, string startTime = null,
            string endDate = null, string endTime = null)
        {
            var now = DateTime.Now;
            var today = now.Date;
            return new My_Calendar
            {
                id = id,
                emp_id = empId,
                title = title,
                startDate = startDate ?? today.ToString("yyyy-MM-dd"),
                startTime = startTime ?? "09:00",
                endDate = endDate ?? today.ToString("yyyy-MM-dd"),
                endTime = endTime ?? "10:00",
                StartDateTime = new DateTime(today.Year, today.Month, today.Day, 9, 0, 0),
                EndDateTime = new DateTime(today.Year, today.Month, today.Day, 10, 0, 0),
                allDay = 0
            };
        }

        private static Message_news NewNews(string id, DateTime createTime, bool isRead = false,
            string title = "新闻")
        {
            return new Message_news
            {
                id = id,
                create_id = "SYSTEM",
                create_time = createTime,
                news_title = title,
                news_content = "内容-" + id,
                isRead = isRead
            };
        }

        private static hr_employee NewEmp(string id, string uid = "u1", string name = "张三")
        {
            return new hr_employee
            {
                id = id,
                uid = uid,
                name = name,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        private static Sys_Param NewParam(string id, string name, string parentType, string parentId = "P0")
        {
            return new Sys_Param
            {
                id = id,
                params_name = name,
                params_type = parentType,
                create_time = new DateTime(2024, 1, 1),
                params_order = 1
            };
        }

        // ============ #93 quickupdate ============

        [Fact]
        public async Task QuickUpdate_HappyPath_UpdatesFourTimeFields()
        {
            var calId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewCalendar(calId, "EMP1")).ExecuteAffrowsAsync();

            var rows = await _calRepo.QuickUpdateAsync(calId, "2025-03-01", "14:30", "2025-03-01", "16:00");

            Assert.Equal(1, rows);
            var cal = await _fsql.Select<My_Calendar>().Where(a => a.id == calId).FirstAsync();
            Assert.Equal("2025-03-01", cal.startDate);
            Assert.Equal("14:30", cal.startTime);
            Assert.Equal("2025-03-01", cal.endDate);
            Assert.Equal("16:00", cal.endTime);
        }

        [Fact]
        public async Task QuickUpdate_NonExistentId_ReturnsZero()
        {
            var rows = await _calRepo.QuickUpdateAsync(
                Guid.NewGuid().ToString(), "2025-01-01", "08:00", "2025-01-01", "12:00");
            Assert.Equal(0, rows);
        }

        [Fact]
        public async Task QuickUpdate_EmptyCalendarId_ReturnsZero()
        {
            var rows = await _calRepo.QuickUpdateAsync("", "2025-01-01", "08:00", "2025-01-01", "12:00");
            Assert.Equal(0, rows);
        }

        [Fact]
        public async Task QuickUpdate_DoesNotTouchTitleOrEmpId()
        {
            var calId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewCalendar(calId, "EMP_ORIGINAL", title: "原标题")).ExecuteAffrowsAsync();

            await _calRepo.QuickUpdateAsync(calId, "2025-02-01", "09:00", "2025-02-01", "10:00");

            var cal = await _fsql.Select<My_Calendar>().Where(a => a.id == calId).FirstAsync();
            Assert.Equal("原标题", cal.title);
            Assert.Equal("EMP_ORIGINAL", cal.emp_id);
        }

        // ============ #94 quickdel ============

        [Fact]
        public async Task QuickDel_RemovesCalendarRow()
        {
            var calId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewCalendar(calId, "EMP1")).ExecuteAffrowsAsync();

            var rows = await _calRepo.DeleteAsync(calId);

            Assert.Equal(1, rows);
            var after = await _fsql.Select<My_Calendar>().Where(a => a.id == calId).FirstAsync();
            Assert.Null(after);
        }

        [Fact]
        public async Task QuickDel_NonExistentId_ReturnsZero()
        {
            var rows = await _calRepo.DeleteAsync(Guid.NewGuid().ToString());
            Assert.Equal(0, rows);
        }

        // ============ #95 Today ============

        [Fact]
        public async Task Today_ReturnsOnlyCurrentUsersTodayEvents()
        {
            var today = DateTime.Today;
            var cal1 = new My_Calendar
            {
                id = Guid.NewGuid().ToString(),
                emp_id = "EMP1",
                title = "A-今日",
                startDate = today.ToString("yyyy-MM-dd"),
                endDate = today.ToString("yyyy-MM-dd"),
                StartDateTime = new DateTime(today.Year, today.Month, today.Day, 9, 0, 0),
                EndDateTime = new DateTime(today.Year, today.Month, today.Day, 11, 0, 0)
            };
            var cal2 = new My_Calendar
            {
                id = Guid.NewGuid().ToString(),
                emp_id = "EMP1",
                title = "B-今日",
                startDate = today.ToString("yyyy-MM-dd"),
                endDate = today.ToString("yyyy-MM-dd"),
                StartDateTime = new DateTime(today.Year, today.Month, today.Day, 8, 0, 0),
                EndDateTime = new DateTime(today.Year, today.Month, today.Day, 10, 0, 0)
            };
            var calOtherEmp = new My_Calendar
            {
                id = Guid.NewGuid().ToString(),
                emp_id = "EMP2",
                title = "C-他人",
                StartDateTime = new DateTime(today.Year, today.Month, today.Day, 15, 0, 0),
                EndDateTime = new DateTime(today.Year, today.Month, today.Day, 17, 0, 0)
            };
            var calYesterday = new My_Calendar
            {
                id = Guid.NewGuid().ToString(),
                emp_id = "EMP1",
                title = "D-昨日",
                StartDateTime = today.AddDays(-1).AddHours(14),
                EndDateTime = today.AddDays(-1).AddHours(16)
            };
            var calTomorrow = new My_Calendar
            {
                id = Guid.NewGuid().ToString(),
                emp_id = "EMP1",
                title = "E-明日",
                StartDateTime = today.AddDays(1).AddHours(9),
                EndDateTime = today.AddDays(1).AddHours(11)
            };

            await _fsql.Insert(new List<My_Calendar> { cal1, cal2, calOtherEmp, calYesterday, calTomorrow })
                .ExecuteAffrowsAsync();

            var list = await _calRepo.GetTodayAsync("EMP1");

            Assert.Equal(2, list.Count);
            var titles = list.Select(c => c.title).ToHashSet();
            Assert.Contains("A-今日", titles);
            Assert.Contains("B-今日", titles);
            // 按 StartDateTime 降序 → cal1(9:00) 应晚于 cal2(8:00)
            Assert.Equal("A-今日", list[0].title);
            Assert.Equal("B-今日", list[1].title);
        }

        [Fact]
        public async Task Today_EmptyEmpId_ReturnsEmpty()
        {
            var list = await _calRepo.GetTodayAsync("");
            Assert.Empty(list);
        }

        // ============ #98 newsremind ============

        [Fact]
        public async Task NewsRemind_DefaultLimit_ReturnsLatestFiveByCreateTimeDesc()
        {
            var baseTime = new DateTime(2025, 1, 1);
            var newses = new List<Message_news>();
            for (int i = 0; i < 8; i++)
            {
                newses.Add(NewNews(Guid.NewGuid().ToString(), baseTime.AddHours(i)));
            }
            await _fsql.Insert(newses).ExecuteAffrowsAsync();

            var list = await _newsRepo.NewsRemindAsync();

            Assert.Equal(5, list.Count);
            // 按 create_time desc 取最新 5 条
            Assert.Equal(newses[7].id, list[0].id);
            Assert.Equal(newses[3].id, list[4].id);
        }

        [Fact]
        public async Task NewsRemind_DoesNotFilterIsRead_IncludesBothReadAndUnread()
        {
            var baseTime = new DateTime(2025, 2, 1);
            await _fsql.Insert(new List<Message_news>
            {
                NewNews(Guid.NewGuid().ToString(), baseTime.AddHours(1), isRead: false),
                NewNews(Guid.NewGuid().ToString(), baseTime.AddHours(2), isRead: true),
                NewNews(Guid.NewGuid().ToString(), baseTime.AddHours(3), isRead: true)
            }).ExecuteAffrowsAsync();

            var list = await _newsRepo.NewsRemindAsync(10);

            Assert.Equal(3, list.Count);
            // 未标记为已读（对齐 A 侧 newsremind 不修改已读语义）
            Assert.Single(list.Where(n => !n.isRead));
        }

        [Fact]
        public async Task NewsRemind_DoesNotModifyIsReadOrReadTime()
        {
            var baseTime = new DateTime(2025, 3, 1);
            var news = NewNews(Guid.NewGuid().ToString(), baseTime, isRead: false);
            await _fsql.Insert(news).ExecuteAffrowsAsync();

            await _newsRepo.NewsRemindAsync(5);

            var after = await _fsql.Select<Message_news>().Where(a => a.id == news.id).FirstAsync();
            Assert.False(after.isRead);
            Assert.Null(after.read_time);
        }

        [Fact]
        public async Task NewsRemind_NoData_ReturnsEmptyList()
        {
            var list = await _newsRepo.NewsRemindAsync(5);
            Assert.Empty(list);
        }

        // ============ #110 Sys_role_emp.add ============

        [Fact]
        public async Task AddBatch_HappyPath_InsertsDistinctRows()
        {
            var roleId = Guid.NewGuid().ToString();
            var rows = await _roleEmpRepo.AddBatchAsync(roleId, new[] { "EMP1", "EMP2", "EMP3" });

            Assert.Equal(3, rows);
            var all = await _fsql.Select<Sys_role_emp>().Where(a => a.role_id == roleId).ToListAsync();
            Assert.Equal(3, all.Count);
            var ids = all.Select(e => e.emp_id).ToHashSet();
            Assert.Contains("EMP1", ids);
            Assert.Contains("EMP2", ids);
            Assert.Contains("EMP3", ids);
        }

        [Fact]
        public async Task AddBatch_DeduplicatesExistingBindings()
        {
            var roleId = Guid.NewGuid().ToString();
            // 预置一条：EMP1 已绑定该角色
            await _fsql.Insert(new Sys_role_emp
            {
                id = Guid.NewGuid().ToString(),
                role_id = roleId,
                emp_id = "EMP1"
            }).ExecuteAffrowsAsync();

            var rows = await _roleEmpRepo.AddBatchAsync(roleId, new[] { "EMP1", "EMP2" });

            Assert.Equal(1, rows); // 只新增 EMP2
            var all = await _fsql.Select<Sys_role_emp>().Where(a => a.role_id == roleId).ToListAsync();
            Assert.Equal(2, all.Count);
        }

        [Fact]
        public async Task AddBatch_EmptyInput_ReturnsZero()
        {
            var roleId = Guid.NewGuid().ToString();
            var rows = await _roleEmpRepo.AddBatchAsync(roleId, new List<string>());
            Assert.Equal(0, rows);
        }

        // ============ #111 Sys_role_emp.remove ============

        [Fact]
        public async Task RemoveBatch_HappyPath_RemovesMatchingRows()
        {
            var roleId = Guid.NewGuid().ToString();
            await _fsql.Insert(new List<Sys_role_emp>
            {
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = roleId, emp_id = "EMP1" },
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = roleId, emp_id = "EMP2" },
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = roleId, emp_id = "EMP3" }
            }).ExecuteAffrowsAsync();

            var rows = await _roleEmpRepo.RemoveBatchAsync(roleId, new[] { "EMP1", "EMP3" });

            Assert.Equal(2, rows);
            var left = await _fsql.Select<Sys_role_emp>().Where(a => a.role_id == roleId).ToListAsync(a => a.emp_id);
            Assert.Single(left);
            Assert.Equal("EMP2", left[0]);
        }

        [Fact]
        public async Task RemoveBatch_NoMatch_ReturnsZero()
        {
            var rows = await _roleEmpRepo.RemoveBatchAsync(
                Guid.NewGuid().ToString(), new[] { "NOSUCH" });
            Assert.Equal(0, rows);
        }

        // ============ #112 emplist ============

        [Fact]
        public async Task GetEmpIdsNotInRole_ExcludesRoleMembers()
        {
            var roleId = Guid.NewGuid().ToString();
            await _fsql.Insert(new List<Sys_role_emp>
            {
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = roleId, emp_id = "EMP1" }
            }).ExecuteAffrowsAsync();

            var notInRole = await _roleEmpRepo.GetEmpIdsNotInRoleAsync(roleId);

            Assert.Single(notInRole);
            Assert.Equal("EMP1", notInRole[0]); // 该角色下的员工（供上层做差集）
        }

        [Fact]
        public async Task GetEmpIdsNotInRole_EmptyRoleId_ReturnsEmpty()
        {
            var list = await _roleEmpRepo.GetEmpIdsNotInRoleAsync("");
            Assert.Empty(list);
        }

        // ============ #113 get（角色下员工）============

        [Fact]
        public async Task GetEmpIdsByRoleId_ReturnsMemberIds()
        {
            var roleId = Guid.NewGuid().ToString();
            await _fsql.Insert(new List<Sys_role_emp>
            {
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = roleId, emp_id = "EMP1" },
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = roleId, emp_id = "EMP2" },
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = "OTHER", emp_id = "EMP3" }
            }).ExecuteAffrowsAsync();

            var ids = await _roleEmpRepo.GetEmpIdsByRoleIdAsync(roleId);

            Assert.Equal(2, ids.Count);
            var set = ids.ToHashSet();
            Assert.Contains("EMP1", set);
            Assert.Contains("EMP2", set);
            Assert.DoesNotContain("EMP3", set);
        }

        // ============ #116 Sys_Param.validate ============

        [Fact]
        public async Task ValidateName_NoConflict_ReturnsTrue()
        {
            var rows = await _paramRepo.ValidateNameAsync("客户等级", "TYPE_A", "root");
            Assert.True(rows);
        }

        [Fact]
        public async Task ValidateName_DuplicateName_ReturnsFalse()
        {
            await _fsql.Insert(new List<Sys_Param>
            {
                NewParam(Guid.NewGuid().ToString(), "客户等级", "TYPE_A"),
                NewParam(Guid.NewGuid().ToString(), "另一个", "TYPE_A")
            }).ExecuteAffrowsAsync();

            var ok = await _paramRepo.ValidateNameAsync("客户等级", "TYPE_A", "root");

            Assert.False(ok);
        }

        [Fact]
        public async Task ValidateName_ExcludesSelfOnEdit()
        {
            var selfId = Guid.NewGuid().ToString();
            await _fsql.Insert(new List<Sys_Param>
            {
                NewParam(selfId, "客户等级", "TYPE_A"),
                NewParam(Guid.NewGuid().ToString(), "另一个", "TYPE_A")
            }).ExecuteAffrowsAsync();

            // 编辑场景：排除自身 → 只有 1 条命中，排除后为 0，返回 true（唯一）
            var ok = await _paramRepo.ValidateNameAsync("客户等级", "TYPE_A", selfId);
            Assert.True(ok);
        }

        [Fact]
        public async Task ValidateName_DifferentParentType_NotConflict()
        {
            await _fsql.Insert(new Sys_Param
            {
                id = Guid.NewGuid().ToString(),
                params_name = "客户等级",
                params_type = "TYPE_A",
                create_time = DateTime.Now
            }).ExecuteAffrowsAsync();

            // 同名不同父级 → 唯一
            var ok = await _paramRepo.ValidateNameAsync("客户等级", "TYPE_B", "root");
            Assert.True(ok);
        }

        [Fact]
        public async Task ValidateName_EmptyParamName_ReturnsFalse()
        {
            var ok = await _paramRepo.ValidateNameAsync("", "TYPE_A", "root");
            Assert.False(ok);
        }

        // ============ Controller 级测试（Moq 桥接）============

        private MyCalendarController CreateCalendarController(
            string userId = "TEST_USER", My_Calendar existingOwner = null)
        {
            var calSvc = new Mock<IMy_CalendarService>();
            // 归属校验：模拟 GridAsync(id) 返回一条数据
            calSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<My_Calendar, bool>>>()))
                .ReturnsAsync(new XHD.Core.Common.XHDData<My_Calendar>
                {
                    data = existingOwner != null
                        ? new List<My_Calendar> { existingOwner }
                        : new List<My_Calendar>(),
                    count = existingOwner != null ? 1 : 0
                });
            calSvc.Setup(s => s.QuickUpdateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(1);
            calSvc.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(1);

            var ctrl = new MyCalendarController(calSvc.Object);

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

        [Fact]
        public async Task QuickUpdate_Controller_OwnCalendar_ReturnsSuccess()
        {
            var cal = new My_Calendar
            {
                id = Guid.NewGuid().ToString(),
                emp_id = "TEST_USER",
                title = "我的日程"
            };
            var ctrl = CreateCalendarController(userId: "TEST_USER", existingOwner: cal);

            var json = await ctrl.QuickUpdate(cal.id, "2025-03-01", "2025-03-01", "14:00", "16:00");
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Contains("更新成功", (string)obj["msg"]!);
        }

        [Fact]
        public async Task QuickUpdate_Controller_OthersCalendar_ReturnsNoPermission()
        {
            var cal = new My_Calendar
            {
                id = Guid.NewGuid().ToString(),
                emp_id = "OTHER_USER", // 别人
                title = "别人的日程"
            };
            var ctrl = CreateCalendarController(userId: "TEST_USER", existingOwner: cal);

            var json = await ctrl.QuickUpdate(cal.id, "2025-03-01", "2025-03-01", "14:00", "16:00");
            var obj = JObject.Parse(json);
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限", (string)obj["msg"]!);
        }

        [Fact]
        public async Task QuickDel_Controller_OwnCalendar_ReturnsSuccess()
        {
            var cal = new My_Calendar
            {
                id = Guid.NewGuid().ToString(),
                emp_id = "TEST_USER"
            };
            var ctrl = CreateCalendarController(userId: "TEST_USER", existingOwner: cal);

            var json = await ctrl.QuickDel(cal.id);
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
        }

        [Fact]
        public async Task QuickDel_Controller_OthersCalendar_ReturnsNoPermission()
        {
            var cal = new My_Calendar
            {
                id = Guid.NewGuid().ToString(),
                emp_id = "OTHER_USER"
            };
            var ctrl = CreateCalendarController(userId: "TEST_USER", existingOwner: cal);

            var json = await ctrl.QuickDel(cal.id);
            var obj = JObject.Parse(json);
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限", (string)obj["msg"]!);
        }

        [Fact]
        public async Task QuickUpdate_Controller_EmptyCalendarId_ReturnsError()
        {
            var ctrl = CreateCalendarController();
            var json = await ctrl.QuickUpdate("", "2025-01-01", "2025-01-01", "09:00", "10:00");
            var obj = JObject.Parse(json);
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("参数错误", (string)obj["msg"]!);
        }

        [Fact]
        public async Task QuickDel_Controller_EmptyCalendarId_ReturnsError()
        {
            var ctrl = CreateCalendarController();
            var json = await ctrl.QuickDel("");
            var obj = JObject.Parse(json);
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("参数错误", (string)obj["msg"]!);
        }

        // ============ #112/#113 集成测试（Repository 层）============

        [Fact]
        public async Task SysRoleEmp_RoleMembership_QueriesCorrectly_Integration()
        {
            var roleId = Guid.NewGuid().ToString();
            await _fsql.Insert(new List<Sys_role_emp>
            {
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = roleId, emp_id = "EMP1" },
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = roleId, emp_id = "EMP2" },
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = "OTHER_ROLE", emp_id = "EMP3" }
            }).ExecuteAffrowsAsync();

            // 按角色查员工（#113 语义）
            var roleMemberIds = await _roleEmpRepo.GetEmpIdsByRoleIdAsync(roleId);
            Assert.Equal(2, roleMemberIds.Count);

            // 按角色查"外部"员工集合作为差集来源（#112 语义）
            var notInRoleIds = await _roleEmpRepo.GetEmpIdsNotInRoleAsync(roleId);
            Assert.Equal(2, notInRoleIds.Count);
            // 两种查询返回同一集合（本 Repository 层实现）
            Assert.Equal(
                roleMemberIds.OrderBy(s => s),
                notInRoleIds.OrderBy(s => s));
        }

        [Fact]
        public async Task SysRoleEmp_AddThenRemove_Integration_ReturnsToEmpty()
        {
            var roleId = Guid.NewGuid().ToString();
            await _roleEmpRepo.AddBatchAsync(roleId, new[] { "EMP1", "EMP2" });

            var before = await _roleEmpRepo.GetEmpIdsByRoleIdAsync(roleId);
            Assert.Equal(2, before.Count);

            var rows = await _roleEmpRepo.RemoveBatchAsync(roleId, new[] { "EMP1", "EMP2" });
            Assert.Equal(2, rows);

            var after = await _roleEmpRepo.GetEmpIdsByRoleIdAsync(roleId);
            Assert.Empty(after);
        }

        [Fact]
        public async Task SysRoleEmp_AddBatch_TrimAndDeduplicatesInput()
        {
            var roleId = Guid.NewGuid().ToString();
            // 输入含空白、重复、空字符串
            var rows = await _roleEmpRepo.AddBatchAsync(roleId, new[] { " EMP1 ", "EMP1", "EMP2", "", " " });

            Assert.Equal(2, rows);
            var all = await _fsql.Select<Sys_role_emp>().Where(a => a.role_id == roleId).ToListAsync(a => a.emp_id);
            Assert.Equal(2, all.Count);
            Assert.Contains("EMP1", all);
            Assert.Contains("EMP2", all);
        }

        [Fact]
        public async Task SysRoleEmp_RemoveBatch_TrimsWhitespace()
        {
            var roleId = Guid.NewGuid().ToString();
            await _fsql.Insert(new List<Sys_role_emp>
            {
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = roleId, emp_id = "EMP1" },
                new Sys_role_emp { id = Guid.NewGuid().ToString(), role_id = roleId, emp_id = "EMP2" }
            }).ExecuteAffrowsAsync();

            var rows = await _roleEmpRepo.RemoveBatchAsync(roleId, new[] { " EMP1 " });
            Assert.Equal(1, rows);
        }
    }
}
