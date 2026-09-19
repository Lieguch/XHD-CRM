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
    internal class CRM_CustomerService : BaseService<CRM_Customer>, ICRM_CustomerService
    {
        ICRM_CustomerRepository _irepositoryBase;

        public CRM_CustomerService(ICRM_CustomerRepository repository)
        {
            _irepository = repository;
            _irepositoryBase = repository;
        }

        public async Task<JArray> ReportYear(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            return await _irepositoryBase.ReportYear(expWhere);
        }

        public async Task<JArray> ReportIndustry(Expression<Func<CRM_Customer, bool>> expWhere)
        { 
            return  await _irepositoryBase.ReportIndustry(expWhere);
        }

        public async Task<JArray> ReportType(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            return await _irepositoryBase.ReportType(expWhere);
        }

        public async Task<JArray> ReportLevel(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            return await _irepositoryBase.ReportLevel(expWhere);
        }

        public async Task<JArray> ReportSource(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            return await _irepositoryBase.ReportSource(expWhere);
        }

        public async Task<JArray> ReportCity(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            return await _irepositoryBase.ReportCity(expWhere);
        }

        public async Task<JArray> ReportProvinces(Expression<Func<CRM_Customer, bool>> expWhere)
        { 
            return await _irepositoryBase.ReportProvinces(expWhere);
        }

        public async Task<bool> LastFollow(string id)
        { 
            return await _irepositoryBase.LastFollow(id);
        }

        /// <summary>
        /// 认领客户：service 层薄封装。targetState=0（正常/已分配员工）由 service 硬编码。
        /// 幂等安全：Service 只负责业务语义映射，不承担并发/权限判定。
        /// </summary>
        /// <param name="ids">客户 ID 列表</param>
        /// <param name="empId">认领人（当前登录用户）ID</param>
        /// <returns>是否有记录被更新</returns>
        public async Task<bool> Claimlist(List<string> ids, string empId)
        {
            if (ids == null || ids.Count == 0)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(empId))
            {
                return false;
            }

            return await _irepositoryBase.ClaimlistAsync(ids, 0, empId);
        }

        /// <summary>
        /// 放弃客户：service 层薄封装。targetState=1（公共客户池）由 service 硬编码。
        /// </summary>
        /// <param name="ids">客户 ID 列表</param>
        /// <returns>是否有记录被更新</returns>
        public async Task<bool> AbanDon(List<string> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return false;
            }

            return await _irepositoryBase.AbanDonAsync(ids, 1);
        }

        /// <summary>
        /// 客户转化漏斗：service 层薄封装，委托 Repository 执行
        /// </summary>
        /// <param name="year">年份过滤，null 表示不限年份</param>
        public async Task<JArray> FunnelAsync(int? year)
        {
            return await _irepositoryBase.FunnelAsync(year);
        }

        /// <summary>
        /// 员工年度客户新增报表：service 层薄封装，委托 Repository 执行
        /// </summary>
        /// <param name="year">统计年份</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        public async Task<JArray> ReportEmpCusAsync(int year, List<string> empIds)
        {
            return await _irepositoryBase.ReportEmpCusAsync(year, empIds);
        }

        /// <summary>
        /// 员工月度客户新增报表：service 层薄封装，委托 Repository 执行
        /// </summary>
        /// <param name="start">起始时间（含）</param>
        /// <param name="end">结束时间（含）</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        public async Task<JArray> ReportMonthEmpCusAsync(DateTime? start, DateTime? end, List<string> empIds)
        {
            return await _irepositoryBase.ReportMonthEmpCusAsync(start, end, empIds);
        }

        /// <summary>
        /// 员工维度双月客户新增对比：service 层薄封装，委托 Repository 执行
        /// </summary>
        /// <param name="startMonth">起始月份（1-12）</param>
        /// <param name="endMonth">结束月份（1-12）</param>
        /// <param name="year">统计年份</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空表示全部员工</param>
        public async Task<JArray> ComparedEmpCusAddAsync(int? startMonth, int? endMonth, int year, List<string> empIds)
        {
            return await _irepositoryBase.ComparedEmpCusAddAsync(startMonth, endMonth, year, empIds);
        }

        /// <summary>
        /// Sprint 3 #12：客户总数 KPI（service 层薄封装，委托 Repository 执行）
        /// </summary>
        /// <param name="expWhere">完整过滤表达式（含 isDelete=0）</param>
        /// <returns>符合条件的客户总数</returns>
        public async Task<int> CountAsync(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            return await _irepositoryBase.CountAsync(expWhere);
        }
    }
}
