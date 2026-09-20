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
    }
}
