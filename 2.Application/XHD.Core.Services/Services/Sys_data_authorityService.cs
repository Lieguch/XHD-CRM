using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Services
{
    /// <summary>
    /// 数据权限-指定部门 服务实现（缺口 E）
    /// </summary>
    internal class Sys_data_authorityService : BaseService<Sys_data_authority>, ISys_data_authorityService
    {
        private readonly ISys_data_authorityRepository _authorityRepository;

        public Sys_data_authorityService(ISys_data_authorityRepository repository)
        {
            _irepository = repository;
            _authorityRepository = repository;
        }

        /// <summary>
        /// 取角色已勾选的部门 id 列表。
        /// </summary>
        public async Task<List<string>> GetDepIdsByRoleIdAsync(string roleId)
        {
            if (string.IsNullOrWhiteSpace(roleId))
            {
                return new List<string>();
            }

            return await _authorityRepository.GetDepIdsByRoleIdAsync(roleId);
        }

        /// <summary>
        /// 保存角色的指定部门勾选（先删后插，单一事务）。
        /// </summary>
        public async Task<int> SaveAsync(string roleId, IEnumerable<string> depIds, string createId)
        {
            if (string.IsNullOrWhiteSpace(roleId))
            {
                return 0;
            }

            return await _authorityRepository.SaveAsync(roleId, depIds, createId);
        }
    }
}
