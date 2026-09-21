
using System;
using System.Threading.Tasks;
using System.Linq.Expressions;

using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.IRepository
{
    /// <summary>
    /// 任务跟进仓储接口
    /// Sprint 8 #39 DeleteWhere 独立 Repository 方法（P12 IgnoreColumns 陷阱经验）
    /// </summary>
    public interface ITask_followRepository : IXHDBaseRepository<Task_follow>
    {
        /// <summary>
        /// 按 task_id 级联删除该任务下的所有跟进记录。
        /// </summary>
        /// <param name="taskId">任务 ID</param>
        /// <returns>受影响的行数</returns>
        Task<int> DeleteWhereAsync(string taskId);
    }
}
