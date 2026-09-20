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
    public interface IMessage_newsService : IBaseService<Message_news>
    {
        /// <summary>
        /// Sprint 4 Wave 2 #15：公告未读提醒，委托 Repository 执行。
        /// 对应 A 侧 Server.Public_notice.noticeremind。
        /// </summary>
        /// <param name="limit">返回条数上限，默认 10</param>
        /// <returns>未读公告列表（已就地标记为已读）</returns>
        Task<List<Message_news>> NoticeRemindAsync(int limit = 10);

        /// <summary>
        /// Sprint 6 Wave 1 #98：新闻提醒（最新 N 条），委托 Repository 执行。
        /// 对应 A 侧 Server.Public_news.newsremind。
        /// 与 NoticeRemindAsync 语义相反：不过滤 isRead、不修改已读标记。
        /// </summary>
        /// <param name="limit">返回条数上限，默认 5</param>
        /// <returns>最新 N 条新闻（未修改 isRead）</returns>
        Task<List<Message_news>> NewsRemindAsync(int limit = 5);
    }
}
