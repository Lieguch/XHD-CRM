
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Repository
{
    /// <summary>
    /// 系统错误日志仓储实现
    /// Sprint 8 #41 GetLogtypeAsync 去重返回 (Err_typeid, Err_type)
    /// </summary>
    public class Sys_log_ErrRepository : BaseRepository<Sys_log_Err>, ISys_log_ErrRepository
    {
        public Sys_log_ErrRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 按 typeid 去重返回错误类型字典，typeid 升序。
        /// </summary>
        /// <returns>错误类型列表</returns>
        public async Task<List<(int typeid, string type)>> GetLogtypeAsync()
        {
            var rows = await _fsql.Select<Sys_log_Err>()
                .Where(a => a.Err_typeid != null)
                .ToListAsync(a => new { Typeid = a.Err_typeid.Value, Type = a.Err_type });

            var result = new List<(int typeid, string type)>();
            if (rows == null)
            {
                return result;
            }

            var dict = new Dictionary<int, string>();
            foreach (var r in rows)
            {
                int tid = r.Typeid;
                string typeName = r.Type ?? string.Empty;
                if (!dict.ContainsKey(tid))
                {
                    dict[tid] = typeName;
                }
                else if (string.IsNullOrEmpty(dict[tid]) && !string.IsNullOrEmpty(typeName))
                {
                    dict[tid] = typeName;
                }
            }

            foreach (var kvp in dict.OrderBy(k => k.Key))
            {
                result.Add((kvp.Key, kvp.Value));
            }

            return result;
        }
    }
}
