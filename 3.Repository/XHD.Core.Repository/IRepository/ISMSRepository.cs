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
    /// SMS 仓储接口
    /// Sprint 7 新增：#124 SMS.send 使用的更新逻辑。
    /// </summary>
    public interface ISMSRepository : IXHDBaseRepository<SMS>
    {
        /// <summary>
        /// Sprint 7 #124 SMS.send：按 id 取短信实体（不存在返回 null）。
        /// </summary>
        Task<SMS> GetByIdAsync(string id);

        /// <summary>
        /// Sprint 7 #124 SMS.send：更新短信发送状态（isSend / sendtime / check_id）。
        /// 独立 Repository 方法，绕开 BaseService.UpdateAsync 的 IgnoreColumns 陷阱（P12 教训）。
        /// </summary>
        /// <param name="id">短信 id</param>
        /// <param name="checkId">审核人 id</param>
        /// <param name="sendTime">发送时间</param>
        /// <returns>实际更新行数</returns>
        Task<int> UpdateSendStateAsync(string id, string checkId, DateTime sendTime);
    }
}
