
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;

using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.IRepository
{
    /// <summary>
    /// 系统错误日志仓储接口
    /// Sprint 8 #41 GetLogtype：独立 Entity（与 Sys_log 是两张表）
    /// </summary>
    public interface ISys_log_ErrRepository : IXHDBaseRepository<Sys_log_Err>
    {
        /// <summary>
        /// 获取错误日志类型字典（去重的 (Err_typeid, Err_type) 列表）
        /// </summary>
        /// <returns>类型列表</returns>
        Task<List<(int typeid, string type)>> GetLogtypeAsync();
    }
}
