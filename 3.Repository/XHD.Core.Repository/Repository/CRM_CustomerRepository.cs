using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;
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
        /// <returns>JArray，每项含 CustomerType/CustomerType_id/params_order/cc</returns>
        public async Task<JArray> FunnelAsync(int? year)
        {
            var query = _fsql.Select<CRM_Customer>();

            if (year.HasValue)
            {
                int y = year.Value;
                query = query.Where(a => a.create_time.Value.Year == y);
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
    }
}
