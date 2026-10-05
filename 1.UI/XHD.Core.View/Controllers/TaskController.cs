
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.View.Configs;
using XHD.Core.View.Authorization;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// 任务管理控制器
    /// Sprint 10.22：从 A 版本 BLL.Task 移植，含 Grid/Save/Delete/UpdateStatus/MyTodo。
    /// 实体名为 TaskInfo（避开与 System.Threading.Tasks.Task 的命名冲突）。
    /// Delete 会级联删除该任务下的 Task_follow 记录。
    /// </summary>
    [Authorize]
    public class TaskController : Controller
    {
        private readonly ILogger<TaskController> _logger;
        private readonly ITaskService _service;
        private readonly ITask_followService _followService;
        private readonly ICRM_CustomerService _customerService;
        private readonly ISys_ParamService _paramService;
        private readonly IDBAuthService _dBAuthService;

        public TaskController(
            ILogger<TaskController> logger,
            ITaskService service,
            ITask_followService followService,
            ICRM_CustomerService customerService,
            ISys_ParamService paramService,
            IDBAuthService dBAuthService)
        {
            _logger = logger;
            _service = service;
            _followService = followService;
            _customerService = customerService;
            _paramService = paramService;
            _dBAuthService = dBAuthService;
        }

        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.Sid)?.Value;
        }

        /// <summary>
        /// 列表页入口
        /// </summary>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// 新增/编辑页入口
        /// </summary>
        public IActionResult Add()
        {
            return View();
        }

        /// <summary>
        /// 列表查询，支持按 task_title / task_status_id / priority_id / executive_id 过滤。
        /// </summary>
        public async Task<string> Grid(PageView<TaskInfo> model)
        {
            Expression<Func<TaskInfo, bool>> exp = t => true;

            if (!string.IsNullOrWhiteSpace(Request.Query["task_title"]))
            {
                var keyword = Request.Query["task_title"];
                exp = exp.And(t => t.task_title.Contains(keyword));
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["task_status_id"]))
            {
                int statusId = int.Parse(Request.Query["task_status_id"]);
                exp = exp.And(t => t.task_status_id == statusId);
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["priority_id"]))
            {
                int priority = int.Parse(Request.Query["priority_id"]);
                exp = exp.And(t => t.priority_id == priority);
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["executive_id"]))
            {
                var execId = Request.Query["executive_id"];
                exp = exp.And(t => t.executive_id == execId);
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["assign_id"]))
            {
                var assignId = Request.Query["assign_id"];
                exp = exp.And(t => t.assign_id == assignId);
            }

            var result = await _service.GridAsync(exp, model.Page, model.Limit, "create_time desc");

            // Sprint 10.42：批量回填 customer_name，使任务列表/编辑表单显示客户名而非 GUID（P1-17）
            var custIds = result.data
                .Where(t => !string.IsNullOrEmpty(t.customer_id))
                .Select(t => t.customer_id)
                .Distinct()
                .ToList();
            if (custIds.Count > 0)
            {
                // 客户名为可选展示增强，不得影响 Grid 主流程；客户服务无结果时保持空名
                var custResult = await _customerService.GridAsync(c => custIds.Contains(c.id));
                var nameMap = (custResult?.data ?? new List<CRM_Customer>())
                    .ToDictionary(c => c.id, c => c.cus_name);
                foreach (var t in result.data)
                {
                    if (!string.IsNullOrEmpty(t.customer_id) && nameMap.TryGetValue(t.customer_id, out var nm))
                    {
                        t.customer_name = nm;
                    }
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// 新增/编辑保存。
        /// 关键修复点：id 为空即新建（生成 UUID + create_id + create_time），
        /// id 有值即更新；避免重复 JobsController.Save 的"新建返回无权限"逻辑反了 bug。
        /// </summary>
        [ButtonAuth("task_manager", "save")]
        public async Task<string> Save(TaskInfo model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.task_title))
            {
                return XHDResult.Error("任务标题不能为空！").ToString();
            }

            if (string.IsNullOrWhiteSpace(model.id))
            {
                // 新建
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.create_id = GetUserId();
                model.create_time = DateTime.Now;
                if (model.task_status_id == null)
                {
                    model.task_status_id = 0;
                }
                if (model.priority_id == null)
                {
                    model.priority_id = 1;
                }
                if (model.is_check == null)
                {
                    model.is_check = 0;
                }

                var result = await _service.AddAsync(model);
                if (result == 0)
                {
                    return XHDResult.Error("操作失败，系统错误！").ToString();
                }

                return XHDResult.Success("新增成功！").ToString();
            }
            else
            {
                // 编辑
                var existing = (await _service.GridAsync(t => t.id == model.id, 1, 1)).data.FirstOrDefault();
                if (existing == null)
                {
                    return XHDResult.Error("找不到数据！").ToString();
                }

                // 保留创建信息，避免客户端伪造 create_id / create_time
                model.create_id = existing.create_id;
                model.create_time = existing.create_time;

                var result = await _service.UpdateAsync(model);
                if (result == 0)
                {
                    return XHDResult.Error("操作失败，系统错误！").ToString();
                }

                return XHDResult.Success("保存成功！").ToString();
            }
        }

        /// <summary>
        /// 删除任务，级联删除该任务下的所有 Task_follow 记录。
        /// </summary>
        [ButtonAuth("task_manager", "del")]
        public async Task<string> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return XHDResult.Error("参数错误！").ToString();
            }

            var existing = (await _service.GridAsync(t => t.id == id, 1, 1)).data.FirstOrDefault();
            if (existing == null)
            {
                return XHDResult.Error("找不到此数据！").ToString();
            }

            // 级联删除跟进记录
            await _followService.DeleteByTaskAsync(id);

            var result = await _service.DeleteAsync(id);
            if (result == 0)
            {
                return XHDResult.Error("删除失败！").ToString();
            }

            return XHDResult.Success("删除成功！").ToString();
        }

        /// <summary>
        /// 更新任务状态：0=进行中 / 1=已完成 / 2=已中止。
        /// 状态置为 1 时同步 is_check=1，模拟 A 侧勾选交互。
        /// </summary>
        [ButtonAuth("task_manager", "edit")]
        public async Task<string> UpdateStatus(string id, int status)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return XHDResult.Error("参数错误！").ToString();
            }
            if (status < 0 || status > 2)
            {
                return XHDResult.Error("状态值无效！").ToString();
            }

            var existing = (await _service.GridAsync(t => t.id == id, 1, 1)).data.FirstOrDefault();
            if (existing == null)
            {
                return XHDResult.Error("找不到数据！").ToString();
            }

            var updateModel = new TaskInfo
            {
                id = existing.id,
                task_title = existing.task_title,
                task_content = existing.task_content,
                task_type_id = existing.task_type_id,
                customer_id = existing.customer_id,
                assign_id = existing.assign_id,
                executive_id = existing.executive_id,
                executive_time = existing.executive_time,
                task_status_id = status,
                priority_id = existing.priority_id,
                remind_time = existing.remind_time,
                is_check = status == 1 ? 1 : 0,
                create_id = existing.create_id,
                create_time = existing.create_time
            };

            var result = await _service.UpdateAsync(updateModel);
            if (result == 0)
            {
                return XHDResult.Error("操作失败，系统错误！").ToString();
            }

            return XHDResult.Success("状态已更新！").ToString();
        }

        /// <summary>
        /// 我的待办：按 executive_id 查询未完成任务（task_status_id=0）。
        /// </summary>
        public async Task<string> MyTodo(string execId)
        {
            var currentUserId = GetUserId();
            var exec = string.IsNullOrWhiteSpace(execId) ? currentUserId : execId;
            if (string.IsNullOrWhiteSpace(exec))
            {
                return XHDResult.Error("参数错误！").ToString();
            }

            // Sprint 10.39 迁移例外（有意保留）：本检查是数据权限(authtype)与按钮权限的复合条件，
            // 且仅在"查看他人待办"时触发 —— 条件依赖请求数据，静态授权属性无法表达。
            // 若日后要收口，需引入条件授权策略(Policy)而非 [ButtonAuth]。
            // 查看他人待办需要 view_others 权限或 admin
            if (exec != currentUserId)
            {
                var roledata = await _dBAuthService.GetDataAuth(currentUserId);
                if (roledata.authtype != 4 && !await _dBAuthService.GetAuth(currentUserId, "task_manager|view_others"))
                {
                    return XHDResult.Error("无权限查看他人待办").ToString();
                }
            }

            Expression<Func<TaskInfo, bool>> exp = t => t.executive_id == exec && t.task_status_id == 0;
            var result = await _service.GridAsync(exp, 1, 200, "executive_time asc");
            return result.ToString();
        }

        /// <summary>
        /// 门户任务提醒：对应 A 侧 Server.MSG_Task.TaskRemind
        /// （A 侧 task.GetList(7, "executive_id='{emp_id}'", "executive_time desc")）。
        /// 与 MyTodo 同口径：仅当前登录员工 + 未完成（task_status_id=0），
        /// 按执行时间升序（最紧急/最逾期在前），默认 7 条。纯查询，不修改任何状态。
        /// </summary>
        /// <param name="limit">条数上限，默认 7（对齐 A 侧 GetList(7, ...)），有效范围夹逼到 1~50</param>
        /// <returns>标准 XHDResult 字符串，data 承载未完成任务数组</returns>
        [HttpGet("Remind")]
        public async Task<string> Remind(int limit = 7)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            if (limit < 1) limit = 1;
            if (limit > 50) limit = 50;

            Expression<Func<TaskInfo, bool>> exp = t => t.executive_id == userId && t.task_status_id == 0;
            var result = await _service.GridAsync(exp, 1, limit, "executive_time asc");

            var arr = new JArray();
            if (result?.data != null)
            {
                foreach (var item in result.data)
                {
                    arr.Add(JObject.FromObject(item));
                }
            }

            return XHDResult.Success(arr).ToString();
        }
    }
}
