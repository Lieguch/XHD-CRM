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
    public interface ISys_authorityRepository: IXHDBaseRepository<Sys_authority>
    {
        /// <summary>
        /// Sprint 7 #100 GetSysApp：按角色 id + 权限类型取权限记录。
        /// 用于非 admin 用户菜单过滤（Auth_type=2 = 菜单）。
        /// </summary>
        /// <param name="roleId">角色 id</param>
        /// <param name="authType">权限类型（1=按钮, 2=菜单）</param>
        /// <returns>匹配记录</returns>
        Task<List<Sys_authority>> GetByRoleAndTypeAsync(string roleId, int authType);
    }
}
