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
    public interface IDBAuthRepository
    {
        Task<int> GetAuthType(string emp_id);

        Task<bool> GetAuth(string emp_id, string auth_id);

        Task<XHDRoleData> GetDataAuth(string emp_id);

        /// <summary>
        /// 公客修改权限（对应 A 版 Controller/GetDataAuth.cs:40-60 getPrivateCusEdit）：
        /// 取该用户所有角色 PublicAuth 的最大值，&gt;0 即具备公客修改权限。
        /// </summary>
        /// <param name="emp_id">员工ID</param>
        /// <returns>true=可修改公客</returns>
        Task<bool> GetPrivateCusEdit(string emp_id);
    }
}
