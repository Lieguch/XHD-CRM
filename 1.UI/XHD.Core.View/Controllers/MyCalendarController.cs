using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;
using XHD.Core.Common;
using XHD.Core.IRepository;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.View.Configs;

namespace XHD.Core.View.Controllers
{
    public class MyCalendarController : Controller
    {
        private readonly IMy_CalendarService _service;
        public MyCalendarController(IMy_CalendarService service)
        {
            _service = service;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<string> Grid()
        {
            var emp_id = User.FindFirst(ClaimTypes.Sid).Value;

            Expression<Func<My_Calendar, bool>> exp = a => a.emp_id == emp_id;

            var result = await _service.GridAsync(exp);

            return result.ToString();
        }

        public async Task<string> Save()
        {
            string requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
            var model = JsonConvert.DeserializeObject<My_Calendar>(requestBody);

            // 1. 验证必要字段
            if (string.IsNullOrWhiteSpace(model.title))
            {
                return XHDResult.Error("任务标题不能为空").ToString();
            }

            if (string.IsNullOrWhiteSpace(model.startDate) || string.IsNullOrWhiteSpace(model.endDate))
            {
                return XHDResult.Error("开始日期或结束日期不能为空").ToString();
            }

            // 2. 根据是否全天任务，构建 StartDateTime 和 EndDateTime
            try
            {
                if (model.allDay == 1) // 全天任务
                {
                    // 开始时间：当天 00:00:00
                    var startDateOnly = DateTime.ParseExact(model.startDate, "yyyy-MM-dd", null);
                    model.StartDateTime = startDateOnly.Date;

                    // 结束时间：结束日期的 23:59:59
                    var endDateOnly = DateTime.ParseExact(model.endDate, "yyyy-MM-dd", null);
                    model.EndDateTime = endDateOnly.Date.AddDays(1).AddSeconds(-1); // 当天 23:59:59
                }
                else // 非全天任务，需要拼接时间
                {
                    // 如果时间为空，给默认值（可选，根据业务决定）
                    var startTime = string.IsNullOrWhiteSpace(model.startTime) ? "00:00" : model.startTime;
                    var endTime = string.IsNullOrWhiteSpace(model.endTime) ? "23:59" : model.endTime;

                    var startDateTimeStr = $"{model.startDate} {startTime}";
                    var endDateTimeStr = $"{model.endDate} {endTime}";

                    model.StartDateTime = DateTime.ParseExact(startDateTimeStr, "yyyy-MM-dd HH:mm", null);
                    model.EndDateTime = DateTime.ParseExact(endDateTimeStr, "yyyy-MM-dd HH:mm", null);
                }
            }
            catch (FormatException)
            {
                return XHDResult.Error("日期或时间格式错误，请使用 yyyy-MM-dd 和 HH:mm 格式").ToString();
            }

            // 3. 执行保存
            int result = 0;
            if (string.IsNullOrWhiteSpace(model.id))
            {
                // 新增：生成主键
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.emp_id = User.FindFirst(ClaimTypes.Sid).Value;
                result = await _service.AddAsync(model);
            }
            else
            {
                // 更新：先检查数据是否存在
                Expression<Func<My_Calendar, bool>> exp = a => a.id == model.id;

                var calendardata = await _service.GridAsync(exp);


                if (calendardata.count == 0)
                {
                    return XHDResult.Error("找不到数据").ToString();
                }

                result = await _service.UpdateAsync(model);
            }

            if (result == 0)
            {
                return XHDResult.Error("操作失败，系统错误").ToString();
            }

            return XHDResult.Success(model.id).ToString();

        }

        public async Task<string> Delete(string id)
        {
            var result = 0;

            result = await _service.DeleteAsync(id);


            return XHDResult.Success().ToString();
        }

        /// <summary>
        /// Sprint 4 Wave 2 #13：日历快速新增（拖拽创建场景）。
        /// 对应 A 侧 Server.Personal_Calendar.quickadd：只接收最少字段（标题 + 起止时间），
        /// 自动补齐 emp_id / id，走现有 My_CalendarService.AddAsync 落库。
        /// 与 Save() 不同，此处不校验 endDate 必填（A 侧同样允许空），
        /// 也不做全天任务的 00:00-23:59:59 强制展开。
        /// </summary>
        /// <param name="model">日程实体（含 title / startDate / startTime / endDate / endTime / description / color / allDay）</param>
        /// <returns>标准 XHDResult 字符串，成功时 data[0].id 承载新日程 ID</returns>
        [HttpPost("quickadd")]
        public async Task<string> QuickAdd(My_Calendar model)
        {
            if (model == null)
            {
                return XHDResult.Error("请求体无效").ToString();
            }

            if (string.IsNullOrWhiteSpace(model.title))
            {
                return XHDResult.Error("日程标题不能为空").ToString();
            }

            if (string.IsNullOrWhiteSpace(model.startDate))
            {
                return XHDResult.Error("开始日期不能为空").ToString();
            }

            try
            {
                // 校验 startDate 格式（yyyy-MM-dd）；startTime 可选，为空则视为 00:00
                DateTime.ParseExact(model.startDate.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return XHDResult.Error("开始日期格式错误，请使用 yyyy-MM-dd").ToString();
            }

            // 补齐 endDate：若为空则默认等于 startDate（单天日程）
            if (string.IsNullOrWhiteSpace(model.endDate))
            {
                model.endDate = model.startDate;
            }

            // 补齐主键与员工归属
            model.id = UUIDNext.Uuid.NewSequential().ToString();
            model.emp_id = User.FindFirst(ClaimTypes.Sid).Value;

            int result = await _service.AddAsync(model);
            if (result <= 0)
            {
                return XHDResult.Error("操作失败，系统错误").ToString();
            }

            return XHDResult.Success(model.id).ToString();
        }

        /// <summary>
        /// Sprint 6 Wave 1 #93：日历快速更新（拖拽改时间）。
        /// 对应 A 侧 Server.Personal_Calendar.quickupdate。
        /// 只做两件事：参数校验 + 当前用户归属校验 + 起止时间四字段更新。
        /// 不触碰 title / description / allDay，避免"改个时间把标题清掉"。
        /// </summary>
        /// <param name="calendarId">日程 id</param>
        /// <param name="calendarStartTime">开始日期（yyyy-MM-dd）</param>
        /// <param name="calendarEndTime">结束日期（yyyy-MM-dd）</param>
        /// <param name="calendarStartTimeHHmm">开始时间（HH:mm，可选）</param>
        /// <param name="calendarEndTimeHHmm">结束时间（HH:mm，可选）</param>
        /// <returns>标准 XHDResult 字符串</returns>
        [HttpPost("quickupdate")]
        public async Task<string> QuickUpdate(
            string calendarId,
            string calendarStartTime,
            string calendarEndTime,
            string calendarStartTimeHHmm = null,
            string calendarEndTimeHHmm = null)
        {
            if (string.IsNullOrWhiteSpace(calendarId))
            {
                return XHDResult.Error("参数错误！").ToString();
            }

            var userId = User.FindFirst(ClaimTypes.Sid)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            // 归属校验：先查一次目标日程是否属于当前用户
            var existing = await _service.GridAsync(a => a.id == calendarId);
            if (existing == null || existing.data == null || existing.data.Count == 0)
            {
                return XHDResult.Error("系统错误，无数据！").ToString();
            }
            if (existing.data[0].emp_id != userId)
            {
                return XHDResult.Error("无权限修改他人日程").ToString();
            }

            // 参数化更新起止时间四字段
            var rows = await _service.QuickUpdateAsync(
                calendarId,
                calendarStartTime ?? string.Empty,
                calendarStartTimeHHmm ?? string.Empty,
                calendarEndTime ?? string.Empty,
                calendarEndTimeHHmm ?? string.Empty);

            if (rows <= 0)
            {
                return XHDResult.Error("更新失败").ToString();
            }

            return XHDResult.Success("更新成功").ToString();
        }

        /// <summary>
        /// Sprint 6 Wave 1 #94：日历快速删除。
        /// 对应 A 侧 Server.Personal_Calendar.quickdel(string calendarId)。
        /// 归属校验：仅允许删除当前用户的日程。
        /// </summary>
        /// <param name="calendarId">日程 id</param>
        /// <returns>标准 XHDResult 字符串</returns>
        [HttpPost("quickdel")]
        public async Task<string> QuickDel(string calendarId)
        {
            if (string.IsNullOrWhiteSpace(calendarId))
            {
                return XHDResult.Error("参数错误！").ToString();
            }

            var userId = User.FindFirst(ClaimTypes.Sid)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var existing = await _service.GridAsync(a => a.id == calendarId);
            if (existing == null || existing.data == null || existing.data.Count == 0)
            {
                return XHDResult.Error("系统错误，无数据！").ToString();
            }
            if (existing.data[0].emp_id != userId)
            {
                return XHDResult.Error("无权限删除他人日程").ToString();
            }

            var rows = await _service.DeleteAsync(calendarId);
            if (rows <= 0)
            {
                return XHDResult.Error("删除失败").ToString();
            }

            return XHDResult.Success("删除成功").ToString();
        }

        /// <summary>
        /// Sprint 6 Wave 1 #95：当日日程查询。
        /// 对应 A 侧 Server.Personal_Calendar.Today：
        ///   StartTime &lt;= 今日 23:59:50 AND EndTime &gt;= 今日 00:00:00 AND emp_id=当前用户。
        /// 按 StartDateTime 降序返回。
        /// </summary>
        /// <returns>标准 XHDResult 字符串，data 承载当日日程数组</returns>
        [HttpGet("Today")]
        public async Task<string> Today()
        {
            var userId = User.FindFirst(ClaimTypes.Sid)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var list = await _service.GetTodayAsync(userId);

            var arr = new JArray();
            if (list != null)
            {
                foreach (var item in list)
                {
                    arr.Add(JObject.FromObject(item));
                }
            }

            return XHDResult.Success(arr).ToString();
        }
    }
}
