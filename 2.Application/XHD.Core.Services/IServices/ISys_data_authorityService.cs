using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.IServices
{
    /// <summary>
    /// 数据权限-指定部门 服务（缺口 E）
    /// </summary>
    public interface ISys_data_authorityService : IBaseService<Sys_data_authority>
    {
        /// <summary>
        /// 取角色已勾选的部门 id 列表。
        /// 对应 A 侧 Server/Sys_data_authority.get。
        /// </summary>
        Task<List<string>> GetDepIdsByRoleIdAsync(string roleId);

        /// <summary>
        /// 保存角色的指定部门勾选（先删后插，单一事务）。
        /// 对应 A 侧 Server/Sys_data_authority.save。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <param name="depIds">勾选的部门 id 集合</param>
        /// <param name="createId">操作人员工 id</param>
        /// <returns>实际插入行数</returns>
        Task<int> SaveAsync(string roleId, IEnumerable<string> depIds, string createId);
    }
}
