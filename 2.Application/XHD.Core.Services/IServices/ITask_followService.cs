
using System;
using System.Threading.Tasks;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.IServices
{
    /// <summary>
    /// 任务跟进服务接口
    /// Sprint 8 #39 DeleteWhere 级联删除
    /// </summary>
    public interface ITask_followService : IBaseService<Task_follow>
    {
        /// <summary>
        /// 按任务 ID 级联删除所有跟进记录。
        /// </summary>
        /// <param name="taskId">任务 ID</param>
        /// <returns>受影响的行数</returns>
        Task<int> DeleteByTaskAsync(string taskId);
    }
}
