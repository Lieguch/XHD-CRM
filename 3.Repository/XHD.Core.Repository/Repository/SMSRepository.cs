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
    /// SMS 仓储实现
    /// Sprint 7 新增：实现 GetByIdAsync / UpdateSendStateAsync。
    /// </summary>
    public class SMSRepository : BaseRepository<SMS>, ISMSRepository
    {
        public SMSRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 按 id 取短信实体。
        /// </summary>
        public async Task<SMS> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return await _fsql.Select<SMS>()
                .Where(a => a.id == id)
                .FirstAsync();
        }

        /// <summary>
        /// 更新发送状态：isSend=1, sendtime=@sendTime, check_id=@checkId。
        /// </summary>
        public async Task<int> UpdateSendStateAsync(string id, string checkId, DateTime sendTime)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return 0;
            }

            return await _fsql.Update<SMS>()
                .Set(a => a.isSend, 1)
                .Set(a => a.sendtime, sendTime)
                .Set(a => a.check_id, checkId ?? string.Empty)
                .Where(a => a.id == id)
                .ExecuteAffrowsAsync();
        }
    }
}
