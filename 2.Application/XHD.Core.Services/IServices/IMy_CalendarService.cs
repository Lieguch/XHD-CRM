using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq.Expressions;

namespace XHD.Core.IServices
{
    public interface IMy_CalendarService : IBaseService<My_Calendar>
    {
        /// <summary>
        /// Sprint 6 Wave 1 #93：日历快速更新（拖拽改时间）。
        /// 委托 Repository 执行；Controller 负责 emp_id 归属校验。
        /// </summary>
        /// <param name="calendarId">日程 id</param>
        /// <param name="startDate">开始日期（yyyy-MM-dd）</param>
        /// <param name="startTime">开始时间（HH:mm）</param>
        /// <param name="endDate">结束日期（yyyy-MM-dd）</param>
        /// <param name="endTime">结束时间（HH:mm）</param>
        /// <returns>受影响行数</returns>
        Task<int> QuickUpdateAsync(string calendarId, string startDate, string startTime,
            string endDate, string endTime);

        /// <summary>
        /// Sprint 6 Wave 1 #95：当日日程查询。
        /// </summary>
        /// <param name="empId">员工 id</param>
        /// <returns>当日日程列表</returns>
        Task<List<My_Calendar>> GetTodayAsync(string empId);
    }
}
