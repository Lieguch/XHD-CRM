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

        /// <summary>
        /// Sprint 6 Wave 1 #110：按角色 id 批量添加员工绑定。
        /// 对应 A 侧 Server.Sys_role_emp.add：遍历 empids 逐条 INSERT。
        /// B 侧参数化，先去重已存在的 (role_id, emp_id) 再插入，避免重复数据。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <param name="empIds">员工 id 集合</param>
        /// <returns>实际插入行数</returns>
        Task<int> AddBatchAsync(string roleId, IEnumerable<string> empIds);

        /// <summary>
        /// Sprint 6 Wave 1 #111：按角色 id 批量移除员工绑定。
        /// 对应 A 侧 Server.Sys_role_emp.remove：DELETE WHERE RoleID=@rid AND empID IN (...)。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <param name="empIds">员工 id 集合</param>
        /// <returns>实际删除行数</returns>
        Task<int> RemoveBatchAsync(string roleId, IEnumerable<string> empIds);

        /// <summary>
        /// Sprint 6 Wave 1 #113：按角色 id 取该角色下的员工 id 集合。
        /// 对应 A 侧 Server.Sys_role_emp.get 中的子查询：
        /// SELECT empID FROM Sys_role_emp WHERE RoleID=@rid。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <returns>员工 id 集合</returns>
        Task<List<string>> GetEmpIdsByRoleIdAsync(string roleId);

        /// <summary>
        /// Sprint 6 Wave 1 #112：取不在指定角色下的员工 id 集合（角色外员工候选）。
        /// 用于 emplist 反向查询；A 侧 emplist 语义是 "NOT IN 该角色下的员工 + uid!='admin'"。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <returns>该角色外员工 id 集合</returns>
        Task<List<string>> GetEmpIdsNotInRoleAsync(string roleId);
    }
}
