
using System;
using System.Threading.Tasks;
using System.Linq.Expressions;

using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.IRepository
{
    /// <summary>
    /// 任务仓储接口
    /// Sprint 10.22：Task 实体的基础仓储接口，继承 IXHDBaseRepository<Task> 提供全部 CRUD。
    /// </summary>
    public interface ITaskRepository : IXHDBaseRepository<Task>
    {
    }
}
