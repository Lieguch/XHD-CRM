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
    public interface ISys_role_empRepository : IXHDBaseRepository<Sys_role_emp>
    {
        /// <summary>
        /// Sprint 5 Wave 1 #31/#81：按员工 id 取关联角色 id 列表。
        /// 对应 A 侧 DAL.hr_employee.GetRole 中的子查询：
        /// SELECT Roleid FROM Sys_role_emp WHERE empid=@id。
        /// </summary>
        /// <param name="empId">员工 id</param>
        /// <returns>角色 id 列表（不含空）</returns>
        Task<List<string>> GetRoleIdsByEmpIdAsync(string empId);
    }
}
