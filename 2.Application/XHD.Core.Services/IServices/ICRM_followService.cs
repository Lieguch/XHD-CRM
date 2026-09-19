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
    public interface ICRM_followService:IBaseService<CRM_follow>
    {
        Task<JArray> ReportYear(Expression<Func<CRM_follow, bool>> expWhere);

        /// <summary>
        /// Sprint 3 #01：跟进年度趋势报表（按 items 分组 + 12 月矩阵）
        /// </summary>
        Task<JArray> ReportsYearAsync(string items, int year, Expression<Func<CRM_follow, bool>> expWhere);

        /// <summary>
        /// Sprint 3 #02：跟进双月对比（按跟进类型）
        /// </summary>
        Task<JArray> ComparedFollowAsync(int year1, int month1, int year2, int month2);

        /// <summary>
        /// Sprint 3 #03：员工维度双月跟进对比
        /// </summary>
        Task<JArray> ComparedEmpCusFollowAsync(int year1, int month1, int year2, int month2, List<string> empIds);

        /// <summary>
        /// Sprint 3 #04：员工月度跟进矩阵（跨月区间）
        /// </summary>
        Task<JArray> ReportMonthEmpFollowAsync(DateTime start, DateTime end, List<string> empIds);

        /// <summary>
        /// Sprint 3 #05：员工年度跟进矩阵
        /// </summary>
        Task<JArray> ReportEmpFollowAsync(int year, List<string> empIds);
    }
}
