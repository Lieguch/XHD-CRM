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
    public interface ISys_MenuRepository: IXHDBaseRepository<Sys_Menu>
    {
        Task<List<string>> GetMenuByEmpID(string emp_id);

        /// <summary>
        /// Sprint 7 #100 GetSysApp：按 App_id 取全部菜单（按 Menu_order 排序）。
        /// </summary>
        Task<List<Sys_Menu>> GetAllByAppAsync(string appid);
    }
}
