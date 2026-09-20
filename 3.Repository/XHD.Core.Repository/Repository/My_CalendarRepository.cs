using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Repository
{
    public class My_CalendarRepository : BaseRepository<My_Calendar>, IMy_CalendarRepository
    {
        public My_CalendarRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 更新
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async new Task<int> UpdateAsync(My_Calendar model)
        {
            var result = await _fsql.Update<My_Calendar>()
                .SetSource(model)
                .IgnoreColumns(a => new { a.emp_id })
                .ExecuteAffrowsAsync();

            return result;
        }

        /// <summary>
        /// Sprint 6 Wave 1 #93：日历快速更新（仅改起止时间四字段）。
        /// 独立 Repository 方法，绕开 BaseService.UpdateAsync 的 IgnoreColumns 陷阱。
        /// </summary>
        public async Task<int> QuickUpdateAsync(string calendarId, string startDate, string startTime,
            string endDate, string endTime)
        {
            if (string.IsNullOrWhiteSpace(calendarId))
            {
                return 0;
            }

            var result = await _fsql.Update<My_Calendar>()
                .Set(a => a.startDate, startDate ?? string.Empty)
                .Set(a => a.startTime, startTime ?? string.Empty)
                .Set(a => a.endDate, endDate ?? string.Empty)
                .Set(a => a.endTime, endTime ?? string.Empty)
                .Where(a => a.id == calendarId)
                .ExecuteAffrowsAsync();

            return result;
        }

        /// <summary>
        /// Sprint 6 Wave 1 #95：当日日程查询。
        /// 时间交叉判定：StartDateTime &lt;= 今日 23:59:50 AND EndDateTime &gt;= 今日 00:00:00。
        /// 按 StartDateTime 降序返回。
        /// </summary>
        public async Task<List<My_Calendar>> GetTodayAsync(string empId)
        {
            if (string.IsNullOrWhiteSpace(empId))
            {
                return new List<My_Calendar>();
            }

            var todayStart = DateTime.Today;
            // 用 A 侧口径：今日 23:59:50（留 9 秒缓冲，避免秒级时间漂移导致漏数据）
            var todayEnd = new DateTime(todayStart.Year, todayStart.Month, todayStart.Day, 23, 59, 50);

            return await _fsql.Select<My_Calendar>()
                .Where(a => a.emp_id == empId)
                .Where(a => a.StartDateTime <= todayEnd)
                .Where(a => a.EndDateTime >= todayStart)
                .OrderByDescending(a => a.StartDateTime)
                .ToListAsync();
        }
    }
}
