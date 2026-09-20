using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;
using XHD.Core.Common.Excel;
using System.Linq;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XHD.Core.Repository
{
    public class CRM_CustomerRepository : BaseRepository<CRM_Customer>, ICRM_CustomerRepository
    {
        public CRM_CustomerRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 更新
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async new Task<int> UpdateAsync(CRM_Customer model)
        {
            var result = await _fsql.Update<CRM_Customer>()
                .SetSource(model)
                .IgnoreColumns(a => new { a.create_id, a.create_time, a.sn, a.isDelete, a.Delete_time,a.lastfollow })
                .ExecuteAffrowsAsync();

            return result;
        }

        /// <summary>
        /// 最后跟进
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async Task<bool> LastFollow(string id)
        {
            var result = await _fsql.Update<CRM_Customer>()
                .Set(a => a.lastfollow == DateTime.Now)
                .Where(a => a.id == id)
                .ExecuteAffrowsAsync();

            if (result == 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 认领客户（底层更新）：将指定客户列表的 state 更新为目标值（一般 0=正常），并将归属人设置为 empId
        /// 语义：认领 = state 改为 targetState + emp_id 设为当前用户（当前用户身份由上层传入）
        /// 参数化：FreeSql 会自动把 ids 展开为 IN (@p0,@p1,...) 参数化语句，杜绝 SQL 注入
        /// </summary>
        /// <param name="ids">客户 ID 列表</param>
        /// <param name="targetState">目标 state 值（认领时通常为 0）</param>
        /// <param name="empId">认领人 ID</param>
        /// <returns>是否有记录被更新</returns>
        public async Task<bool> ClaimlistAsync(List<string> ids, int targetState, string empId)
        {
            if (ids == null || ids.Count == 0)
            {
                return false;
            }

            int rows = await _fsql.Update<CRM_Customer>()
                .Set(a => a.state == targetState)
                .Set(a => a.emp_id == empId)
                .Where(a => ids.Contains(a.id))
                .ExecuteAffrowsAsync();

            return rows > 0;
        }

        /// <summary>
        /// 放弃客户（底层更新）：将指定客户列表的 state 更新为目标值（一般 1=回到公共池），不清空 emp_id
        /// 语义：放弃 = state 改为 targetState；emp_id 保留原归属，供审计/回归使用
        /// 参数化：FreeSql 会自动把 ids 展开为 IN (@p0,@p1,...) 参数化语句，杜绝 SQL 注入
        /// </summary>
        /// <param name="ids">客户 ID 列表</param>
        /// <param name="targetState">目标 state 值（放弃时通常为 1）</param>
        /// <returns>是否有记录被更新</returns>
        public async Task<bool> AbanDonAsync(List<string> ids, int targetState)
        {
            if (ids == null || ids.Count == 0)
            {
                return false;
            }

            int rows = await _fsql.Update<CRM_Customer>()
                .Set(a => a.state == targetState)
                .Where(a => ids.Contains(a.id))
                .ExecuteAffrowsAsync();

            return rows > 0;
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="Page"></param>
        /// <param name="Limit"></param>
        /// <param name="expOrder"></param>
        /// <returns></returns>
        public async new Task<XHDData<CRM_Customer>> GridAsync(Expression<Func<CRM_Customer, bool>> expWhere, int Page, int Limit)
        {
            var data = await _fsql.Select<CRM_Customer>()
                .LeftJoin(a => a.cus_industry.id == a.cus_industry_id && a.cus_industry.params_type == "cus_industry")
                .LeftJoin(a => a.cus_type.id == a.cus_type_id && a.cus_type.params_type == "cus_type")
                .LeftJoin(a => a.cus_level.id == a.cus_level_id && a.cus_level.params_type == "cus_level")
                .LeftJoin(a => a.cus_source.id == a.cus_source_id && a.cus_source.params_type == "cus_source")
                .LeftJoin(a => a.Provinces.id == a.Provinces_id)
                .LeftJoin(a => a.City.id == a.City_id)
                .LeftJoin(a => a.Employee.id == a.emp_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                .LeftJoin(a => a.Employee.department.id == a.Employee.dep_id)
                .Where(expWhere)
                //.OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync(true);

            //构建返回数据
            XHDData<CRM_Customer> result = new XHDData<CRM_Customer>()
            {
                data = data,
                count = total
            };

            return result;
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="Page"></param>
        /// <param name="Limit"></param>
        /// <param name="expOrder"></param>
        /// <returns></returns>
        public async new Task<XHDData<CRM_Customer>> GridAsync(Expression<Func<CRM_Customer, bool>> expWhere, int Page, int Limit, string orderby)
        {
            if (string.IsNullOrWhiteSpace(orderby))
            {
                return await GridAsync(expWhere, Page, Limit);
            }

            var data = await _fsql.Select<CRM_Customer>()
                .LeftJoin(a => a.cus_industry.id == a.cus_industry_id && a.cus_industry.params_type == "cus_industry")
                .LeftJoin(a => a.cus_type.id == a.cus_type_id && a.cus_type.params_type == "cus_type")
                .LeftJoin(a => a.cus_level.id == a.cus_level_id && a.cus_level.params_type == "cus_level")
                .LeftJoin(a => a.cus_source.id == a.cus_source_id && a.cus_source.params_type == "cus_source")
                .LeftJoin(a => a.Provinces.id == a.Provinces_id)
                .LeftJoin(a => a.City.id == a.City_id)
                .LeftJoin(a => a.Employee.id == a.emp_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                .LeftJoin(a => a.Employee.department.id == a.Employee.dep_id)
                .Where(expWhere)
                .OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync(true);

            //构建返回数据
            XHDData<CRM_Customer> result = new XHDData<CRM_Customer>()
            {
                data = data,
                count = total
            };

            return result;
        }

        /// <summary>
        /// 普通条件查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <returns></returns>
        public async new Task<List<CRM_Customer>> GridAsync(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            var data = await _fsql.Select<CRM_Customer>()
                .LeftJoin(a => a.cus_industry.id == a.cus_industry_id && a.cus_industry.params_type == "cus_industry")
                .LeftJoin(a => a.cus_type.id == a.cus_type_id && a.cus_type.params_type == "cus_type")
                .LeftJoin(a => a.cus_level.id == a.cus_level_id && a.cus_level.params_type == "cus_level")
                .LeftJoin(a => a.cus_source.id == a.cus_source_id && a.cus_source.params_type == "cus_source")
                .LeftJoin(a => a.Provinces.id == a.Provinces_id)
                .LeftJoin(a => a.City.id == a.City_id)
                .LeftJoin(a => a.Employee.id == a.emp_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                .LeftJoin(a => a.Employee.department.id == a.Employee.dep_id)
                    .Where(expWhere)
                    .ToListAsync(true);

            return data;
        }

        /// <summary>
        /// 普通条件查询带排序
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="OrderBy"></param>
        /// <returns></returns>
        public async new Task<List<CRM_Customer>> GridAsync(Expression<Func<CRM_Customer, bool>> expWhere, string OrderBy)
        {
            if (string.IsNullOrWhiteSpace(OrderBy))
            {
                return await GridAsync(expWhere);
            }

            var data = await _fsql.Select<CRM_Customer>()
                .LeftJoin(a => a.cus_industry.id == a.cus_industry_id && a.cus_industry.params_type == "cus_industry")
                .LeftJoin(a => a.cus_type.id == a.cus_type_id && a.cus_type.params_type == "cus_type")
                .LeftJoin(a => a.cus_level.id == a.cus_level_id && a.cus_level.params_type == "cus_level")
                .LeftJoin(a => a.cus_source.id == a.cus_source_id && a.cus_source.params_type == "cus_source")
                .LeftJoin(a => a.Provinces.id == a.Provinces_id)
                .LeftJoin(a => a.City.id == a.City_id)
                .LeftJoin(a => a.Employee.id == a.emp_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                .LeftJoin(a => a.Employee.department.id == a.Employee.dep_id)
                    .Where(expWhere)
                    .OrderBy(OrderBy)
                    .ToListAsync(true);

            return data;
        }

        public async Task<JArray> ReportYear(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            var data = await _fsql.Select<CRM_Customer>()
                .Where(expWhere)
                .GroupBy(a => new { xmonth = a.create_time.Value.ToString("MM") })
                .ToListAsync(a => new { a.Key.xmonth, count = a.Count() });

            //return data.Select(a => (dynamic)a).ToList();
            JArray arr = new JArray();
            foreach (var item in data)
            {
                JObject obj = new JObject();
                obj.Add("xmonth", item.xmonth);
                obj.Add("count", item.count);

                arr.Add(obj);
            }

            return arr;
        }

        public async Task<JArray> ReportIndustry(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            var data = await _fsql.Select<CRM_Customer>()
                .Where(expWhere)
                .GroupBy(a => new { xmonth = a.cus_industry.params_name })
                .ToListAsync(a => new { a.Key.xmonth, count = a.Count() });

            //return data.Select(a => (dynamic)a).ToList();
            JArray arr = new JArray();
            foreach (var item in data)
            {
                JObject obj = new JObject();

                if (string.IsNullOrWhiteSpace(item.xmonth))
                {
                    obj.Add("xmonth", "未分类");
                }
                else
                {
                    obj.Add("xmonth", item.xmonth);
                }


                obj.Add("count", item.count);

                arr.Add(obj);
            }

            return arr;
        }

        public async Task<JArray> ReportType(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            var data = await _fsql.Select<CRM_Customer>()
                .Where(expWhere)
                .GroupBy(a => new { xmonth = a.cus_type.params_name })
                .ToListAsync(a => new { a.Key.xmonth, count = a.Count() });

            //return data.Select(a => (dynamic)a).ToList();
            JArray arr = new JArray();
            foreach (var item in data)
            {
                JObject obj = new JObject();

                if (string.IsNullOrWhiteSpace(item.xmonth))
                {
                    obj.Add("xmonth", "未分类");
                }
                else
                {
                    obj.Add("xmonth", item.xmonth);
                }


                obj.Add("count", item.count);

                arr.Add(obj);
            }

            return arr;
        }

        public async Task<JArray> ReportLevel(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            var data = await _fsql.Select<CRM_Customer>()
                .Where(expWhere)
                .GroupBy(a => new { xmonth = a.cus_level.params_name })
                .ToListAsync(a => new { a.Key.xmonth, count = a.Count() });

            //return data.Select(a => (dynamic)a).ToList();
            JArray arr = new JArray();
            foreach (var item in data)
            {
                JObject obj = new JObject();

                if (string.IsNullOrWhiteSpace(item.xmonth))
                {
                    obj.Add("xmonth", "未分类");
                }
                else
                {
                    obj.Add("xmonth", item.xmonth);
                }


                obj.Add("count", item.count);

                arr.Add(obj);
            }

            return arr;
        }

        public async Task<JArray> ReportSource(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            var data = await _fsql.Select<CRM_Customer>()
                .Where(expWhere)
                .GroupBy(a => new { xmonth = a.cus_source.params_name })
                .ToListAsync(a => new { a.Key.xmonth, count = a.Count() });

            //return data.Select(a => (dynamic)a).ToList();
            JArray arr = new JArray();
            foreach (var item in data)
            {
                JObject obj = new JObject();

                if (string.IsNullOrWhiteSpace(item.xmonth))
                {
                    obj.Add("xmonth", "未分类");
                }
                else
                {
                    obj.Add("xmonth", item.xmonth);
                }


                obj.Add("count", item.count);

                arr.Add(obj);
            }

            return arr;
        }

        public async Task<JArray> ReportProvinces(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            var data = await _fsql.Select<CRM_Customer>()
                .LeftJoin(a => a.Provinces.id == a.Provinces_id)
                .Where(expWhere)
                .GroupBy(a => new { xmonth = a.Provinces.Provinces })
                .ToListAsync(a => new { a.Key.xmonth, count = a.Count() });

            //return data.Select(a => (dynamic)a).ToList();
            JArray arr = new JArray();
            foreach (var item in data)
            {
                JObject obj = new JObject();

                if (string.IsNullOrWhiteSpace(item.xmonth))
                {
                    continue;
                }

                obj.Add("xmonth", item.xmonth);
                obj.Add("count", item.count);

                arr.Add(obj);
            }

            return arr;
        }

        public async Task<JArray> ReportCity(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            var data = await _fsql.Select<CRM_Customer>()
                .LeftJoin(a => a.City.id == a.City_id)
                .Where(expWhere)
                .GroupBy(a => new { xmonth = a.City.City })
                .ToListAsync(a => new { a.Key.xmonth, count = a.Count() });

            //return data.Select(a => (dynamic)a).ToList();
            JArray arr = new JArray();
            foreach (var item in data)
            {
                JObject obj = new JObject();

                if (string.IsNullOrWhiteSpace(item.xmonth))
                {
                    continue;
                }

                obj.Add("xmonth", item.xmonth);
                obj.Add("count", item.count);

                arr.Add(obj);
            }

            return arr;
        }

        /// <summary>
        /// 客户转化漏斗：按客户类型（cus_type_id 关联 Sys_Param）统计年度客户数
        /// 按 params_order 排序，未分类客户归入"未分类"
        /// </summary>
        /// <param name="year">年份过滤，null 表示不限年份</param>
        /// <param name="typeIds">客户类型 ID 白名单；null 或空集合表示不限类型</param>
        /// <returns>JArray，每项含 CustomerType/CustomerType_id/params_order/cc</returns>
        public async Task<JArray> FunnelAsync(int? year, List<string> typeIds = null)
        {
            var query = _fsql.Select<CRM_Customer>();

            if (year.HasValue)
            {
                int y = year.Value;
                query = query.Where(a => a.create_time.Value.Year == y);
            }

            if (typeIds != null && typeIds.Count > 0)
            {
                query = query.Where(a => typeIds.Contains(a.cus_type_id));
            }

            var data = await query
                .GroupBy(a => new
                {
                    name = a.cus_type.params_name,
                    typeId = a.cus_type.id,
                    order = a.cus_type.params_order
                })
                .ToListAsync(a => new
                {
                    a.Key.name,
                    a.Key.typeId,
                    a.Key.order,
                    count = a.Count()
                });

            JArray arr = new JArray();
            foreach (var item in data.OrderBy(x => x.order ?? int.MaxValue))
            {
                JObject obj = new JObject();

                if (string.IsNullOrWhiteSpace(item.name))
                {
                    obj.Add("CustomerType", "未分类");
                }
                else
                {
                    obj.Add("CustomerType", item.name);
                }

                obj.Add("CustomerType_id", item.typeId);
                obj.Add("params_order", item.order);
                obj.Add("cc", item.count);

                arr.Add(obj);
            }

            return arr;
        }

        /// <summary>
        /// 员工年度客户新增报表（Wave 3b #13）：明细查询 + 内存 PIVOT
        /// 按 create_id（客户创建人）× 12 个月分组计数
        /// </summary>
        /// <param name="year">统计年份</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空集合表示统计全部员工</param>
        /// <returns>JArray，每员工一项，含 name、yy 与 m1..m12 计数</returns>
        public async Task<JArray> ReportEmpCusAsync(int year, List<string> empIds)
        {
            bool hasEmpIds = empIds != null && empIds.Count > 0;

            var details = await _fsql.Select<CRM_Customer>()
                .Where(a => a.isDelete == 0
                            && a.create_time != null
                            && a.create_time.Value.Year == year)
                .WhereIf(hasEmpIds, a => empIds.Contains(a.create_id))
                .GroupBy(a => new { a.create_id, month = a.create_time.Value.Month })
                .ToListAsync(a => new
                {
                    a.Key.create_id,
                    month = a.Key.month,
                    count = a.Count()
                });

            var empQuery = _fsql.Select<hr_employee>();
            if (hasEmpIds)
            {
                empQuery = empQuery.Where(e => empIds.Contains(e.id));
            }

            var employees = await empQuery.ToListAsync();

            JArray arr = new JArray();
            foreach (var emp in employees)
            {
                var empDetails = details.Where(d => d.create_id == emp.id).ToList();
                JObject obj = new JObject { ["name"] = emp.name, ["yy"] = year };
                for (int m = 1; m <= 12; m++)
                {
                    obj[$"m{m}"] = empDetails.FirstOrDefault(d => d.month == m)?.count ?? 0;
                }
                arr.Add(obj);
            }

            return arr;
        }

        /// <summary>
        /// 员工月度客户新增报表（Wave 3b #14）：明细查询 + 内存 PIVOT
        /// 按 create_id × 12 个月分组计数，返回结构与 #13 保持一致（前端兼容）
        /// </summary>
        /// <param name="start">起始时间（含）</param>
        /// <param name="end">结束时间（含）</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空集合表示统计全部员工</param>
        /// <returns>JArray，每员工一项，含 name 与 m1..m12 计数</returns>
        public async Task<JArray> ReportMonthEmpCusAsync(DateTime? start, DateTime? end, List<string> empIds)
        {
            bool hasEmpIds = empIds != null && empIds.Count > 0;
            bool hasStart = start.HasValue;
            bool hasEnd = end.HasValue;

            var details = await _fsql.Select<CRM_Customer>()
                .Where(a => a.isDelete == 0 && a.create_time != null)
                .WhereIf(hasStart, a => a.create_time.Value >= start.Value)
                .WhereIf(hasEnd, a => a.create_time.Value <= end.Value)
                .WhereIf(hasEmpIds, a => empIds.Contains(a.create_id))
                .GroupBy(a => new { a.create_id, month = a.create_time.Value.Month })
                .ToListAsync(a => new
                {
                    a.Key.create_id,
                    month = a.Key.month,
                    count = a.Count()
                });

            var empQuery = _fsql.Select<hr_employee>();
            if (hasEmpIds)
            {
                empQuery = empQuery.Where(e => empIds.Contains(e.id));
            }

            var employees = await empQuery.ToListAsync();

            JArray arr = new JArray();
            foreach (var emp in employees)
            {
                var empDetails = details.Where(d => d.create_id == emp.id).ToList();
                JObject obj = new JObject { ["name"] = emp.name };
                for (int m = 1; m <= 12; m++)
                {
                    obj[$"m{m}"] = empDetails.FirstOrDefault(d => d.month == m)?.count ?? 0;
                }
                arr.Add(obj);
            }

            return arr;
        }

        /// <summary>
        /// 员工维度双月客户新增对比（Wave 3b #15）：
        /// 按年 + 起止月份区间过滤，逐员工输出 startMonth_count、endMonth_count、diff。
        /// Sprint 1 简化：不通过 hr_post 间接过滤，直接接收 empIds 列表。
        /// 若 startMonth / endMonth 为 null，则对应 count 记为 0。
        /// </summary>
        /// <param name="startMonth">起始月份（1-12），null 表示未指定</param>
        /// <param name="endMonth">结束月份（1-12），null 表示未指定</param>
        /// <param name="year">统计年份</param>
        /// <param name="empIds">参与统计的员工 ID 列表；null 或空集合表示统计全部员工</param>
        /// <returns>JArray，每员工一项，含 name、startMonth_count、endMonth_count、diff</returns>
        public async Task<JArray> ComparedEmpCusAddAsync(int? startMonth, int? endMonth, int year, List<string> empIds)
        {
            bool hasEmpIds = empIds != null && empIds.Count > 0;
            bool hasStartMonth = startMonth.HasValue;
            bool hasEndMonth = endMonth.HasValue;

            var details = await _fsql.Select<CRM_Customer>()
                .Where(a => a.isDelete == 0
                            && a.create_time != null
                            && a.create_time.Value.Year == year)
                .WhereIf(hasStartMonth, a => a.create_time.Value.Month >= startMonth.Value)
                .WhereIf(hasEndMonth, a => a.create_time.Value.Month <= endMonth.Value)
                .WhereIf(hasEmpIds, a => empIds.Contains(a.create_id))
                .GroupBy(a => new { a.create_id, month = a.create_time.Value.Month })
                .ToListAsync(a => new
                {
                    a.Key.create_id,
                    month = a.Key.month,
                    count = a.Count()
                });

            var empQuery = _fsql.Select<hr_employee>();
            if (hasEmpIds)
            {
                empQuery = empQuery.Where(e => empIds.Contains(e.id));
            }

            var employees = await empQuery.ToListAsync();

            int m1 = startMonth ?? 0;
            int m2 = endMonth ?? 0;

            JArray arr = new JArray();
            foreach (var emp in employees)
            {
                var empDetails = details.Where(d => d.create_id == emp.id).ToList();
                int startCount = empDetails.FirstOrDefault(d => d.month == m1)?.count ?? 0;
                int endCount = empDetails.FirstOrDefault(d => d.month == m2)?.count ?? 0;
                JObject obj = new JObject
                {
                    ["name"] = emp.name,
                    ["startMonth"] = startMonth,
                    ["endMonth"] = endMonth,
                    ["startMonth_count"] = startCount,
                    ["endMonth_count"] = endCount,
                    ["diff"] = endCount - startCount
                };
                arr.Add(obj);
            }

            return arr;
        }

        /// <summary>
        /// Sprint 3 #12：客户总数 KPI。
        /// 直接按外部传入的动态表达式统计客户数（表达式必须包含 isDelete=0 基础过滤）。
        /// </summary>
        /// <param name="expWhere">完整过滤表达式</param>
        /// <returns>符合条件的客户总数</returns>
        public async Task<int> CountAsync(Expression<Func<CRM_Customer, bool>> expWhere)
        {
            long total = await _fsql.Select<CRM_Customer>()
                .Where(expWhere)
                .CountAsync();
            return (int)total;
        }

        /// <summary>
        /// Sprint 3 Wave 2 #13：客户预删除（软删）。
        /// 将指定客户的 isDelete 置 1、写入 Delete_time 与 Delete_id。
        /// 幂等安全：重复调用仍返回 true（受影响行数由 UPDATE 的 SET 语义保证至少匹配到）。
        /// 参数化执行，禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="id">客户 ID</param>
        /// <param name="operatorId">删除人 ID（当前登录用户）</param>
        /// <returns>受影响行数 &gt; 0 则返回 true</returns>
        public async Task<bool> AdvanceDeleteAsync(string id, string operatorId)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            DateTime now = DateTime.Now;
            int rows = await _fsql.Update<CRM_Customer>()
                .Set(a => a.isDelete == 1)
                .Set(a => a.Delete_time == now)
                .Set(a => a.Delete_id == operatorId)
                .Where(a => a.id == id)
                .ExecuteAffrowsAsync();

            return rows > 0;
        }

        /// <summary>
        /// Sprint 4 Wave 1b #01：客户重取（从回收站恢复）。
        /// 对应 A 侧 Server.CRM_Customer.regain（内部调 AdvanceDelete(id, 0, now)）。
        /// 与 <see cref="AdvanceDeleteAsync"/> 语义镜像、方向相反：
        ///   isDelete  1 → 0
        ///   Delete_time 非空 → null
        ///   Delete_id 非空 → ""
        /// 幂等安全：未被预删除的客户再次调用仍返回 true（已处于恢复态，UPDATE 仍匹配到行）。
        /// 参数化执行（FreeSql 表达式转参数），禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="id">客户 ID</param>
        /// <returns>是否成功（受影响行数 &gt; 0）</returns>
        public async Task<bool> RegainAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            DateTime? nullTime = null;

            int rows = await _fsql.Update<CRM_Customer>()
                .Set(a => a.isDelete, 0)
                .Set(a => a.Delete_time, nullTime)
                .Set(a => a.Delete_id, string.Empty)
                .Where(a => a.id == id)
                .ExecuteAffrowsAsync();

            return rows > 0;
        }

        /// <summary>
        /// Sprint 4 Wave 1b #02：移动端客户更新（简化字段子集）。
        /// 对应 A 侧 DAL.CRM_Customer.UpdateApp（DAL/CRM_Customer.cs:745-810）：
        /// 仅更新 15 个业务字段，create_time / sn / isDelete / Delete_time / Delete_id /
        /// lastfollow / state / x / y 等管理字段保持不变。
        /// 参数化执行（FreeSql 表达式逐字段转参数），禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="model">移动端提交的客户模型（id 必填）</param>
        /// <returns>是否成功（受影响行数 &gt; 0）</returns>
        public async Task<bool> UpdateAppAsync(CRM_Customer model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.id))
            {
                return false;
            }

            int rows = await _fsql.Update<CRM_Customer>()
                .Set(a => a.cus_name, model.cus_name)
                .Set(a => a.cus_add, model.cus_add)
                .Set(a => a.cus_tel, model.cus_tel)
                .Set(a => a.cus_fax, model.cus_fax)
                .Set(a => a.cus_website, model.cus_website)
                .Set(a => a.cus_industry_id, model.cus_industry_id)
                .Set(a => a.Provinces_id, model.Provinces_id)
                .Set(a => a.City_id, model.City_id)
                .Set(a => a.cus_type_id, model.cus_type_id)
                .Set(a => a.cus_level_id, model.cus_level_id)
                .Set(a => a.cus_source_id, model.cus_source_id)
                .Set(a => a.DesCripe, model.DesCripe)
                .Set(a => a.Remarks, model.Remarks)
                .Set(a => a.emp_id, model.emp_id)
                .Set(a => a.isPrivate, model.isPrivate)
                .Where(a => a.id == model.id)
                .ExecuteAffrowsAsync();

            return rows > 0;
        }

        /// <summary>
        /// Sprint 4 Wave 3 #04：批量插入客户（普通导入）。
        /// 逐条检查 cus_name 唯一性（未删除），已存在则跳过并记入失败。
        /// 同一次调用内插入的行也参与去重（用 HashSet 缓存已成功插入的 cus_name）。
        /// 参数化执行（FreeSql 表达式转参数），禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="models">待插入的客户列表（不能为 null；可空列表）</param>
        /// <returns>批量导入结果（Success=成功新增数，Error=跳过/失败数，Message=错误详情）</returns>
        public async Task<ExcelImportResult> ImportRangeAsync(List<CRM_Customer> models)
        {
            var result = new ExcelImportResult();
            if (models == null || models.Count == 0)
            {
                result.Message = "导入数据为空";
                return result;
            }

            // 缓存同批次内已成功插入的 cus_name，避免同一次导入内部重复
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int rowNum = 0;
            foreach (var model in models)
            {
                rowNum++;
                if (model == null || string.IsNullOrWhiteSpace(model.cus_name))
                {
                    result.Fail(rowNum, "客户名不能为空");
                    continue;
                }

                // 同批次内去重
                if (seenNames.Contains(model.cus_name))
                {
                    result.Fail(rowNum, $"客户【{model.cus_name}】在本批次中重复");
                    continue;
                }

                // 数据库去重（不删除的记录中是否存在同名客户）
                var existingCount = await _fsql.Select<CRM_Customer>()
                    .Where(c => c.cus_name == model.cus_name && c.isDelete != 1)
                    .CountAsync();

                if (existingCount > 0)
                {
                    result.Fail(rowNum, $"客户【{model.cus_name}】已存在，跳过");
                    continue;
                }

                // 参数化插入
                var rows = await _fsql.Insert(model).ExecuteAffrowsAsync();
                if (rows > 0)
                {
                    result.Add();
                    seenNames.Add(model.cus_name);
                }
                else
                {
                    result.Fail(rowNum, $"客户【{model.cus_name}】插入失败");
                }
            }

            return result;
        }

        /// <summary>
        /// Sprint 4 Wave 3 #06：管理员批量 upsert 客户（按 cus_name 覆盖）。
        /// 覆盖时保留 id / create_time / sn / isDelete / Delete_time / Delete_id /
        /// lastfollow / state 等管理字段不变，仅更新业务字段（与 UpdateApp 同一子集 + isPrivate）。
        /// </summary>
        /// <param name="models">待 upsert 的客户列表</param>
        /// <returns>批量导入结果（Success=新增数，Update=覆盖数，Error=失败数）</returns>
        public async Task<ExcelImportResult> AdminImportRangeAsync(List<CRM_Customer> models)
        {
            var result = new ExcelImportResult();
            if (models == null || models.Count == 0)
            {
                result.Message = "导入数据为空";
                return result;
            }

            int rowNum = 0;
            foreach (var model in models)
            {
                rowNum++;
                if (model == null || string.IsNullOrWhiteSpace(model.cus_name))
                {
                    result.Fail(rowNum, "客户名不能为空");
                    continue;
                }

                // 查找是否已存在同名客户（未删除）
                var existing = await _fsql.Select<CRM_Customer>()
                    .Where(c => c.cus_name == model.cus_name && c.isDelete != 1)
                    .FirstAsync();

                if (existing != null)
                {
                    // 覆盖更新：只更新业务字段，管理字段保持不变
                    int rows = await _fsql.Update<CRM_Customer>()
                        .Set(a => a.cus_name, model.cus_name)
                        .Set(a => a.cus_add, model.cus_add)
                        .Set(a => a.cus_tel, model.cus_tel)
                        .Set(a => a.cus_fax, model.cus_fax)
                        .Set(a => a.cus_website, model.cus_website)
                        .Set(a => a.cus_industry_id, model.cus_industry_id)
                        .Set(a => a.Provinces_id, model.Provinces_id)
                        .Set(a => a.City_id, model.City_id)
                        .Set(a => a.cus_type_id, model.cus_type_id)
                        .Set(a => a.cus_level_id, model.cus_level_id)
                        .Set(a => a.cus_source_id, model.cus_source_id)
                        .Set(a => a.DesCripe, model.DesCripe)
                        .Set(a => a.Remarks, model.Remarks)
                        .Set(a => a.emp_id, model.emp_id)
                        .Set(a => a.isPrivate, model.isPrivate)
                        .Where(a => a.id == existing.id)
                        .ExecuteAffrowsAsync();

                    if (rows > 0)
                    {
                        result.AddUpdate();
                    }
                    else
                    {
                        result.Fail(rowNum, $"客户【{model.cus_name}】覆盖更新失败");
                    }
                }
                else
                {
                    // 不存在 → 直接插入
                    var rows = await _fsql.Insert(model).ExecuteAffrowsAsync();
                    if (rows > 0)
                    {
                        result.Add();
                    }
                    else
                    {
                        result.Fail(rowNum, $"客户【{model.cus_name}】插入失败");
                    }
                }
            }

            return result;
        }
    }
}
