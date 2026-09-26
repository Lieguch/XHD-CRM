
using System;
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

        public TaskController(
            ILogger<TaskController> logger,
            ITaskService service,
            ITask_followService followService,
            ICRM_CustomerService customerService,
            ISys_ParamService paramService)
        {
            _logger = logger;
            _service = service;
            _followService = followService;
            _customerService = customerService;
            _paramService = paramService;
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
            return result.ToString();
        }

        /// <summary>
        /// 新增/编辑保存。
        /// 关键修复点：id 为空即新建（生成 UUID + create_id + create_time），
        /// id 有值即更新；避免重复 JobsController.Save 的"新建返回无权限"逻辑反了 bug。
        /// </summary>
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
            var exec = string.IsNullOrWhiteSpace(execId) ? GetUserId() : execId;
            if (string.IsNullOrWhiteSpace(exec))
            {
                return XHDResult.Error("参数错误！").ToString();
            }

            Expression<Func<TaskInfo, bool>> exp = t => t.executive_id == exec && t.task_status_id == 0;
            var result = await _service.GridAsync(exp, 1, 200, "executive_time asc");
            return result.ToString();
        }
    }
}
