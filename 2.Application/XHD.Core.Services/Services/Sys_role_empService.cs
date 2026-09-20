using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

namespace XHD.Core.Services
{
    internal class Sys_role_empService : BaseService<Sys_role_emp>, ISys_role_empService
    {
        private readonly ISys_role_empRepository _irepositoryBase;
        private readonly ISys_roleRepository _roleRepository;
        private readonly Ihr_employeeRepository _employeeRepository;

        public Sys_role_empService(
            ISys_role_empRepository repository,
            ISys_roleRepository roleRepository,
            Ihr_employeeRepository employeeRepository)
        {
            _irepository = repository;
            _irepositoryBase = repository;
            _roleRepository = roleRepository;
            _employeeRepository = employeeRepository;
        }

        /// <summary>
        /// Sprint 5 Wave 1 #31/#81：按员工 id 取关联角色列表。
        /// 语义等价 A 侧：SELECT * FROM Sys_role WHERE Roleid IN (SELECT Roleid FROM Sys_role_emp WHERE empid=@id)。
        /// </summary>
        public async Task<List<Sys_role>> GetRolesByEmpIdAsync(string empId)
        {
            if (string.IsNullOrWhiteSpace(empId))
            {
                return new List<Sys_role>();
            }

            var roleIds = await _irepositoryBase.GetRoleIdsByEmpIdAsync(empId);

            if (roleIds == null || roleIds.Count == 0)
            {
                return new List<Sys_role>();
            }

            return await _roleRepository.GridAsync(a => roleIds.Contains(a.id));
        }

        /// <summary>
        /// Sprint 6 Wave 1 #110：批量添加员工到角色（去重后再插入）。
        /// 委托 Repository 执行。
        /// </summary>
        public async Task<int> AddBatchAsync(string roleId, IEnumerable<string> empIds)
        {
            return await _irepositoryBase.AddBatchAsync(roleId, empIds);
        }

        /// <summary>
        /// Sprint 6 Wave 1 #111：批量从角色移除员工。
        /// 委托 Repository 执行。
        /// </summary>
        public async Task<int> RemoveBatchAsync(string roleId, IEnumerable<string> empIds)
        {
            return await _irepositoryBase.RemoveBatchAsync(roleId, empIds);
        }

        /// <summary>
        /// Sprint 6 Wave 1 #113：按角色 id 取该角色下的员工列表。
        /// 排除 admin 账户（对齐 A 侧 uid != 'admin'）。
        /// 可选 stext 模糊匹配员工 name。
        /// 分页返回。
        /// </summary>
        public async Task<XHDData<hr_employee>> GetEmployeesByRoleIdAsync(
            string roleId, string stext, int page, int pageSize)
        {
            var empty = new XHDData<hr_employee> { data = new List<hr_employee>(), count = 0 };

            if (string.IsNullOrWhiteSpace(roleId))
            {
                return empty;
            }

            var empIds = await _irepositoryBase.GetEmpIdsByRoleIdAsync(roleId);
            if (empIds == null || empIds.Count == 0)
            {
                return empty;
            }

            Expression<Func<hr_employee, bool>> exp = a =>
                empIds.Contains(a.id) && a.uid != "admin";

            if (!string.IsNullOrWhiteSpace(stext))
            {
                var kw = stext.Trim();
                exp = exp.And(a => a.name.Contains(kw));
            }

            return await _employeeRepository.GridAsync(exp, page, pageSize);
        }

        /// <summary>
        /// Sprint 6 Wave 1 #112：按角色 id 取不在该角色下的员工列表（可分配候选）。
        /// 排除 admin 账户 + 已在角色下的员工。
        /// 可选 stext 模糊匹配员工 name。
        /// </summary>
        public async Task<XHDData<hr_employee>> GetEmployeesNotInRoleAsync(
            string roleId, string stext, int page, int pageSize)
        {
            var empty = new XHDData<hr_employee> { data = new List<hr_employee>(), count = 0 };

            if (string.IsNullOrWhiteSpace(roleId))
            {
                // roleId 为空 → 语义上无"该角色"，返回全员（除 admin）作为候选
                Expression<Func<hr_employee, bool>> expAll = a => a.uid != "admin";
                if (!string.IsNullOrWhiteSpace(stext))
                {
                    var kw = stext.Trim();
                    expAll = expAll.And(a => a.name.Contains(kw));
                }
                return await _employeeRepository.GridAsync(expAll, page, pageSize);
            }

            var empIds = await _irepositoryBase.GetEmpIdsNotInRoleAsync(roleId);
            var excludeSet = new HashSet<string>(empIds ?? new List<string>());

            Expression<Func<hr_employee, bool>> exp = a =>
                !excludeSet.Contains(a.id) && a.uid != "admin";

            if (!string.IsNullOrWhiteSpace(stext))
            {
                var kw = stext.Trim();
                exp = exp.And(a => a.name.Contains(kw));
            }

            return await _employeeRepository.GridAsync(exp, page, pageSize);
        }
    }
}
