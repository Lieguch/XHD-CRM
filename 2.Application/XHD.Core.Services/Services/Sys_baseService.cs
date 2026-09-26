using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Linq.Expressions;

using XHD.Core.Models;
using XHD.Core.Common;
using XHD.Core.IRepository;
using XHD.Core.IServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XHD.Core.Services
{
    /// <summary>
    /// Sys_base 服务实现
    /// Sprint 7 新增：#100-#103 四函数。
    /// </summary>
    public class Sys_baseService : ISys_baseService
    {
        private readonly ISys_MenuRepository _menuRepo;
        private readonly ISys_onlineRepository _onlineRepo;
        private readonly ISys_authorityRepository _authRepo;
        private readonly ISys_role_empRepository _roleEmpRepo;
        private readonly Ihr_departmentRepository _deptRepo;
        private readonly Ihr_postRepository _postRepo;

        public Sys_baseService(
            ISys_MenuRepository menuRepo,
            ISys_onlineRepository onlineRepo,
            ISys_authorityRepository authRepo,
            ISys_role_empRepository roleEmpRepo,
            Ihr_departmentRepository deptRepo,
            Ihr_postRepository postRepo)
        {
            _menuRepo = menuRepo;
            _onlineRepo = onlineRepo;
            _authRepo = authRepo;
            _roleEmpRepo = roleEmpRepo;
            _deptRepo = deptRepo;
            _postRepo = postRepo;
        }

        /// <summary>
        /// #100 GetSysApp：按 App_id 取菜单，按 parentid 递归构建树。
        /// admin 全量；非 admin 与 Sys_authority 中 Menus 授权取交集。
        /// </summary>
        public async Task<JArray> GetSysAppAsync(string appid, string userId, bool isAdmin)
        {
            var result = new JArray();
            if (string.IsNullOrWhiteSpace(appid))
            {
                return result;
            }

            var menus = await _menuRepo.GetAllByAppAsync(appid);
            if (menus == null || menus.Count == 0)
            {
                return result;
            }

            IEnumerable<Sys_Menu> filtered = menus;
            if (!isAdmin)
            {
                var allowedMenuIds = await GetAllowedMenuIdsAsync(userId);
                if (allowedMenuIds.Count == 0)
                {
                    return result;
                }
                filtered = menus.Where(m => allowedMenuIds.Contains(m.id));
            }

            return BuildMenuTree(filtered.ToList(), "root");
        }

        private static JArray BuildMenuTree(List<Sys_Menu> menus, string parentId)
        {
            var result = new JArray();
            var children = menus
                .Where(m => m.parentid == parentId)
                .OrderBy(m => m.Menu_order ?? 0)
                .ToList();

            foreach (var m in children)
            {
                var obj = new JObject
                {
                    { "id", m.id },
                    { "text", m.Menu_name },
                    { "icon", m.Menu_icon },
                    { "url", m.Menu_url },
                    { "parentid", m.parentid },
                    { "order", m.Menu_order ?? 0 }
                };
                var subs = BuildMenuTree(menus, m.id);
                if (subs.Count > 0)
                {
                    obj["children"] = subs;
                }
                result.Add(obj);
            }
            return result;
        }

        private async Task<HashSet<string>> GetAllowedMenuIdsAsync(string userId)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return set;
            }

            var roleIds = await _roleEmpRepo.GetRoleIdsByEmpIdAsync(userId);
            if (roleIds == null || roleIds.Count == 0)
            {
                return set;
            }

            foreach (var rid in roleIds)
            {
                var auths = await _authRepo.GetByRoleAndTypeAsync(rid, 2);
                if (auths == null) continue;
                foreach (var a in auths)
                {
                    if (string.IsNullOrWhiteSpace(a.Auth_id)) continue;
                    foreach (var part in a.Auth_id.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = part.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            set.Add(trimmed);
                        }
                    }
                }
            }
            return set;
        }

        /// <summary>
        /// #101 getUserTree：部门+员工组织架构树。
        /// 先 touch 当前用户 + 清理僵尸；再遍历 hr_department 树，
        /// 每部门下挂该部门员工，在线标记 d_icon="37.png"，离线 d_icon="93.png"。
        /// </summary>
        public async Task<JArray> GetUserTreeAsync(string empId, string empName)
        {
            var result = new JArray();
            if (string.IsNullOrWhiteSpace(empId))
            {
                return result;
            }

            await _onlineRepo.TouchAsync(empId, empName);
            await _onlineRepo.PurgeStaleAsync(2);
            var onlineIds = await _onlineRepo.GetOnlineUserIdsAsync(2);
            var onlineSet = new HashSet<string>(onlineIds ?? new List<string>());

            var posts = await _postRepo.GridAsync(a => 1 == 1);
            var depts = await _deptRepo.GetAllAsync();

            if (depts == null || depts.Count == 0)
            {
                return result;
            }

            return BuildDeptTree(depts, posts ?? new List<hr_post>(), "root", onlineSet);
        }

        private static JArray BuildDeptTree(
            List<hr_department> depts,
            List<hr_post> posts,
            string parentId,
            HashSet<string> onlineSet)
        {
            var result = new JArray();
            var children = depts.Where(d => d.parentid == parentId).ToList();

            foreach (var d in children)
            {
                var obj = new JObject
                {
                    { "id", d.id },
                    { "text", d.dep_name },
                    { "d_icon", string.Empty }
                };

                var deptPosts = posts
                    .Where(p => p.dep_id == d.id && !string.IsNullOrWhiteSpace(p.emp_id))
                    .ToList();

                var empArray = new JArray();
                foreach (var p in deptPosts)
                {
                    empArray.Add(new JObject
                    {
                        { "id", p.id },
                        { "text", p.post_name },
                        { "d_icon", onlineSet.Contains(p.emp_id) ? "37.png" : "93.png" }
                    });
                }

                var subDepts = BuildDeptTree(depts, posts, d.id, onlineSet);

                var allChildren = new JArray();
                foreach (var item in empArray) allChildren.Add(item);
                foreach (var item in subDepts) allChildren.Add(item);

                if (allChildren.Count > 0)
                {
                    obj["children"] = allChildren;
                }
                result.Add(obj);
            }
            return result;
        }

        /// <summary>
        /// #102 GetOnline：touch + purge + 返回全部在线用户 JSON。
        /// </summary>
        public async Task<JArray> GetOnlineAsync(string empId, string empName)
        {
            var result = new JArray();
            if (string.IsNullOrWhiteSpace(empId))
            {
                return result;
            }

            await _onlineRepo.TouchAsync(empId, empName);
            await _onlineRepo.PurgeStaleAsync(2);
            var users = await _onlineRepo.GetAllAsync(2);

            foreach (var u in users)
            {
                result.Add(new JObject
                {
                    { "UserID", u.UserID },
                    { "UserName", u.UserName },
                    { "LastLogTime", u.LastLogTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty }
                });
            }
            return result;
        }

        /// <summary>
        /// #103 GetIcons：读物理目录文件列表。
        /// </summary>
        public async Task<JArray> GetIconsAsync(string iconRoot)
        {
            await Task.CompletedTask; // Sprint 10.30: 保留 async 签名以匹配接口；主体为同步目录读取
            var result = new JArray();
            try
            {
                var dir = string.IsNullOrWhiteSpace(iconRoot)
                    ? Path.Combine(AppContext.BaseDirectory, "wwwroot/images/icon")
                    : iconRoot;

                if (!Directory.Exists(dir))
                {
                    return result;
                }

                var files = Directory.GetFiles(dir);
                foreach (var f in files)
                {
                    result.Add(new JObject { { "filename", Path.GetFileName(f) } });
                }
            }
            catch (Exception ex)
            {
                _ = ex;
            }
            return result;
        }
    }
}
