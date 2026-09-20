using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using XHD.Core.Models;
using XHD.Core.Common;
using XHD.Core.Common.Excel;

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
        /// <param name="typeIds">客户类型 ID 白名单；null 或空集合表示不限类型</param>
        Task<JArray> FunnelAsync(int? year, List<string> typeIds = null);

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

        /// <summary>
        /// Sprint 3 Wave 2 #13：客户预删除（软删），委托 Repository 执行
        /// </summary>
        /// <param name="id">客户 ID</param>
        /// <param name="operatorId">删除人 ID</param>
        /// <returns>是否成功</returns>
        Task<bool> AdvanceDeleteAsync(string id, string operatorId);

        /// <summary>
        /// Sprint 4 Wave 1b #01：客户重取（从回收站恢复），委托 Repository 执行。
        /// 与 AdvanceDeleteAsync 语义镜像、方向相反。
        /// </summary>
        /// <param name="id">客户 ID</param>
        /// <returns>是否成功</returns>
        Task<bool> RegainAsync(string id);

        /// <summary>
        /// Sprint 4 Wave 1b #02：移动端客户更新（简化字段子集），委托 Repository 执行。
        /// 仅更新 15 个业务字段，管理字段保持不变。
        /// </summary>
        /// <param name="model">移动端提交的客户模型（id 必填）</param>
        /// <returns>是否成功</returns>
        Task<bool> UpdateAppAsync(CRM_Customer model);

        /// <summary>
        /// Sprint 4 Wave 3 #04：批量导入客户（普通用户）。
        /// 逐条 cus_name 去重；已存在则跳过。
        /// </summary>
        /// <param name="models">待插入的客户列表（已构建完成，含校验结果）</param>
        /// <returns>批量导入结果</returns>
        Task<ExcelImportResult> ImportAsync(List<CRM_Customer> models);

        /// <summary>
        /// Sprint 4 Wave 3 #06：管理员批量 upsert 客户（按 cus_name 覆盖）。
        /// 已存在 → 覆盖业务字段；不存在 → 新增。
        /// </summary>
        /// <param name="models">待 upsert 的客户列表</param>
        /// <returns>批量导入结果</returns>
        Task<ExcelImportResult> AdminImportAsync(List<CRM_Customer> models);
    }
}
