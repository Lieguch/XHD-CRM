
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Security.Claims;

using XHD.Core.IServices;
using XHD.Core.Common;
using XHD.Core.Models;
using XHD.Core.View.Configs;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// 任务跟进控制器
    /// Sprint 8 #39 Task_follow.DeleteWhere（按 task_id 级联删除）
    /// Sprint 10.22 追加：Grid / Save（明细行显示跟进列表 + 新增跟进记录）
    /// 对应 A 侧 BLL.Task_follow（ext_rar2018/BLL/Task_follow.cs）。
    /// </summary>
    [Authorize]
    public class TaskFollowController : Controller
    {
        private readonly ILogger<TaskFollowController> _logger;
        private readonly ITask_followService _service;

        public TaskFollowController(
            ILogger<TaskFollowController> logger,
            ITask_followService service)
        {
            _logger = logger;
            _service = service;
        }

        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.Sid)?.Value;
        }

        /// <summary>
        /// 按任务 ID 级联删除该任务下的所有跟进记录。
        /// </summary>
        /// <param name="taskId">任务 ID</param>
        /// <returns>标准 XHDResult 字符串</returns>
        [HttpPost("DeleteWhere")]
        public async Task<string> DeleteWhere(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                return XHDResult.Error("参数错误！").ToString();
            }

            var rows = await _service.DeleteByTaskAsync(taskId);

            return XHDResult.Success("删除成功！").ToString();
        }

        /// <summary>
        /// 按 task_id 过滤查询跟进列表；无 task_id 时返回最近 50 条。
        /// 用于 Task/Index.cshtml 的明细展开行。
        /// </summary>
        public async Task<string> Grid(PageView<Task_follow> model)
        {
            Expression<Func<Task_follow, bool>> exp = f => true;

            if (!string.IsNullOrWhiteSpace(Request.Query["task_id"]))
            {
                var taskId = Request.Query["task_id"];
                exp = exp.And(f => f.task_id == taskId);
            }

            var sort = "follow_time desc";
            var result = await _service.GridAsync(exp, model.Page, model.Limit, sort);
            return result.ToString();
        }

        /// <summary>
        /// 新增/编辑跟进记录。
        /// id 为空即新建（生成 UUID + follow_id=当前用户 + follow_time=当前时间），
        /// id 有值即更新。
        /// </summary>
        public async Task<string> Save(Task_follow model)
        {
            if (model == null)
            {
                return XHDResult.Error("参数错误！").ToString();
            }
            if (string.IsNullOrWhiteSpace(model.task_id))
            {
                return XHDResult.Error("任务ID不能为空！").ToString();
            }

            if (string.IsNullOrWhiteSpace(model.id))
            {
                // 新建
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                if (string.IsNullOrWhiteSpace(model.follow_id))
                {
                    model.follow_id = GetUserId();
                }
                if (model.follow_time == null)
                {
                    model.follow_time = DateTime.Now;
                }
                if (model.follow_status == null)
                {
                    model.follow_status = 0;
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
                var existing = (await _service.GridAsync(f => f.id == model.id, 1, 1)).data.FirstOrDefault();
                if (existing == null)
                {
                    return XHDResult.Error("找不到数据！").ToString();
                }

                // 保留创建时字段，避免客户端伪造 task_id / follow_id
                model.task_id = existing.task_id;
                model.follow_id = existing.follow_id;

                var result = await _service.UpdateAsync(model);
                if (result == 0)
                {
                    return XHDResult.Error("操作失败，系统错误！").ToString();
                }

                return XHDResult.Success("保存成功！").ToString();
            }
        }
    }
}
