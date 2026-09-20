using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
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
using XHD.Core.View.Configs;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// Sprint 5 Wave 1 岗位/员工控制器。
    /// 8 函数：#27 UpdatePost / #31+#81 GetRole / #34 UpdatePostEmp /
    /// #35 UpdatePostEmpbyEid / #88 getpostbyempid / #89 serch / #90 postemp。
    /// B 侧原本完全缺失 hr_post 表与 Sys_role_emp 表，本控制器独立承担，
    /// 避免污染已落地的 HrPositionController（hr_position 表）与 HrEmployeeController
    /// （Sprint 4 已锁定其构造函数签名）。
    /// </summary>
    [Authorize]
    public class HrPostController : Controller
    {
        private readonly ILogger<HrPostController> _logger;
        private readonly Ihr_postService _service;
        private readonly Ihr_employeeService _employeeservice;
        private readonly ISys_role_empService _roleEmpService;
        private readonly ISys_logService _LogService;
        private readonly IDBAuthService _dBAuthService;

        public HrPostController(
            ILogger<HrPostController> logger,
            Ihr_postService service,
            Ihr_employeeService employeeservice,
            ISys_role_empService roleEmpService,
            ISys_logService logService,
            IDBAuthService dBAuthService)
        {
            _logger = logger;
            _service = service;
            _employeeservice = employeeservice;
            _roleEmpService = roleEmpService;
            _LogService = logService;
            _dBAuthService = dBAuthService;
        }

        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Sprint 5 Wave 1 #27：变更员工岗位三元组（dep_id / post_id / position_id）。
        /// 对应 A 侧 BLL.hr_employee.UpdatePost → DAL.hr_employee.UpdatePost。
        /// </summary>
        /// <param name="id">员工 id</param>
        /// <param name="depId">目标部门 id</param>
        /// <param name="postId">目标岗位 id</param>
        /// <param name="positionId">目标职务级别 id</param>
        [HttpPost("update-post")]
        public async Task<string> UpdatePost(string id, string depId, string postId, string positionId)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return XHDResult.Error("员工ID无效").ToString();
            }

            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            // 权限：hr_post|edit 与 hr_employee|edit 二者之一即可（对齐 A 侧无显式按钮鉴权，但 B 侧保持最小权限）
            var auth = await _dBAuthService.GetAuth(userId, "hr_post|edit");
            if (!auth)
            {
                auth = await _dBAuthService.GetAuth(userId, "hr_employee|edit");
            }
            if (!auth)
            {
                return XHDResult.Error("无权限！").ToString();
            }

            var ok = await _employeeservice.UpdatePostAsync(id, depId, postId, positionId);
            return ok
                ? XHDResult.Success("更新成功").ToString()
                : XHDResult.Error("员工不存在或更新失败").ToString();
        }

        /// <summary>
        /// Sprint 5 Wave 1 #31+#81：按员工 id 取关联角色列表。
        /// 合并 A 侧 BLL.hr_employee.GetRole（DAL 层）与 Server.hr_employee.getRole（Web 层）。
        /// 参数校验语义对齐 A 侧：empid 非合法 id 时返回 "{}"。
        /// </summary>
        [HttpGet("get-role")]
        public async Task<string> GetRole(string empid)
        {
            if (string.IsNullOrWhiteSpace(empid))
            {
                return "{}";
            }

            var roles = await _roleEmpService.GetRolesByEmpIdAsync(empid);

            var arr = new JArray();
            foreach (var r in roles)
            {
                arr.Add(JObject.FromObject(r));
            }

            return arr.ToString();
        }

        /// <summary>
        /// Sprint 5 Wave 1 #34：更新岗位的员工绑定（emp_id 与 default_post）。
        /// 对应 A 侧 DAL.hr_post.UpdatePostEmp。
        /// </summary>
        [HttpPost("update-post-emp")]
        public async Task<string> UpdatePostEmp(string id, string empId, int? defaultPost)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return XHDResult.Error("岗位ID无效").ToString();
            }

            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var authbtn = await _dBAuthService.GetAuth(userId, "hr_post|edit");
            if (!authbtn)
            {
                return XHDResult.Error("无权限！").ToString();
            }

            var ok = await _service.UpdatePostEmpAsync(id, empId, defaultPost);
            return ok
                ? XHDResult.Success("更新成功").ToString()
                : XHDResult.Error("岗位不存在或更新失败").ToString();
        }

        /// <summary>
        /// Sprint 5 Wave 1 #35：清空员工所有岗位分配（emp_id='', default_post=0）。
        /// 对应 A 侧 DAL.hr_post.UpdatePostEmpbyEid。
        /// 语义对齐 A 侧：rows==0 视为失败（该员工没有任何岗位记录）。
        /// </summary>
        [HttpPost("update-post-empby-eid")]
        public async Task<string> UpdatePostEmpbyEid(string empid)
        {
            if (string.IsNullOrWhiteSpace(empid))
            {
                return XHDResult.Error("员工ID无效").ToString();
            }

            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var authbtn = await _dBAuthService.GetAuth(userId, "hr_post|edit");
            if (!authbtn)
            {
                return XHDResult.Error("无权限！").ToString();
            }

            var ok = await _service.UpdatePostEmpbyEidAsync(empid);
            return ok
                ? XHDResult.Success("清理成功").ToString()
                : XHDResult.Error("该员工没有岗位记录").ToString();
        }

        /// <summary>
        /// Sprint 5 Wave 1 #88：按员工 id 查其名下所有岗位。
        /// 对应 A 侧 Server.hr_post.getpostbyempid。
        /// 无结果时返回 null（对齐 A 侧 return null 语义）。
        /// </summary>
        [HttpGet("getpostbyempid")]
        public async Task<string> GetPostByEmpId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var posts = await _service.GetPostByEmpIdAsync(id);

            if (posts == null || posts.Count == 0)
            {
                return null;
            }

            var arr = new JArray();
            foreach (var p in posts)
            {
                arr.Add(JObject.FromObject(p));
            }

            return arr.ToString();
        }

        /// <summary>
        /// Sprint 5 Wave 1 #89：按岗位名模糊搜索。
        /// 对应 A 侧 Server.hr_post.serch（post_name LIKE N'%{text}%'）。
        /// 无结果返回 A 侧兼容格式 {"Rows":[],"Total":0}。
        /// </summary>
        [HttpGet("serch")]
        public async Task<string> Serch(string serchtxt)
        {
            const string emptyResult = "{\"Rows\":[],\"Total\":0}";

            if (string.IsNullOrWhiteSpace(serchtxt))
            {
                return emptyResult;
            }

            var posts = await _service.SerchAsync(serchtxt);

            if (posts == null || posts.Count == 0)
            {
                return emptyResult;
            }

            var arr = new JArray();
            foreach (var p in posts)
            {
                arr.Add(JObject.FromObject(p));
            }

            return "{\"Rows\":" + arr.ToString() + ",\"Total\":" + posts.Count + "}";
        }

        /// <summary>
        /// Sprint 5 Wave 1 #90：批量设置员工岗位分配。
        /// 对应 A 侧 Server.hr_post.postemp。
        /// 语义：遍历每条 PostData，写入 hr_post(emp_id, default_post)；
        /// 若 Default_post==1，同步更新 hr_employee(dep_id, post_id, position_id)。
        /// </summary>
        [HttpPost("postemp")]
        public async Task<string> Postemp(string empid, [FromBody] PostData[] postdata)
        {
            if (string.IsNullOrWhiteSpace(empid))
            {
                return XHDResult.Error("员工ID无效").ToString();
            }

            if (postdata == null || postdata.Length == 0)
            {
                // 对齐 A 侧 void postemp()：无数据时不报错，直接返回成功
                return XHDResult.Success().ToString();
            }

            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var authbtn = await _dBAuthService.GetAuth(userId, "hr_post|edit");
            if (!authbtn)
            {
                return XHDResult.Error("无权限！").ToString();
            }

            foreach (var item in postdata)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Post_id))
                {
                    continue;
                }

                // 更新 hr_post.emp_id 与 default_post
                await _service.UpdatePostEmpAsync(item.Post_id, empid, item.Default_post);

                // 若是默认岗位，同步员工岗位三元组
                if (item.Default_post == 1)
                {
                    await _employeeservice.UpdatePostAsync(
                        empid, item.Dep_id, item.Post_id, item.Position_id);
                }
            }

            return XHDResult.Success().ToString();
        }

        /// <summary>
        /// 取当前登录用户 ID（ClaimTypes.Sid）。
        /// </summary>
        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.Sid)?.Value;
        }
    }

    /// <summary>
    /// 岗位分配数据载体（对应 A 侧 Server.hr_post.PostData 类）。
    /// 字段名保持 A 侧命名（首字母大写），前端约定沿用。
    /// </summary>
    public class PostData
    {
        public string Post_id { get; set; } = string.Empty;
        public string Post_name { get; set; } = string.Empty;
        public string Emp_id { get; set; } = string.Empty;
        public string Emp_name { get; set; } = string.Empty;
        public int? Default_post { get; set; }
        public string Dep_id { get; set; } = string.Empty;
        public string Depname { get; set; } = string.Empty;
        public string Position_id { get; set; } = string.Empty;
        public string Position_name { get; set; } = string.Empty;
    }
}
