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
internal class CRM_followService : BaseService<CRM_follow>, ICRM_followService
    {
        ICRM_followRepository followRepository;
        public CRM_followService(ICRM_followRepository repository)
        {
            _irepository = repository;
            followRepository=repository;
        }

        public async Task<JArray> ReportYear(Expression<Func<CRM_follow, bool>> expWhere)
        { 
            return await followRepository.ReportYear(expWhere);
        }

        public async Task<JArray> ReportsYearAsync(string items, int year, Expression<Func<CRM_follow, bool>> expWhere)
        {
            return await followRepository.ReportsYearAsync(items, year, expWhere);
        }

        public async Task<JArray> ComparedFollowAsync(int year1, int month1, int year2, int month2)
        {
            return await followRepository.ComparedFollowAsync(year1, month1, year2, month2);
        }

        public async Task<JArray> ComparedEmpCusFollowAsync(int year1, int month1, int year2, int month2, List<string> empIds)
        {
            return await followRepository.ComparedEmpCusFollowAsync(year1, month1, year2, month2, empIds);
        }

        public async Task<JArray> ReportMonthEmpFollowAsync(DateTime start, DateTime end, List<string> empIds)
        {
            return await followRepository.ReportMonthEmpFollowAsync(start, end, empIds);
        }

        public async Task<JArray> ReportEmpFollowAsync(int year, List<string> empIds)
        {
            return await followRepository.ReportEmpFollowAsync(year, empIds);
        }
    }
}
