using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq.Expressions;

using XHD.Core.Models;
using XHD.Core.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XHD.Core.IServices
{
    /// <summary>
    /// Sys_base 服务接口
    /// Sprint 7 新增：#100-#103 四函数集中实现。
    /// </summary>
    public interface ISys_baseService
    {
        /// <summary>
        /// Sprint 7 #100 GetSysApp：按 App_id 取菜单树（admin 全量 / 非 admin 过滤权限）。
        /// </summary>
        /// <param name="appid">应用 id</param>
        /// <param name="userId">当前员工 id</param>
        /// <param name="isAdmin">是否 admin（uid == "admin"）</param>
        /// <returns>菜单树 JSON 数组</returns>
        Task<JArray> GetSysAppAsync(string appid, string userId, bool isAdmin);

        /// <summary>
        /// Sprint 7 #101 getUserTree：取部门+员工组织架构树，含在线标记。
        /// </summary>
        /// <param name="empId">当前员工 id</param>
        /// <param name="empName">当前员工姓名</param>
        /// <returns>组织架构树 JSON 数组</returns>
        Task<JArray> GetUserTreeAsync(string empId, string empName);

        /// <summary>
        /// Sprint 7 #102 GetOnline：touch 当前用户 + 清理僵尸 + 返回在线用户列表。
        /// </summary>
        /// <param name="empId">当前员工 id</param>
        /// <param name="empName">当前员工姓名</param>
        /// <returns>在线用户列表（JSON 数组）</returns>
        Task<JArray> GetOnlineAsync(string empId, string empName);

        /// <summary>
        /// Sprint 7 #103 GetIcons：读物理目录 ~/images/icon/ 下所有文件名。
        /// </summary>
        /// <param name="iconRoot">图标根目录绝对路径（可空，走默认）</param>
        /// <returns>图标文件名列表 JSON 数组</returns>
        Task<JArray> GetIconsAsync(string iconRoot);
    }
}
