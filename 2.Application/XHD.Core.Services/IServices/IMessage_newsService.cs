using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq.Expressions;

namespace XHD.Core.IServices
{
    public interface IMessage_newsService:IBaseService<Message_news>
    {
        /// <summary>
        /// Sprint 4 Wave 2 #15：公告未读提醒，委托 Repository 执行。
        /// 对应 A 侧 Server.Public_notice.noticeremind。
        /// </summary>
        /// <param name="limit">返回条数上限，默认 10</param>
        /// <returns>未读公告列表（已就地标记为已读）</returns>
        Task<List<Message_news>> NoticeRemindAsync(int limit = 10);
    }
}