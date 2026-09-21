
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Repository
{
    /// <summary>
    /// 任务跟进仓储实现
    /// Sprint 8 #39 DeleteWhere 独立方法（P12 IgnoreColumns 陷阱经验：
    /// 不复用 UpdateAsync 路径，直接走 Delete.Where）
    /// </summary>
    public class Task_followRepository : BaseRepository<Task_follow>, ITask_followRepository
    {
        public Task_followRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 按 task_id 级联删除该任务下所有跟进记录。
        /// </summary>
        /// <param name="taskId">任务 ID</param>
        /// <returns>受影响行数</returns>
        public async Task<int> DeleteWhereAsync(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                return 0;
            }

            var result = await _fsql.Delete<Task_follow>()
                .Where(a => a.task_id == taskId)
                .ExecuteAffrowsAsync();

            return result;
        }
    }
}
