
using System;
using System.Linq.Expressions;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Services
{
    /// <summary>
    /// 任务服务实现
    /// Sprint 10.22：TaskInfo 实体的基础服务。全部 CRUD 由 BaseService&lt;TaskInfo&gt; 提供，
    /// DI 反射自动注册（类名以 Service 结尾、非抽象非接口）。
    /// </summary>
    public class TaskService : BaseService<TaskInfo>, ITaskService
    {
        public TaskService(ITaskRepository repository)
        {
            _irepository = repository;
        }
    }
}
