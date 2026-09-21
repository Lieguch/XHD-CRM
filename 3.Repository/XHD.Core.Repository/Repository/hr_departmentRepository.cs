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
    public class hr_departmentRepository : BaseRepository<hr_department>, Ihr_departmentRepository
    {
        public hr_departmentRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// Sprint 7 #101 getUserTree：取全部部门（按 dep_order 排序）。
        /// </summary>
        public async Task<List<hr_department>> GetAllAsync()
        {
            return await _fsql.Select<hr_department>()
                .OrderBy(a => a.dep_order)
                .ToListAsync();
        }
    }
}
