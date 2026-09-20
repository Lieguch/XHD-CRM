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

        public Sys_role_empService(
            ISys_role_empRepository repository,
            ISys_roleRepository roleRepository)
        {
            _irepository = repository;
            _irepositoryBase = repository;
            _roleRepository = roleRepository;
        }

        /// <summary>
        /// Sprint 5 Wave 1 #31/#81：按员工 id 取关联角色列表。
        /// 语义等价 A 侧：SELECT * FROM Sys_role WHERE Roleid IN (SELECT Roleid FROM Sys_role_emp WHERE empid=@id)。
        /// 分两步查询：先取 role_id 集合，再按集合过滤 Sys_role；
        /// 避免 IN 子查询在 FreeSql 中的翻译差异。
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
    }
}
