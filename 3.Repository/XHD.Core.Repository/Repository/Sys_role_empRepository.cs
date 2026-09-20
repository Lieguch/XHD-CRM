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
    public class Sys_role_empRepository : BaseRepository<Sys_role_emp>, ISys_role_empRepository
    {
        public Sys_role_empRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// Sprint 5 Wave 1 #31/#81：按员工 id 取关联角色 id 列表。
        /// </summary>
        public async Task<List<string>> GetRoleIdsByEmpIdAsync(string empId)
        {
            if (string.IsNullOrWhiteSpace(empId))
            {
                return new List<string>();
            }

            return await _fsql.Select<Sys_role_emp>()
                .Where(a => a.emp_id == empId)
                .ToListAsync(a => a.role_id);
        }

        /// <summary>
        /// Sprint 6 Wave 1 #110：按角色 id 批量添加员工绑定（去重后再插入）。
        /// 参数化，禁止字符串拼接。
        /// </summary>
        public async Task<int> AddBatchAsync(string roleId, IEnumerable<string> empIds)
        {
            if (string.IsNullOrWhiteSpace(roleId) || empIds == null)
            {
                return 0;
            }

            // 1. 过滤空值 + 去重
            var validEmpIds = empIds
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct()
                .ToList();

            if (validEmpIds.Count == 0)
            {
                return 0;
            }

            // 2. 查询已存在的 emp_id，避免重复插入
            var existing = await _fsql.Select<Sys_role_emp>()
                .Where(a => a.role_id == roleId)
                .Where(a => validEmpIds.Contains(a.emp_id))
                .ToListAsync(a => a.emp_id);

            var existingSet = new HashSet<string>(existing ?? new List<string>());
            var toAdd = validEmpIds.Where(e => !existingSet.Contains(e)).ToList();

            if (toAdd.Count == 0)
            {
                return 0;
            }

            // 3. 构造实体并批量插入
            var entities = toAdd.Select(empId => new Sys_role_emp
            {
                id = Guid.NewGuid().ToString(),
                role_id = roleId,
                emp_id = empId
            }).ToList();

            var rows = await _fsql.Insert(entities).ExecuteAffrowsAsync();
            return rows;
        }

        /// <summary>
        /// Sprint 6 Wave 1 #111：按角色 id 批量移除员工绑定。
        /// 参数化 IN 查询，避免字符串拼接。
        /// </summary>
        public async Task<int> RemoveBatchAsync(string roleId, IEnumerable<string> empIds)
        {
            if (string.IsNullOrWhiteSpace(roleId) || empIds == null)
            {
                return 0;
            }

            var validEmpIds = empIds
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct()
                .ToList();

            if (validEmpIds.Count == 0)
            {
                return 0;
            }

            var rows = await _fsql.Delete<Sys_role_emp>()
                .Where(a => a.role_id == roleId)
                .Where(a => validEmpIds.Contains(a.emp_id))
                .ExecuteAffrowsAsync();

            return rows;
        }

        /// <summary>
        /// Sprint 6 Wave 1 #113：按角色 id 取该角色下的员工 id 集合。
        /// </summary>
        public async Task<List<string>> GetEmpIdsByRoleIdAsync(string roleId)
        {
            if (string.IsNullOrWhiteSpace(roleId))
            {
                return new List<string>();
            }

            return await _fsql.Select<Sys_role_emp>()
                .Where(a => a.role_id == roleId)
                .ToListAsync(a => a.emp_id);
        }

        /// <summary>
        /// Sprint 6 Wave 1 #112：取不在指定角色下的员工 id 集合。
        /// 实现：直接返回该角色下的 emp_id 集合作为"排除集"，
        /// 由上层 Service 与 hr_employee 侧做 NOT IN 差集。
        /// 说明：FreeSql 直接写 NOT IN 子查询在 SQLite 上的翻译差异较多，
        /// 走两步查询更稳。
        /// </summary>
        public async Task<List<string>> GetEmpIdsNotInRoleAsync(string roleId)
        {
            if (string.IsNullOrWhiteSpace(roleId))
            {
                return new List<string>();
            }

            return await GetEmpIdsByRoleIdAsync(roleId);
        }
    }
}
