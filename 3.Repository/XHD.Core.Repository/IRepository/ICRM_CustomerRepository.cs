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
    public interface ICRM_CustomerRepository: IXHDBaseRepository<CRM_Customer>
    {
        Task<JArray> ReportYear(Expression<Func<CRM_Customer, bool>> expWhere);

        Task<JArray> ReportIndustry(Expression<Func<CRM_Customer, bool>> expWhere);

        Task<JArray> ReportType(Expression<Func<CRM_Customer, bool>> expWhere);

        Task<JArray> ReportLevel(Expression<Func<CRM_Customer, bool>> expWhere);

        Task<JArray> ReportSource(Expression<Func<CRM_Customer, bool>> expWhere);

        Task<JArray> ReportCity(Expression<Func<CRM_Customer, bool>> expWhere);

        Task<JArray> ReportProvinces(Expression<Func<CRM_Customer, bool>> expWhere);

        Task<bool> LastFollow(string id);

        /// <summary>
        /// 认领客户（底层更新）：将指定客户 state 更新为 targetState，并设置 emp_id
        /// </summary>
        Task<bool> ClaimlistAsync(List<string> ids, int targetState, string empId);

        /// <summary>
        /// 放弃客户（底层更新）：将指定客户 state 更新为 targetState
        /// </summary>
        Task<bool> AbanDonAsync(List<string> ids, int targetState);

        /// <summary>
        /// 客户转化漏斗：按客户类型（cus_type_id 关联 Sys_Param）统计年度客户数
        /// </summary>
        /// <param name="year">年份过滤，null 表示不限年份</param>
        Task<JArray> FunnelAsync(int? year);

        /// <summary>
        /// 员工年度客户新增报表（Wave 3b #13）：按 create_id × 12 个月分组计数
        /// </summary>
        /// <param name="year">统计年份</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        Task<JArray> ReportEmpCusAsync(int year, List<string> empIds);

        /// <summary>
        /// 员工月度客户新增报表（Wave 3b #14）：按 start..end 时间区间分组计数
        /// </summary>
        /// <param name="start">起始时间（含）</param>
        /// <param name="end">结束时间（含）</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        Task<JArray> ReportMonthEmpCusAsync(DateTime? start, DateTime? end, List<string> empIds);

        /// <summary>
        /// 员工维度双月客户新增对比（Wave 3b #15）：
        /// 输出每员工 startMonth_count、endMonth_count 及差值
        /// </summary>
        /// <param name="startMonth">起始月份（1-12）</param>
        /// <param name="endMonth">结束月份（1-12）</param>
        /// <param name="year">统计年份</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        Task<JArray> ComparedEmpCusAddAsync(int? startMonth, int? endMonth, int year, List<string> empIds);
    }
}
