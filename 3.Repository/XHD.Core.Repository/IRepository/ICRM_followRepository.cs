using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XHD.Core.IRepository
{
    public interface ICRM_followRepository: IXHDBaseRepository<CRM_follow>
    {
        /// <summary>
        /// Sprint 1 简化版：仅按月分组计数
        /// </summary>
        Task<JArray> ReportYear(Expression<Func<CRM_follow, bool>> expWhere);

        /// <summary>
        /// Sprint 3 #01：跟进年度趋势报表。
        /// 按 items（Follow_Type 或 Follow_aim）分组，输出 12 个月计数矩阵。
        /// </summary>
        /// <param name="items">分组维度：Follow_Type / Follow_aim（Contact_Type 留 Sprint 4）</param>
        /// <param name="year">统计年份</param>
        /// <param name="expWhere">附加过滤条件（可为 null）</param>
        Task<JArray> ReportsYearAsync(string items, int year, Expression<Func<CRM_follow, bool>> expWhere);

        /// <summary>
        /// Sprint 3 #02：跟进双月对比报表（按跟进类型）。
        /// 输出每类型在两个月的跟进数（dt1、dt2）。
        /// </summary>
        Task<JArray> ComparedFollowAsync(int year1, int month1, int year2, int month2);

        /// <summary>
        /// Sprint 3 #03：员工维度双月跟进对比。
        /// 输出每员工在两个月的跟进数。
        /// </summary>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        Task<JArray> ComparedEmpCusFollowAsync(int year1, int month1, int year2, int month2, List<string> empIds);

        /// <summary>
        /// Sprint 3 #04：员工月度跟进矩阵（跨月区间）。
        /// 输出每员工 m1..m12 各月跟进次数。
        /// </summary>
        /// <param name="start">起始时间（含）</param>
        /// <param name="end">结束时间（含）</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        Task<JArray> ReportMonthEmpFollowAsync(DateTime start, DateTime end, List<string> empIds);

        /// <summary>
        /// Sprint 3 #05：员工年度跟进矩阵。
        /// 输出每员工 m1..m12 各月跟进次数。
        /// </summary>
        /// <param name="year">统计年份</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        Task<JArray> ReportEmpFollowAsync(int year, List<string> empIds);
    }
}
