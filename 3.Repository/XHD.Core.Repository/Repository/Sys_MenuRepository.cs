using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Repository
{
    public class Sys_MenuRepository : BaseRepository<Sys_Menu>, ISys_MenuRepository
    {
        public Sys_MenuRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        public async Task<List<string>> GetMenuByEmpID(string emp_id)
        {
            if (string.IsNullOrWhiteSpace(emp_id))
            {
                return new List<string>();
            }

            // Sprint 10.39 #183 根因修复（活体故障）：
            // 旧实现用 hr_employee.id（员工id）去比 Sys_authority.Role_id（角色id）：
            //   List<string> rolelist = Select<hr_employee>().Where(a => a.id == emp_id)
            //                                        .ToListAsync(a => a.id);   ← 取的是员工id
            //   ...Where(a => rolelist.Contains(a.Role_id))                      ← 拿员工id 比角色id
            // 员工id 与角色id 命名空间永不相交 ⇒ 永远查不到任何授权 ⇒ 返回空菜单树。
            // 调用点 HomeController.cs:133-141 对非 admin 用户执行
            //   menulist = GetMenuByEmpID(emp_id); expWhere = expWhere.And(a => menulist.Contains(a.id));
            // ⇒ 所有非 admin 用户首页菜单为空，"系统只有 admin 能用"的真正主因即在此。
            // 正确实现见同仓 Sys_baseService.GetAllowedMenuIdsAsync（多角色表路径，已被单测覆盖）。

            var roleIds = new List<string>();

            // 路径 1：旧单值字段 hr_employee.role_id（存量数据仍在使用）
            var legacyRoleId = await _fsql.Select<hr_employee>()
                .Where(e => e.id == emp_id)
                .FirstAsync(e => e.role_id);
            if (!string.IsNullOrWhiteSpace(legacyRoleId))
            {
                roleIds.Add(legacyRoleId);
            }

            // 路径 2：Sprint 5 新增的多角色关联表 Sys_role_emp（一名员工可绑多个角色）
            var multiRoleIds = await _fsql.Select<Sys_role_emp>()
                .Where(r => r.emp_id == emp_id && (r.isDelete == null || r.isDelete == 0))
                .ToListAsync(r => r.role_id);
            if (multiRoleIds != null)
            {
                foreach (var rid in multiRoleIds)
                {
                    if (!string.IsNullOrWhiteSpace(rid)) roleIds.Add(rid);
                }
            }

            roleIds = roleIds
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct()
                .ToList();

            if (roleIds.Count == 0)
            {
                return new List<string>();
            }

            // Auth_type == 2 表示"菜单"类授权（与 Sys_baseService.GetAllowedMenuIdsAsync 的取值一致）
            List<string> authIds = await _fsql.Select<Sys_authority>()
                .Where(a => roleIds.Contains(a.Role_id) && a.Auth_type == 2)
                .ToListAsync(a => a.Auth_id);

            // Sys_authority.Auth_id 可能以逗号存多个菜单id，需展开（与 Sys_baseService 处理一致）
            var menulist = new List<string>();
            foreach (var authId in authIds ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(authId)) continue;
                foreach (var part in authId.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !menulist.Contains(trimmed))
                    {
                        menulist.Add(trimmed);
                    }
                }
            }

            return menulist;
        }

        /// <summary>
        /// Sprint 7 #100 GetSysApp：按 App_id 取全部菜单。
        /// </summary>
        public async Task<List<Sys_Menu>> GetAllByAppAsync(string appid)
        {
            if (string.IsNullOrWhiteSpace(appid))
            {
                return new List<Sys_Menu>();
            }
            return await _fsql.Select<Sys_Menu>()
                .Where(a => a.App_id == appid)
                .OrderBy(a => a.Menu_order)
                .ToListAsync();
        }
    }
}
