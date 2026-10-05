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
    /// Sprint 10.26b：数据权限配置。
    ///
    /// 【语义 —— 对齐 A 版枚举】
    /// Sys_role.DataAuth：0=无 / 1=本人 / 2=本部 / 3=本部及下级 /
    /// 4=指定部门（跨部，由 Sys_data_authority 表记录勾选部门）/
    /// 5=全部。由 DBAuthRepository.GetDataAuth 按层级展开为可见员工 ID 列表。
    ///
    /// 本页切换角色的 DataAuth 层级；选 4 时通过 SysDataAuthority 弹窗勾选部门
    /// （对应 A 版 Sys_data_authorized.aspx 的组织架构树勾选）。
    /// </summary>
    [Authorize]
    public class DataAuthConfigController : Controller
    {
        private readonly ILogger<DataAuthConfigController> _logger;
        private readonly ISys_roleService _roleService;
        private readonly ISys_logService _logService;

        public DataAuthConfigController(
            ILogger<DataAuthConfigController> logger,
            ISys_roleService roleService,
            ISys_logService logService
            )
        {
            _logger = logger;
            _roleService = roleService;
            _logService = logService;
        }

        /// <summary>
        /// 数据权限配置页。
        /// </summary>
        [HttpGet("Index")]
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// 拉取所有角色的 DataAuth 层级，供前端表格渲染。
        /// 说明映射：0 无 / 1 本人 / 2 本部 / 3 本部及下级 / 4 指定部门 / 5 全部
        /// </summary>
        [HttpGet("Grid")]
        [ButtonAuth("sys_role", "edit")]
        public async Task<string> Grid()
        {
            var result = await _roleService.GridAsync(r => true, "RoleSort");
            var roles = result.data ?? new System.Collections.Generic.List<Sys_role>();

            var arr = new JArray();
            foreach (var r in roles)
            {
                arr.Add(new JObject
                {
                    ["id"] = r.id,
                    ["RoleName"] = r.RoleName,
                    ["RoleDscript"] = r.RoleDscript,
                    ["RoleSort"] = r.RoleSort ?? 0,
                    ["DataAuth"] = r.DataAuth ?? 0
                });
            }

            return XHDResult.Success(arr).ToString();
        }

        /// <summary>
        /// 保存单个角色的 DataAuth 层级（0-5）。
        /// </summary>
        [HttpPost("Save")]
        [ButtonAuth("sys_role", "edit", DenyMessage = "无权限！")]
        public async Task<string> Save(string role_id, int? DataAuth)
        {
            if (string.IsNullOrWhiteSpace(role_id))
            {
                return XHDResult.Error("参数错误：role_id 不能为空").ToString();
            }

            if (!DataAuth.HasValue || DataAuth.Value < 0 || DataAuth.Value > 5)
            {
                return XHDResult.Error("参数错误：DataAuth 必须是 0-5 之间的整数（4=指定部门，5=全部）").ToString();
            }

            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var old = (await _roleService.GridAsync(r => r.id == role_id, 1, 1)).data.FirstOrDefault();
            if (old == null)
            {
                return XHDResult.Error("找不到此角色！").ToString();
            }

            var oldAuth = old.DataAuth ?? 0;
            var newAuth = DataAuth.Value;

            old.DataAuth = newAuth;
            var updated = await _roleService.UpdateAsync(old);
            if (updated == 0)
            {
                return XHDResult.Error("操作失败，系统错误！").ToString();
            }

            // 写审计日志（层级变化时才写，减少噪音）
            if (oldAuth != newAuth)
            {
                await _logService.UpdateLog(new Sys_log
                {
                    id = Guid.NewGuid().ToString(),
                    EventType = "[数据权限]修改",
                    EventID = role_id,
                    EventTitle = old.RoleName,
                    UserID = userId,
                    UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                    IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    EventDate = DateTime.Now,
                    Log_Content = $"数据权限：{oldAuth}→{newAuth}"
                });
            }

            _logger.LogInformation(
                "DataAuthConfig.Save: role={RoleId} name={RoleName} {Old}→{New} by {User}",
                role_id, old.RoleName, oldAuth, newAuth, userId);

            return XHDResult.Success().ToString();
        }

        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.Sid)?.Value;
        }
    }
}
