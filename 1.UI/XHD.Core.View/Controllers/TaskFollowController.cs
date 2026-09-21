
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using System.Security.Claims;
using XHD.Core.IServices;
using XHD.Core.Common;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// 任务跟进控制器
    /// Sprint 8 #39 Task_follow.DeleteWhere（按 task_id 级联删除）
    /// 对应 A 侧 BLL.Task_follow.DeleteWhere（BLL/Task_follow.cs:104）。
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

            if (rows > 0)
            {
                return XHDResult.Success("删除成功！").ToString();
            }

            return XHDResult.Success("删除成功！").ToString();
        }
    }
}
