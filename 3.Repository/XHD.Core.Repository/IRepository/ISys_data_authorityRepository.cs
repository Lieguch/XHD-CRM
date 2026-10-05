using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.IRepository
{
    /// <summary>
    /// 数据权限-指定部门 仓储（缺口 E）
    /// </summary>
    public interface ISys_data_authorityRepository : IXHDBaseRepository<Sys_data_authority>
    {
        /// <summary>
        /// 取角色已勾选的部门 id 列表。
        /// 对应 A 侧 Server/Sys_data_authority.get：
        /// SELECT * FROM Sys_data_authority WHERE Role_id=@id（取 dep_id 列）。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <returns>部门 id 列表（不含空；角色不存在或未勾选时返回空列表）</returns>
        Task<List<string>> GetDepIdsByRoleIdAsync(string roleId);

        /// <summary>
        /// 保存角色的指定部门勾选：先删后插（单一事务）。
        /// 对应 A 侧 Server/Sys_data_authority.save：
        /// DELETE FROM Sys_data_authority WHERE Role_id=@id，再按 depids 逐行 INSERT。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <param name="depIds">勾选的部门 id 集合（空集合 = 清空该角色指定部门）</param>
        /// <param name="createId">操作人员工 id（写入 create_id）</param>
        /// <returns>实际插入行数</returns>
        Task<int> SaveAsync(string roleId, IEnumerable<string> depIds, string createId);
    }
}
