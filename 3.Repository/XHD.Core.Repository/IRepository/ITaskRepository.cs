
using System;
using System.Linq.Expressions;

using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.IRepository
{
    /// <summary>
    /// 任务仓储接口
    /// Sprint 10.22：TaskInfo 实体的基础仓储接口，继承 IXHDBaseRepository<TaskInfo> 提供全部 CRUD。
    /// </summary>
    public interface ITaskRepository : IXHDBaseRepository<TaskInfo>
    {
    }
}
