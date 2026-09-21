
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.IServices
{
    /// <summary>
    /// 系统错误日志服务接口
    /// Sprint 8 #41 GetLogtype 错误日志类型字典
    /// </summary>
    public interface ISys_log_ErrService : IBaseService<Sys_log_Err>
    {
        /// <summary>
        /// 获取错误日志类型字典（按 typeid 升序去重）
        /// </summary>
        /// <returns>错误类型列表</returns>
        Task<List<(int typeid, string type)>> GetLogtypeAsync();
    }
}
