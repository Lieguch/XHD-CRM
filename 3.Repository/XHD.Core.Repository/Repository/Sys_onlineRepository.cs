using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;
using XHD.Core.IRepository;

namespace XHD.Core.Repository
{
    /// <summary>
    /// Sys_online 仓储实现
    /// Sprint 7 新增：实现 TouchAsync / PurgeStaleAsync / GetOnlineUserIdsAsync / GetAllAsync。
    /// </summary>
    public class Sys_onlineRepository : BaseRepository<Sys_online>, ISys_onlineRepository
    {
        public Sys_onlineRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// Touch：先 UPDATE，rows=0 时 INSERT。
        /// </summary>
        public async Task<int> TouchAsync(string userId, string userName)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return 0;
            }

            var now = DateTime.Now;
            var updated = await _fsql.Update<Sys_online>()
                .Set(a => a.LastLogTime, now)
                .Set(a => a.UserName, userName ?? string.Empty)
                .Where(a => a.UserID == userId)
                .ExecuteAffrowsAsync();

            if (updated > 0)
            {
                return 1;
            }

            var entity = new Sys_online
            {
                UserID = userId,
                UserName = userName ?? string.Empty,
                LastLogTime = now
            };
            await _fsql.Insert(entity).ExecuteAffrowsAsync();
            return 1;
        }

        /// <summary>
        /// PurgeStale：删除 LastLogTime &lt; now - minutes 分钟 的记录。
        /// </summary>
        public async Task<int> PurgeStaleAsync(int staleMinutes)
        {
            if (staleMinutes < 1) staleMinutes = 1;
            var threshold = DateTime.Now.AddMinutes(-staleMinutes);
            return await _fsql.Delete<Sys_online>()
                .Where(a => a.LastLogTime < threshold)
                .ExecuteAffrowsAsync();
        }

        /// <summary>
        /// GetOnlineUserIds：返回未超时用户 id 集合。
        /// </summary>
        public async Task<List<string>> GetOnlineUserIdsAsync(int staleMinutes)
        {
            if (staleMinutes < 1) staleMinutes = 1;
            var threshold = DateTime.Now.AddMinutes(-staleMinutes);
            return await _fsql.Select<Sys_online>()
                .Where(a => a.LastLogTime >= threshold)
                .ToListAsync(a => a.UserID);
        }

        /// <summary>
        /// GetAll：返回未超时的全部 Sys_online 记录（按 LastLogTime 倒序）。
        /// </summary>
        public async Task<List<Sys_online>> GetAllAsync(int staleMinutes)
        {
            if (staleMinutes < 1) staleMinutes = 1;
            var threshold = DateTime.Now.AddMinutes(-staleMinutes);
            return await _fsql.Select<Sys_online>()
                .Where(a => a.LastLogTime >= threshold)
                .OrderByDescending(a => a.LastLogTime)
                .ToListAsync();
        }
    }
}
