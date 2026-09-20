using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;
using XHD.Core.Common.Excel;

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
        /// <param name="typeIds">客户类型 ID 白名单；null 或空集合表示不限类型</param>
        Task<JArray> FunnelAsync(int? year, List<string> typeIds = null);

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

        /// <summary>
        /// Sprint 3 #12：客户总数 KPI。
        /// 按外部传入的动态过滤条件统计客户数（含 isDelete=0 基础过滤）。
        /// </summary>
        /// <param name="expWhere">完整过滤表达式（含 isDelete=0 与其它业务过滤）</param>
        /// <returns>符合条件的客户总数</returns>
        Task<int> CountAsync(Expression<Func<CRM_Customer, bool>> expWhere);

        /// <summary>
        /// Sprint 3 Wave 2 #13：客户预删除（软删）。
        /// 将指定客户的 isDelete 置 1、写入 Delete_time 与 Delete_id。
        /// </summary>
        /// <param name="id">客户 ID</param>
        /// <param name="operatorId">删除人 ID</param>
        /// <returns>是否成功（受影响行数 &gt; 0）</returns>
        Task<bool> AdvanceDeleteAsync(string id, string operatorId);

        /// <summary>
        /// Sprint 4 Wave 1b #01：客户重取（从回收站恢复）。
        /// 对应 A 侧 Server.CRM_Customer.regain（AdvanceDelete(id, 0, time)）。
        /// 与 AdvanceDeleteAsync 语义镜像、方向相反：isDelete 置 0，清空 Delete_time / Delete_id。
        /// 幂等安全：未被预删除的客户再次调用仍返回 true（已处于恢复态）。
        /// 参数化执行，禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="id">客户 ID</param>
        /// <returns>是否成功（受影响行数 &gt; 0）</returns>
        Task<bool> RegainAsync(string id);

        /// <summary>
        /// Sprint 4 Wave 1b #02：移动端客户更新（简化字段子集）。
        /// 对应 A 侧 DAL.CRM_Customer.UpdateApp（DAL/CRM_Customer.cs:745）：
        /// 仅更新 cus_name / cus_add / cus_tel / cus_fax / cus_website / cus_industry_id /
        /// Provinces_id / City_id / cus_type_id / cus_level_id / cus_source_id /
        /// DesCripe / Remarks / emp_id / isPrivate 共 15 个业务字段；
        /// create_time / sn / isDelete / Delete_time / Delete_id / lastfollow / state / x / y 等管理字段保持不变。
        /// </summary>
        /// <param name="model">移动端提交的客户模型（id 必填）</param>
        /// <returns>是否成功（受影响行数 &gt; 0）</returns>
        Task<bool> UpdateAppAsync(CRM_Customer model);

        /// <summary>
        /// Sprint 4 Wave 3 #04：批量插入客户（普通导入）。
        /// 逐条检查 cus_name 唯一性；已存在则跳过并记入失败。
        /// 已插入的行会在下一次循环中被识别为重复（同一次导入内部去重）。
        /// </summary>
        /// <param name="models">待插入的客户列表</param>
        /// <returns>批量导入结果（Success=成功新增数，Error=跳过/失败数）</returns>
        Task<ExcelImportResult> ImportRangeAsync(List<CRM_Customer> models);

        /// <summary>
        /// Sprint 4 Wave 3 #06：管理员批量 upsert 客户（按 cus_name 覆盖）。
        /// 若 cus_name 已存在 → 用模型覆盖业务字段（保留 id / create_time / isDelete / Delete_* / lastfollow / state / sn）
        /// 若不存在 → 直接插入。
        /// </summary>
        /// <param name="models">待 upsert 的客户列表</param>
        /// <returns>批量导入结果（Success=新增数，Update=覆盖数，Error=失败数）</returns>
        Task<ExcelImportResult> AdminImportRangeAsync(List<CRM_Customer> models);
    }
}
