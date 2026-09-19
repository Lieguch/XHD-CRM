using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;
using Newtonsoft.Json.Linq;

namespace XHD.Core.Repository
{
    public class CRM_followRepository : BaseRepository<CRM_follow>, ICRM_followRepository
    {
        public CRM_followRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 更新
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async new Task<int> UpdateAsync(CRM_follow model)
        {
            var result = await _fsql.Update<CRM_follow>()
                .SetSource(model)
                .IgnoreColumns(a => new { a.employee_id,a.follow_time,a.customer_id })
                .ExecuteAffrowsAsync();

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
        public async new Task<XHDData<CRM_follow>> GridAsync(Expression<Func<CRM_follow, bool>> expWhere, int Page, int Limit)
        {
            var data = await _fsql.Select<CRM_follow>()
                .LeftJoin(a => a.FollowAim.id == a.follow_aim_id && a.FollowAim.params_type == "follow_aim")
                .LeftJoin(a => a.FollowType.id == a.follow_type_id && a.FollowType.params_type == "follow_type")
                .LeftJoin(a => a.customer.id == a.customer_id )
                .LeftJoin(a => a.contact.id == a.contact_id)
                .LeftJoin(a => a.employee.id == a.employee_id)
                .LeftJoin(a => a.employee.department.id == a.employee.dep_id)
                .Where(expWhere)
                //.OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync(true);

            //构建返回数据
            XHDData<CRM_follow> result = new XHDData<CRM_follow>()
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
        public async new Task<XHDData<CRM_follow>> GridAsync(Expression<Func<CRM_follow, bool>> expWhere, int Page, int Limit, string orderby)
        {
            if (string.IsNullOrWhiteSpace(orderby))
            {
                return await GridAsync(expWhere, Page, Limit);
            }

            var data = await _fsql.Select<CRM_follow>()
                .LeftJoin(a => a.FollowAim.id == a.follow_aim_id && a.FollowAim.params_type == "follow_aim")
                .LeftJoin(a => a.FollowType.id == a.follow_type_id && a.FollowType.params_type == "follow_type")
                .LeftJoin(a => a.customer.id == a.customer_id)
                .LeftJoin(a => a.contact.id == a.contact_id)
                .LeftJoin(a => a.employee.id == a.employee_id)
                .LeftJoin(a => a.employee.department.id == a.employee.dep_id)
                .Where(expWhere)
                .OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync(true);

            //构建返回数据
            XHDData<CRM_follow> result = new XHDData<CRM_follow>()
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
        public async new Task<List<CRM_follow>> GridAsync(Expression<Func<CRM_follow, bool>> expWhere)
        {
            var data = await _fsql.Select<CRM_follow>()
                .LeftJoin(a => a.FollowAim.id == a.follow_aim_id && a.FollowAim.params_type == "follow_aim")
                .LeftJoin(a => a.FollowType.id == a.follow_type_id && a.FollowType.params_type == "follow_type")
                .LeftJoin(a => a.customer.id == a.customer_id)
                .LeftJoin(a => a.contact.id == a.contact_id)
                .LeftJoin(a => a.employee.id == a.employee_id)
                .LeftJoin(a => a.employee.department.id == a.employee.dep_id)
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
        public async new Task<List<CRM_follow>> GridAsync(Expression<Func<CRM_follow, bool>> expWhere, string OrderBy)
        {
            if (string.IsNullOrWhiteSpace(OrderBy))
            {
                return await GridAsync(expWhere);
            }

            var data = await _fsql.Select<CRM_follow>()
                .LeftJoin(a => a.FollowAim.id == a.follow_aim_id && a.FollowAim.params_type == "follow_aim")
                .LeftJoin(a => a.FollowType.id == a.follow_type_id && a.FollowType.params_type == "follow_type")
                .LeftJoin(a => a.customer.id == a.customer_id)
                .LeftJoin(a => a.contact.id == a.contact_id)
                .LeftJoin(a => a.employee.id == a.employee_id)
                .LeftJoin(a => a.employee.department.id == a.employee.dep_id)
                .Where(expWhere)
                .OrderBy(OrderBy)
                .ToListAsync(true);

            return data;
        }

        public async Task<JArray> ReportYear(Expression<Func<CRM_follow, bool>> expWhere)
        {
            var data = await _fsql.Select<CRM_follow>()
                .Where(expWhere)
                .GroupBy(a => new { xmonth = a.follow_time.Value.ToString("MM") })
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

        /// <summary>
        /// Sprint 3 #01：跟进年度趋势报表。
        /// 按 items 分组 + 12 个月计数矩阵；Contact_Type 分支因 B 侧 CRM_Contact
        /// 无 ParamsType 导航留到 Sprint 4，本方法仅支持 Follow_Type / Follow_aim。
        /// </summary>
        public async Task<JArray> ReportsYearAsync(string items, int year, Expression<Func<CRM_follow, bool>> expWhere)
        {
            if (string.IsNullOrWhiteSpace(items))
                throw new ArgumentException("items 参数不能为空", nameof(items));

            if (items == "Follow_Type")
            {
                var rows = await _fsql.Select<CRM_follow>()
                    .LeftJoin(a => a.FollowType.id == a.follow_type_id && a.FollowType.params_type == "follow_type")
                    .LeftJoin(a => a.customer.id == a.customer_id)
                    .Where(a => a.follow_time != null)
                    .Where(a => a.follow_time.Value.Year == year)
                    .WhereIf(expWhere != null, expWhere)
                    .ToListAsync(a => new
                    {
                        key = (string)a.FollowType.params_name,
                        month = a.follow_time.Value.Month
                    });

                return PivotFollowYear(rows);
            }

            if (items == "Follow_aim")
            {
                var rows = await _fsql.Select<CRM_follow>()
                    .LeftJoin(a => a.FollowAim.id == a.follow_aim_id && a.FollowAim.params_type == "follow_aim")
                    .LeftJoin(a => a.customer.id == a.customer_id)
                    .Where(a => a.follow_time != null)
                    .Where(a => a.follow_time.Value.Year == year)
                    .WhereIf(expWhere != null, expWhere)
                    .ToListAsync(a => new
                    {
                        key = (string)a.FollowAim.params_name,
                        month = a.follow_time.Value.Month
                    });

                return PivotFollowYear(rows);
            }

            throw new ArgumentException(
                $"不支持的 items：{items}（Sprint 3 仅支持 Follow_Type / Follow_aim；Contact_Type 留到 Sprint 4）",
                nameof(items));
        }

        /// <summary>
        /// #01 内部辅助：将 (key, month) 行列表 PIVOT 成 params_name × m1..m12 矩阵。
        /// </summary>
        private static JArray PivotFollowYear<T>(List<T> rows) where T : class
        {
            // 反射读取 key / month 字段，兼容两种匿名类型
            var keyProp = typeof(T).GetProperty("key");
            var monthProp = typeof(T).GetProperty("month");
            if (keyProp == null || monthProp == null)
                throw new InvalidOperationException("PIVOT 失败：无法读取 key/month 字段");

            var records = rows.Select(r =>
            {
                object keyVal = keyProp.GetValue(r);
                object monthVal = monthProp.GetValue(r);
                return new
                {
                    key = keyVal as string ?? "未分类",
                    month = monthVal is int mi ? mi : Convert.ToInt32(monthVal)
                };
            }).ToList();

            JArray arr = new JArray();
            var groups = records.GroupBy(r => r.key ?? "未分类");
            foreach (var group in groups)
            {
                JObject obj = new JObject { ["params_name"] = group.Key };
                for (int m = 1; m <= 12; m++)
                {
                    obj[$"m{m}"] = group.Count(r => r.month == m);
                }
                arr.Add(obj);
            }

            return arr;
        }

        /// <summary>
        /// Sprint 3 #02：跟进双月对比（按跟进类型）。
        /// 输出每类型在两个月的跟进数（dt1、dt2）。
        /// </summary>
        public async Task<JArray> ComparedFollowAsync(int year1, int month1, int year2, int month2)
        {
            var rows = await _fsql.Select<CRM_follow>()
                .LeftJoin(a => a.FollowType.id == a.follow_type_id && a.FollowType.params_type == "follow_type")
                .Where(a => a.follow_time != null)
                .Where(a => (a.follow_time.Value.Year == year1 && a.follow_time.Value.Month == month1)
                          || (a.follow_time.Value.Year == year2 && a.follow_time.Value.Month == month2))
                .ToListAsync(a => new
                {
                    key = (string)a.FollowType.params_name,
                    y = a.follow_time.Value.Year,
                    m = a.follow_time.Value.Month
                });

            JArray arr = new JArray();
            var groups = rows.GroupBy(r => r.key ?? "未分类");
            foreach (var g in groups)
            {
                int dt1 = g.Count(r => r.y == year1 && r.m == month1);
                int dt2 = g.Count(r => r.y == year2 && r.m == month2);
                arr.Add(new JObject
                {
                    ["yy"] = g.Key,
                    ["dt1"] = dt1,
                    ["dt2"] = dt2
                });
            }

            return arr;
        }

        /// <summary>
        /// Sprint 3 #03：员工维度双月跟进对比。
        /// </summary>
        public async Task<JArray> ComparedEmpCusFollowAsync(int year1, int month1, int year2, int month2, List<string> empIds)
        {
            bool hasEmpIds = empIds != null && empIds.Count > 0;

            // 先取员工列表（保留顺序，与 A 侧 hr_employee 全量遍历语义对齐）
            var empQuery = _fsql.Select<hr_employee>()
                .WhereIf(hasEmpIds, e => empIds.Contains(e.id));
            var employees = await empQuery.ToListAsync();

            // 取两个月的跟进明细
            var rows = await _fsql.Select<CRM_follow>()
                .Where(a => a.follow_time != null)
                .Where(a => (a.follow_time.Value.Year == year1 && a.follow_time.Value.Month == month1)
                          || (a.follow_time.Value.Year == year2 && a.follow_time.Value.Month == month2))
                .WhereIf(hasEmpIds, a => empIds.Contains(a.employee_id))
                .ToListAsync(a => new
                {
                    eid = a.employee_id,
                    y = a.follow_time.Value.Year,
                    m = a.follow_time.Value.Month
                });

            JArray arr = new JArray();
            foreach (var emp in employees)
            {
                int dt1 = rows.Count(r => r.eid == emp.id && r.y == year1 && r.m == month1);
                int dt2 = rows.Count(r => r.eid == emp.id && r.y == year2 && r.m == month2);
                arr.Add(new JObject
                {
                    ["yy"] = emp.name,
                    ["dt1"] = dt1,
                    ["dt2"] = dt2
                });
            }

            return arr;
        }

        /// <summary>
        /// Sprint 3 #04：员工月度跟进矩阵（跨月区间）。
        /// </summary>
        public async Task<JArray> ReportMonthEmpFollowAsync(DateTime start, DateTime end, List<string> empIds)
        {
            bool hasEmpIds = empIds != null && empIds.Count > 0;

            if (start > end)
                throw new ArgumentException("开始时间不能晚于结束时间", nameof(start));

            var empQuery = _fsql.Select<hr_employee>()
                .WhereIf(hasEmpIds, e => empIds.Contains(e.id));
            var employees = await empQuery.ToListAsync();

            var rows = await _fsql.Select<CRM_follow>()
                .Where(a => a.follow_time != null)
                .Where(a => a.follow_time.Value >= start)
                .Where(a => a.follow_time.Value <= end)
                .WhereIf(hasEmpIds, a => empIds.Contains(a.employee_id))
                .ToListAsync(a => new
                {
                    eid = a.employee_id,
                    m = a.follow_time.Value.Month
                });

            JArray arr = new JArray();
            foreach (var emp in employees)
            {
                var empRows = rows.Where(r => r.eid == emp.id).ToList();
                JObject obj = new JObject { ["name"] = emp.name, ["yy"] = start.Year };
                for (int m = 1; m <= 12; m++)
                {
                    obj[$"m{m}"] = empRows.Count(r => r.m == m);
                }
                arr.Add(obj);
            }

            return arr;
        }

        /// <summary>
        /// Sprint 3 #05：员工年度跟进矩阵。
        /// </summary>
        public async Task<JArray> ReportEmpFollowAsync(int year, List<string> empIds)
        {
            bool hasEmpIds = empIds != null && empIds.Count > 0;

            var empQuery = _fsql.Select<hr_employee>()
                .WhereIf(hasEmpIds, e => empIds.Contains(e.id));
            var employees = await empQuery.ToListAsync();

            var rows = await _fsql.Select<CRM_follow>()
                .Where(a => a.follow_time != null)
                .Where(a => a.follow_time.Value.Year == year)
                .WhereIf(hasEmpIds, a => empIds.Contains(a.employee_id))
                .ToListAsync(a => new
                {
                    eid = a.employee_id,
                    m = a.follow_time.Value.Month
                });

            JArray arr = new JArray();
            foreach (var emp in employees)
            {
                var empRows = rows.Where(r => r.eid == emp.id).ToList();
                JObject obj = new JObject { ["name"] = emp.name, ["yy"] = year };
                for (int m = 1; m <= 12; m++)
                {
                    obj[$"m{m}"] = empRows.Count(r => r.m == m);
                }
                arr.Add(obj);
            }

            return arr;
        }
    }
}
