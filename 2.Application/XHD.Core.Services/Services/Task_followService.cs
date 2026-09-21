
using System;
using System.Threading.Tasks;
using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Services
{
    /// <summary>
    /// 任务跟进服务实现
    /// Sprint 8 #39 DeleteByTaskAsync 直接调独立 Repository 方法
    /// </summary>
    public class Task_followService : BaseService<Task_follow>, ITask_followService
    {
        private readonly ITask_followRepository _repo;

        public Task_followService(ITask_followRepository repository)
        {
            _irepository = repository;
            _repo = repository;
        }

        /// <summary>
        /// 按任务 ID 级联删除该任务下的所有跟进记录。
        /// </summary>
        /// <param name="taskId">任务 ID</param>
        /// <returns>受影响的行数</returns>
        public async Task<int> DeleteByTaskAsync(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                return 0;
            }

            return await _repo.DeleteWhereAsync(taskId);
        }
    }
}
