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

namespace XHD.Core.Repository
{
    public class Sys_ParamRepository : BaseRepository<Sys_Param>, ISys_ParamRepository
    {
        public Sys_ParamRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        public async new Task<XHDData<Sys_Param>> GridAsync(Expression<Func<Sys_Param, bool>> expWhere, int Page, int Limit)
        {
            var data = await _fsql.Select<Sys_Param>()
                .LeftJoin(a => a.ParamType.id == a.params_type)
                .Where(expWhere)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync();

            XHDData<Sys_Param> result = new XHDData<Sys_Param>()
            {
                data = data,
                count = total
            };

            return result;
        }

        /// <summary>
        /// 分页查询（带排序）
        /// </summary>
        public async new Task<XHDData<Sys_Param>> GridAsync(Expression<Func<Sys_Param, bool>> expWhere, int Page, int Limit, string orderby)
        {
            if (string.IsNullOrWhiteSpace(orderby))
            {
                return await GridAsync(expWhere, Page, Limit);
            }

            var data = await _fsql.Select<Sys_Param>()
                .LeftJoin(a => a.ParamType.id == a.params_type)
                .Where(expWhere)
                .OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync();

            XHDData<Sys_Param> result = new XHDData<Sys_Param>()
            {
                data = data,
                count = total
            };

            return result;
        }

        /// <summary>
        /// 普通条件查询
        /// </summary>
        public async new Task<List<Sys_Param>> GridAsync(Expression<Func<Sys_Param, bool>> expWhere)
        {
            var data = await _fsql.Select<Sys_Param>()
                .LeftJoin(a => a.ParamType.id == a.params_type)
                .Where(expWhere)
                .ToListAsync();

            return data;
        }

        /// <summary>
        /// 普通条件查询带排序
        /// </summary>
        public async new Task<List<Sys_Param>> GridAsync(Expression<Func<Sys_Param, bool>> expWhere, string OrderBy)
        {
            if (string.IsNullOrWhiteSpace(OrderBy))
            {
                return await GridAsync(expWhere);
            }

            var data = await _fsql.Select<Sys_Param>()
                .LeftJoin(a => a.ParamType.id == a.params_type)
                .Where(expWhere)
                .OrderBy(OrderBy)
                .ToListAsync();

            return data;
        }

        /// <summary>
        /// Sprint 6 Wave 1 #116：参数名唯一性校验。
        /// 参数化，禁止字符串拼接。excludeId 为空或 "root" 时不排除任何行。
        /// </summary>
        public async Task<bool> ValidateNameAsync(string paramName, string parentId, string excludeId)
        {
            if (string.IsNullOrWhiteSpace(paramName))
            {
                // 参数名为空 → 语义上一定重（同 parent 下必然存在空名字段），返回 false 阻止
                return false;
            }

            var effectiveExclude = string.IsNullOrWhiteSpace(excludeId) || excludeId == "root"
                ? string.Empty
                : excludeId;

            var count = await _fsql.Select<Sys_Param>()
                .Where(a => a.params_name == paramName)
                .Where(a => a.params_type == parentId)
                .Where(a => a.id != effectiveExclude)
                .CountAsync();

            // count == 0 → 唯一可用（true）；count > 0 → 重名（false）
            return count == 0;
        }
    }
}
