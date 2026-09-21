using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.IRepository
{
    /// <summary>
    /// Sys_online 仓储接口
    /// Sprint 7 新增：支持 #101 getUserTree 与 #102 GetOnline 的在线追踪。
    /// </summary>
    public interface ISys_onlineRepository : IXHDBaseRepository<Sys_online>
    {
        /// <summary>
        /// Sprint 7 #102 GetOnline：更新当前用户 LastLogTime；若不存在则插入。
        /// </summary>
        /// <param name="userId">用户 id（员工 id）</param>
        /// <param name="userName">用户姓名</param>
        /// <returns>实际写入行数（1 更新 / 1 插入）</returns>
        Task<int> TouchAsync(string userId, string userName);

        /// <summary>
        /// Sprint 7 #102 GetOnline：删除超过指定分钟数的僵尸记录。
        /// </summary>
        /// <param name="staleMinutes">超时分钟数（A 侧 = 2）</param>
        /// <returns>删除行数</returns>
        Task<int> PurgeStaleAsync(int staleMinutes);

        /// <summary>
        /// Sprint 7 #101 getUserTree：取全部在线用户 id 集合（用于在线标记）。
        /// </summary>
        /// <param name="staleMinutes">仅返回未超时的记录</param>
        /// <returns>在线员工 id 集合</returns>
        Task<List<string>> GetOnlineUserIdsAsync(int staleMinutes);

        /// <summary>
        /// Sprint 7 #102 GetOnline：取全部未超时的 Sys_online 记录（按 LastLogTime 倒序）。
        /// </summary>
        /// <param name="staleMinutes">超时阈值</param>
        /// <returns>在线用户列表</returns>
        Task<List<Sys_online>> GetAllAsync(int staleMinutes);
    }
}
