using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq.Expressions;

namespace XHD.Core.IServices
{
    public interface ISys_role_empService : IBaseService<Sys_role_emp>
    {
        /// <summary>
        /// Sprint 5 Wave 1 #31/#81：按员工 id 取关联角色列表。
        /// 对应 A 侧 BLL.hr_employee.GetRole：
        ///   SELECT * FROM Sys_role WHERE Roleid IN
        ///     (SELECT Roleid FROM Sys_role_emp WHERE empid=@id)
        /// </summary>
        /// <param name="empId">员工 id</param>
        /// <returns>该员工关联的角色列表</returns>
        Task<List<Sys_role>> GetRolesByEmpIdAsync(string empId);
    }
}
