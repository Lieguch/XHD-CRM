using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Repository
{
    public class hr_employeeRepository : BaseRepository<hr_employee>, Ihr_employeeRepository
    {
        public hr_employeeRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 更新
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async new Task<int> UpdateAsync(hr_employee model)
        {
            var result = await _fsql.Update<hr_employee>()
                .SetSource(model)
                .IgnoreColumns(a => new { a.default_city, a.create_id, a.create_time, a.pwd })
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
        public async new Task<XHDData<hr_employee>> GridAsync(Expression<Func<hr_employee, bool>> expWhere, int Page, int Limit)
        {
            var data = await _fsql.Select<hr_employee>()
                .LeftJoin(a => a.position.id == a.position_id)
                .LeftJoin(a => a.department.id == a.dep_id)
                .LeftJoin(a => a.Role.id == a.role_id)
                .Where(expWhere)
                //.OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync();

            //构建返回数据
            XHDData<hr_employee> result = new XHDData<hr_employee>()
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
        public async new Task<XHDData<hr_employee>> GridAsync(Expression<Func<hr_employee, bool>> expWhere, int Page, int Limit, string orderby)
        {
            if (string.IsNullOrWhiteSpace(orderby))
            {
                return await GridAsync(expWhere, Page, Limit);
            }

            var data = await _fsql.Select<hr_employee>()
                .LeftJoin(a => a.position.id == a.position_id)
                .LeftJoin(a => a.department.id == a.dep_id)
                .LeftJoin(a => a.Role.id == a.role_id)
                .Where(expWhere)
                .OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync();

            //构建返回数据
            XHDData<hr_employee> result = new XHDData<hr_employee>()
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
        public async new Task<List<hr_employee>> GridAsync(Expression<Func<hr_employee, bool>> expWhere)
        {
            var data = await _fsql.Select<hr_employee>()
                .LeftJoin(a => a.position.id == a.position_id)
                .LeftJoin(a => a.department.id == a.dep_id)
                .LeftJoin(a => a.Role.id == a.role_id)
                .Where(expWhere)
                .ToListAsync();

            return data;
        }

        /// <summary>
        /// 普通条件查询带排序
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="OrderBy"></param>
        /// <returns></returns>
        public async new Task<List<hr_employee>> GridAsync(Expression<Func<hr_employee, bool>> expWhere, string OrderBy)
        {
            if (string.IsNullOrWhiteSpace(OrderBy))
            {
                return await GridAsync(expWhere);
            }

            var data = await _fsql.Select<hr_employee>()
                .LeftJoin(a => a.position.id == a.position_id)
                .LeftJoin(a => a.department.id == a.dep_id)
                .LeftJoin(a => a.Role.id == a.role_id)
                .Where(expWhere)
                .OrderBy(OrderBy)
                .ToListAsync();

            return data;
        }

        /// <summary>
        /// Sprint 4 Wave 1b #10：员工唯一性校验计数。
        /// 对应 A 侧 Server.hr_employee.Exist 的 GetList(...).Rows.Count 语义。
        /// 不走 GridAsync（其内置 LeftJoin 依赖 hr_position/hr_department/Sys_role 表，
        /// 在唯一性校验场景下属多余开销），直接用纯单表 Select 计数。
        /// 参数化执行，禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="expWhere">完整过滤表达式（含 field==value 与可选的 id 排除）</param>
        /// <returns>符合条件的员工数量</returns>
        public async Task<int> ExistsAsync(Expression<Func<hr_employee, bool>> expWhere)
        {
            // FreeSql CountAsync() 返回 long，接口声明 Task&lt;int&gt;，需显式转换
            long total = await _fsql.Select<hr_employee>()
                .Where(expWhere)
                .CountAsync();
            return (int)total;
        }

        /// <summary>
        /// Sprint 4 Wave 1b #11：读取员工默认城市。
        /// 勘误 C3：字段名是 default_city（不是 default_city_id），已存在，无需 schema 补强。
        /// </summary>
        /// <param name="empId">员工 ID</param>
        /// <returns>默认城市；员工不存在返回 null，城市为空返回空字符串</returns>
        public async Task<string> GetDefaultCityAsync(string empId)
        {
            if (string.IsNullOrWhiteSpace(empId))
            {
                return null;
            }

            var emp = await _fsql.Select<hr_employee>()
                .Where(a => a.id == empId)
                .FirstAsync();

            if (emp == null)
            {
                return null;
            }

            return emp.default_city ?? string.Empty;
        }

        /// <summary>
        /// Sprint 4 Wave 1b #12：更新员工默认城市。
        /// 对应 A 侧 BLL.hr_employee.UpdateDefaultCity。
        /// 注意：不能用 <see cref="UpdateAsync"/>，因为其 IgnoreColumns 明确忽略 default_city 字段。
        /// </summary>
        /// <param name="empId">员工 ID</param>
        /// <param name="city">目标城市值</param>
        /// <returns>是否成功（受影响行数 &gt; 0）</returns>
        public async Task<bool> UpdateDefaultCityAsync(string empId, string city)
        {
            if (string.IsNullOrWhiteSpace(empId))
            {
                return false;
            }

            int rows = await _fsql.Update<hr_employee>()
                .Set(a => a.default_city, city ?? string.Empty)
                .Where(a => a.id == empId)
                .ExecuteAffrowsAsync();

            return rows > 0;
        }
    }
}
