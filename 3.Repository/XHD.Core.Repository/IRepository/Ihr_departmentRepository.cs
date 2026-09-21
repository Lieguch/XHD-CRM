using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;


namespace XHD.Core.IRepository
{
    public interface Ihr_departmentRepository: IXHDBaseRepository<hr_department>
    {
        /// <summary>
        /// Sprint 7 #101 getUserTree：取全部部门（按 dep_order 排序）。
        /// </summary>
        Task<List<hr_department>> GetAllAsync();
    }
}
