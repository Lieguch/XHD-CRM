using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;


namespace XHD.Core.IRepository
{
    public interface IMy_CalendarRepository : IXHDBaseRepository<My_Calendar>
    {
        /// <summary>
        /// Sprint 6 Wave 1 #93：日历快速更新（拖拽改时间场景）。
        /// 对应 A 侧 Server.Personal_Calendar.quickupdate → DAL.Personal_Calendar.quickUpdate。
        /// 参数化 UPDATE：只写 startDate / startTime / endDate / endTime 四字段，
        /// 不触碰 title / description / emp_id / allDay 等；避免 BaseService.UpdateAsync
        /// 的 IgnoreColumns 陷阱（P6/P12）。
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
        /// 对应 A 侧 Server.Personal_Calendar.Today：
        ///   StartTime &lt;= 今日 23:59:50 AND EndTime &gt;= 今日 00:00:00 AND emp_id=@empId
        /// 按 StartDateTime 降序返回。
        /// </summary>
        /// <param name="empId">员工 id</param>
        /// <returns>当日日程列表</returns>
        Task<List<My_Calendar>> GetTodayAsync(string empId);
    }
}
