using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using System.Security.Claims;
using Newtonsoft.Json.Linq;
using XHD.Core.IServices;
using XHD.Core.Common;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// Sys_base 系统基础控制器
    /// Sprint 7 新增：#100 GetSysApp / #101 getUserTree / #102 GetOnline / #103 GetIcons。
    /// </summary>
    [Authorize]
    public class SysBaseController : Controller
    {
        private readonly ILogger<SysBaseController> _logger;
        private readonly ISys_baseService _service;

        public SysBaseController(
            ILogger<SysBaseController> logger,
            ISys_baseService service)
        {
            _logger = logger;
            _service = service;
        }

        /// <summary>
        /// Sprint 7 #100 GetSysApp：按 App_id 取菜单树。
        /// admin 全量；非 admin 与 Sys_authority.Menus 授权取交集。
        /// </summary>
        /// <param name="appid">应用 id</param>
        /// <returns>标准 XHDResult 字符串（data 为菜单树 JSON 数组）</returns>
        [HttpGet("GetSysApp")]
        public async Task<string> GetSysApp(string appid)
        {
            var userId = GetUserId();
            var isAdmin = GetUserUid() == "admin";

            var tree = await _service.GetSysAppAsync(appid ?? string.Empty, userId ?? string.Empty, isAdmin);
            return XHDResult.Success(tree).ToString();
        }

        /// <summary>
        /// Sprint 7 #101 getUserTree：部门+员工组织架构树（含在线标记）。
        /// </summary>
        [HttpGet("UserTree")]
        public async Task<string> UserTree()
        {
            var userId = GetUserId();
            var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var tree = await _service.GetUserTreeAsync(userId, userName);
            return XHDResult.Success(tree).ToString();
        }

        /// <summary>
        /// Sprint 7 #102 GetOnline：更新当前用户在线状态 + 返回全部在线用户。
        /// </summary>
        [HttpGet("GetOnline")]
        public async Task<string> GetOnline()
        {
            var userId = GetUserId();
            var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var list = await _service.GetOnlineAsync(userId, userName);
            return XHDResult.Success(list).ToString();
        }

        /// <summary>
        /// Sprint 7 #103 GetIcons：图标文件列表（读 ~/images/icon/ 目录）。
        /// 目录缺失时返回空数组。
        /// </summary>
        [HttpGet("Icons")]
        public async Task<string> Icons()
        {
            var icons = await _service.GetIconsAsync(null);
            return XHDResult.Success(icons).ToString();
        }

        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.Sid)?.Value;
        }

        private string GetUserUid()
        {
            return User.FindFirst("uid")?.Value;
        }
    }
}
