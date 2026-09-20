using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Common;
using XHD.Core.Models;
using System.Linq.Expressions;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// Sprint 6 Wave 1 员工角色控制器。
    /// 4 函数：#110 add / #111 remove / #112 emplist / #113 get。
    /// 与 Sprint 5 Wave 1 HrPostController.get-role 是**双向查询**——
    /// 后者按员工查角色，本控制器按角色查员工，语义反向、不合并。
    /// </summary>
    [Authorize]
    public class SysRoleEmpController : Controller
    {
        private readonly ILogger<SysRoleEmpController> _logger;
        private readonly ISys_role_empService _roleEmpService;
        private readonly ISys_logService _LogService;
        private readonly IDBAuthService _dBAuthService;

        public SysRoleEmpController(
            ILogger<SysRoleEmpController> logger,
            ISys_role_empService roleEmpService,
            ISys_logService logService,
            IDBAuthService dBAuthService)
        {
            _logger = logger;
            _roleEmpService = roleEmpService;
            _LogService = logService;
            _dBAuthService = dBAuthService;
        }

        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Sprint 6 Wave 1 #110：批量添加员工到角色。
        /// 对应 A 侧 Server.Sys_role_emp.add：解析 empids 逗号分隔字符串，
        /// 逐条 INSERT，并写 Sys_log（EventType="权限人员调整"）。
        /// B 侧参数化，去重后批量插入。
        /// </summary>
        /// <param name="role_id">角色 id</param>
        /// <param name="empids">员工 id 集合（逗号分隔字符串）</param>
        /// <returns>标准 XHDResult 字符串</returns>
        [HttpPost("add")]
        public async Task<string> Add(string role_id, string empids)
        {
            if (string.IsNullOrWhiteSpace(role_id))
            {
                return XHDResult.Error("角色ID无效").ToString();
            }

            if (string.IsNullOrWhiteSpace(empids))
            {
                return XHDResult.Error("员工ID不能为空").ToString();
            }

            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var authbtn = await _dBAuthService.GetAuth(userId, "Sys_role|edit");
            if (!authbtn)
            {
                authbtn = await _dBAuthService.GetAuth(userId, "Sys_role_emp|add");
            }
            if (!authbtn)
            {
                return XHDResult.Error("无权限！").ToString();
            }

            // 解析逗号分隔字符串（对齐 A 侧 TrimEnd(',') + Split(',')）
            var empIdList = empids
                .TrimEnd(',')
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (empIdList.Count == 0)
            {
                return XHDResult.Error("员工ID列表为空").ToString();
            }

            await _roleEmpService.AddBatchAsync(role_id, empIdList);

            // 写 Sys_log（权限人员调整）
            await WriteLogAsync(userId, "权限人员调整", role_id);

            return XHDResult.Success("添加成功").ToString();
        }

        /// <summary>
        /// Sprint 6 Wave 1 #111：批量从角色移除员工。
        /// 对应 A 侧 Server.Sys_role_emp.remove：DELETE WHERE RoleID=@rid AND empID IN (...)。
        /// 写 Sys_log（EventType="权限人员调整"）。
        /// </summary>
        /// <param name="role_id">角色 id</param>
        /// <param name="empids">员工 id 集合（逗号分隔字符串）</param>
        /// <returns>标准 XHDResult 字符串</returns>
        [HttpPost("remove")]
        public async Task<string> Remove(string role_id, string empids)
        {
            if (string.IsNullOrWhiteSpace(role_id))
            {
                return XHDResult.Error("角色ID无效").ToString();
            }

            if (string.IsNullOrWhiteSpace(empids))
            {
                return XHDResult.Error("员工ID不能为空").ToString();
            }

            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var authbtn = await _dBAuthService.GetAuth(userId, "Sys_role|edit");
            if (!authbtn)
            {
                authbtn = await _dBAuthService.GetAuth(userId, "Sys_role_emp|del");
            }
            if (!authbtn)
            {
                return XHDResult.Error("无权限！").ToString();
            }

            var empIdList = empids
                .TrimEnd(',')
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (empIdList.Count == 0)
            {
                return XHDResult.Error("员工ID列表为空").ToString();
            }

            await _roleEmpService.RemoveBatchAsync(role_id, empIdList);

            await WriteLogAsync(userId, "权限人员调整", role_id);

            return XHDResult.Success("移除成功").ToString();
        }

        /// <summary>
        /// Sprint 6 Wave 1 #112：查询不在指定角色下的员工列表（可分配候选）。
        /// 对应 A 侧 Server.Sys_role_emp.emplist：
        ///   id NOT IN (SELECT empID FROM Sys_role_emp WHERE RoleID=@rid) AND uid!='admin'。
        /// 可选 stext 模糊匹配员工 name；分页参数 page/pagesize 默认 1/30。
        /// </summary>
        /// <param name="role_id">角色 id</param>
        /// <param name="stext">姓名关键词（可选）</param>
        /// <param name="page">页码，默认 1</param>
        /// <param name="pagesize">每页大小，默认 30</param>
        /// <returns>标准 XHDResult 字符串，data 承载员工分页数组</returns>
        [HttpGet("emplist")]
        public async Task<string> Emplist(string role_id, string stext, int page = 1, int pagesize = 30)
        {
            if (page < 1) page = 1;
            if (pagesize < 1) pagesize = 1;
            if (pagesize > 200) pagesize = 200;

            var result = await _roleEmpService.GetEmployeesNotInRoleAsync(
                role_id ?? string.Empty, stext, page, pagesize);

            return result.ToString();
        }

        /// <summary>
        /// Sprint 6 Wave 1 #113：查询指定角色下的员工列表。
        /// 对应 A 侧 Server.Sys_role_emp.get：
        ///   id IN (SELECT empID FROM Sys_role_emp WHERE RoleID=@rid) AND uid!='admin'。
        /// 可选 stext 模糊匹配员工 name；分页参数 page/pagesize 默认 1/30。
        /// 注意：与 Sprint 5 Wave 1 HrPostController.get-role 是**反向**——
        /// 后者按员工查角色，本端点按角色查员工。
        /// </summary>
        /// <param name="role_id">角色 id</param>
        /// <param name="stext">姓名关键词（可选）</param>
        /// <param name="page">页码，默认 1</param>
        /// <param name="pagesize">每页大小，默认 30</param>
        /// <returns>标准 XHDResult 字符串，data 承载员工分页数组</returns>
        [HttpGet("get")]
        public async Task<string> Get(string role_id, string stext, int page = 1, int pagesize = 30)
        {
            if (page < 1) page = 1;
            if (pagesize < 1) pagesize = 1;
            if (pagesize > 200) pagesize = 200;

            var result = await _roleEmpService.GetEmployeesByRoleIdAsync(
                role_id ?? string.Empty, stext, page, pagesize);

            return result.ToString();
        }

        /// <summary>
        /// 取当前登录用户 ID（ClaimTypes.Sid）。
        /// </summary>
        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.Sid)?.Value;
        }

        /// <summary>
        /// 写权限调整日志。异常吞掉不阻断主流程（对齐 A 侧 Sys_log 失败不抛）。
        /// </summary>
        private async Task WriteLogAsync(string userId, string eventType, string eventId)
        {
            try
            {
                var log = new Sys_log
                {
                    id = UUIDNext.Uuid.NewSequential().ToString(),
                    EventDate = DateTime.Now,
                    UserID = userId ?? string.Empty,
                    UserName = User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty,
                    IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                    EventType = eventType,
                    EventID = eventId
                };

                await _LogService.AddAsync(log);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("写 Sys_log 失败：{Msg}", ex.Message);
            }
        }
    }
}
