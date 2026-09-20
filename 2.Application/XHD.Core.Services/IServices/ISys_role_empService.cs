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
        /// 对应 A 侧 BLL.hr_employee.GetRole。
        /// </summary>
        /// <param name="empId">员工 id</param>
        /// <returns>该员工关联的角色列表</returns>
        Task<List<Sys_role>> GetRolesByEmpIdAsync(string empId);

        /// <summary>
        /// Sprint 6 Wave 1 #110：批量添加员工到角色（去重后再插入）。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <param name="empIds">员工 id 集合</param>
        /// <returns>实际插入行数</returns>
        Task<int> AddBatchAsync(string roleId, IEnumerable<string> empIds);

        /// <summary>
        /// Sprint 6 Wave 1 #111：批量从角色移除员工。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <param name="empIds">员工 id 集合</param>
        /// <returns>实际删除行数</returns>
        Task<int> RemoveBatchAsync(string roleId, IEnumerable<string> empIds);

        /// <summary>
        /// Sprint 6 Wave 1 #113：按角色 id 取该角色下的员工列表（分页）。
        /// 排除 admin 账户，可选 stext 模糊匹配员工 name。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <param name="stext">姓名关键词（可选）</param>
        /// <param name="page">页码，从 1 开始</param>
        /// <param name="pageSize">每页大小</param>
        /// <returns>该角色下的员工分页结果</returns>
        Task<XHDData<hr_employee>> GetEmployeesByRoleIdAsync(
            string roleId, string stext, int page, int pageSize);

        /// <summary>
        /// Sprint 6 Wave 1 #112：按角色 id 取不在该角色下的员工列表（可分配候选）。
        /// 排除 admin 账户 + 已在角色下的员工，可选 stext 模糊匹配员工 name。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <param name="stext">姓名关键词（可选）</param>
        /// <param name="page">页码，从 1 开始</param>
        /// <param name="pageSize">每页大小</param>
        /// <returns>该角色外的员工分页结果</returns>
        Task<XHDData<hr_employee>> GetEmployeesNotInRoleAsync(
            string roleId, string stext, int page, int pageSize);
    }
}
