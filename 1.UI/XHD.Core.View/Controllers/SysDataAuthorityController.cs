using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.View.Authorization;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// 缺口 E：数据权限「指定部门」（Sys_data_authority）。
    ///
    /// 【契约来源】A 版 Server/Sys_data_authority.cs + View/System/sysmanager/Sys_data_authorized.aspx：
    /// <list type="bullet">
    /// <item>get(Role_id)：返回该角色勾选的部门行列表（dep_id）</item>
    /// <item>save(id, depids)：DELETE WHERE Role_id=id 后按逗号分割 depids 逐行 INSERT，并写 Sys_log</item>
    /// </list>
    /// 配合 Sys_role.DataAuth == 4（指定部门）使用；DBAuthRepository.GetDataAuth 的
    /// case 4 消费本表展开可见员工 id。
    /// </summary>
    [Authorize]
    public class SysDataAuthorityController : Controller
    {
        private readonly ILogger<SysDataAuthorityController> _logger;
        private readonly ISys_data_authorityService _authorityService;
        private readonly ISys_roleService _roleService;
        private readonly ISys_logService _logService;

        public SysDataAuthorityController(
            ILogger<SysDataAuthorityController> logger,
            ISys_data_authorityService authorityService,
            ISys_roleService roleService,
            ISys_logService logService)
        {
            _logger = logger;
            _authorityService = authorityService;
            _roleService = roleService;
            _logService = logService;
        }

        /// <summary>
        /// 指定部门勾选页（组织架构树 + checkbox）。
        /// 由 SysRole/Add 或 DataAuthConfig 在 DataAuth==4 时以 layer.open iframe 载入。
        /// </summary>
        [HttpGet("Index")]
        public IActionResult Index(string roleId)
        {
            ViewData["roleId"] = roleId ?? string.Empty;
            return View();
        }

        /// <summary>
        /// 取角色已勾选的部门 id 列表。
        /// </summary>
        [HttpGet("Get")]
        public async Task<string> Get(string roleId)
        {
            if (string.IsNullOrWhiteSpace(roleId))
            {
                return XHDResult.Error("参数错误：roleId 不能为空").ToString();
            }

            var depIds = await _authorityService.GetDepIdsByRoleIdAsync(roleId);

            return XHDResult.Success(JArray.FromObject(depIds)).ToString();
        }

        /// <summary>
        /// 保存角色的指定部门勾选（先删后插）+ 审计日志。
        /// </summary>
        [HttpPost("Save")]
        [ButtonAuth("sys_role", "edit", DenyMessage = "无权限！")]
        public async Task<string> Save(string role_id, string depids)
        {
            if (string.IsNullOrWhiteSpace(role_id))
            {
                return XHDResult.Error("参数错误：role_id 不能为空").ToString();
            }

            var userId = User.FindFirst(ClaimTypes.Sid)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var role = (await _roleService.GridAsync(r => r.id == role_id, 1, 1)).data.FirstOrDefault();
            if (role == null)
            {
                return XHDResult.Error("找不到此角色！").ToString();
            }

            // A 版按逗号分割 depids；空字符串 = 清空勾选
            var depIdList = (depids ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .ToList();

            int inserted = await _authorityService.SaveAsync(role_id, depIdList, userId);

            // 审计日志（对齐 A 版 EventType="数据权限修改"、EventTitle=角色名）
            await _logService.UpdateLog(new Sys_log
            {
                id = Guid.NewGuid().ToString(),
                EventType = "数据权限修改",
                EventID = role_id,
                EventTitle = role.RoleName,
                UserID = userId,
                UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString(),
                EventDate = DateTime.Now,
                Log_Content = $"指定部门：勾选 {inserted} 个部门"
            });

            _logger.LogInformation(
                "SysDataAuthority.Save: role={RoleId} name={RoleName} deps={Count} by={User}",
                role_id, role.RoleName, inserted, userId);

            return XHDResult.Success().ToString();
        }
    }
}
