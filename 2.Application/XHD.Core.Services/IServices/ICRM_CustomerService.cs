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
    public interface ICRM_CustomerService:IBaseService<CRM_Customer>
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
        /// 认领客户（service 层薄封装，targetState=0=正常 由 service 硬编码）
        /// </summary>
        Task<bool> Claimlist(List<string> ids, string empId);

        /// <summary>
        /// 放弃客户（service 层薄封装，targetState=1=回公共池 由 service 硬编码）
        /// </summary>
        Task<bool> AbanDon(List<string> ids);

        /// <summary>
        /// 客户转化漏斗：按客户类型统计年度客户数
        /// </summary>
        /// <param name="year">年份过滤，null 表示不限年份</param>
        Task<JArray> FunnelAsync(int? year);

        /// <summary>
        /// 员工年度客户新增报表（Wave 3b #13）：委托 Repository 执行
        /// </summary>
        /// <param name="year">统计年份</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        Task<JArray> ReportEmpCusAsync(int year, List<string> empIds);

        /// <summary>
        /// 员工月度客户新增报表（Wave 3b #14）：委托 Repository 执行
        /// </summary>
        /// <param name="start">起始时间（含）</param>
        /// <param name="end">结束时间（含）</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        Task<JArray> ReportMonthEmpCusAsync(DateTime? start, DateTime? end, List<string> empIds);

        /// <summary>
        /// 员工维度双月客户新增对比（Wave 3b #15）：委托 Repository 执行
        /// </summary>
        /// <param name="startMonth">起始月份（1-12）</param>
        /// <param name="endMonth">结束月份（1-12）</param>
        /// <param name="year">统计年份</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        Task<JArray> ComparedEmpCusAddAsync(int? startMonth, int? endMonth, int year, List<string> empIds);

        /// <summary>
        /// Sprint 3 #12：客户总数 KPI（委托 Repository 执行）
        /// </summary>
        /// <param name="expWhere">完整过滤表达式（含 isDelete=0）</param>
        /// <returns>符合条件的客户总数</returns>
        Task<int> CountAsync(Expression<Func<CRM_Customer, bool>> expWhere);
    }
}
