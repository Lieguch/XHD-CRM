using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;
using System.Threading.Tasks;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

namespace XHD.Core.Services
{
    internal class My_CalendarService : BaseService<My_Calendar>, IMy_CalendarService
    {
        // 与 Message_newsService 同模式：BaseService._irepository 是基类接口引用，
        // 拿不到具体接口上的扩展方法，因此额外持有一个具体接口引用。
        private readonly IMy_CalendarRepository _irepositoryBase;

        public My_CalendarService(IMy_CalendarRepository repository)
        {
            _irepository = repository;
            _irepositoryBase = repository;
        }

        /// <summary>
        /// Sprint 6 Wave 1 #93：日历快速更新，Service 层薄封装。
        /// </summary>
        public async Task<int> QuickUpdateAsync(string calendarId, string startDate, string startTime,
            string endDate, string endTime)
        {
            return await _irepositoryBase.QuickUpdateAsync(
                calendarId, startDate, startTime, endDate, endTime);
        }

        /// <summary>
        /// Sprint 6 Wave 1 #95：当日日程查询，Service 层薄封装。
        /// </summary>
        public async Task<List<My_Calendar>> GetTodayAsync(string empId)
        {
            return await _irepositoryBase.GetTodayAsync(empId);
        }
    }
}
