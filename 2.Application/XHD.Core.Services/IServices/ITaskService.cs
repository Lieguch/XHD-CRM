
using System;
using System.Threading.Tasks;
using System.Linq.Expressions;

using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.IServices
{
    /// <summary>
    /// 任务服务接口
    /// Sprint 10.22：Task 实体的基础服务接口，继承 IBaseService&lt;Task&gt; 提供全部 CRUD。
    /// </summary>
    public interface ITaskService : IBaseService<Task>
    {
    }
}
