
using System;
using System.Linq.Expressions;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Repository
{
    /// <summary>
    /// 任务仓储实现
    /// Sprint 10.22：Task 实体的基础仓储。全部 CRUD 由 BaseRepository&lt;Task&gt; 提供，
    /// DI 反射自动注册（类名以 Repository 结尾、非抽象非接口）。
    /// </summary>
    public class TaskRepository : BaseRepository<Task>, ITaskRepository
    {
        public TaskRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }
    }
}
