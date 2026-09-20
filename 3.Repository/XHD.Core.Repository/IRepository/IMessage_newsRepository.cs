using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;


namespace XHD.Core.IRepository
{
    public interface IMessage_newsRepository : IXHDBaseRepository<Message_news>
    {
        /// <summary>
        /// Sprint 4 Wave 2 #15：公告未读提醒。
        /// 对应 A 侧 Server.Public_notice.noticeremind（公告按 create_time desc 取前 N 条）。
        /// B 侧扩展：按 isRead=false 过滤（B 侧独有字段），并批量将返回列表的公告
        /// 标记为 isRead=true、read_time=now。公告为全局可见，不按 emp_id 过滤。
        /// 参数化执行（FreeSql 表达式），禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="limit">返回条数上限，默认 10</param>
        /// <returns>未读公告列表（已就地标记为已读）</returns>
        Task<List<Message_news>> NoticeRemindAsync(int limit = 10);

        /// <summary>
        /// Sprint 6 Wave 1 #98：新闻提醒（最新 N 条）。
        /// 对应 A 侧 Server.Public_news.newsremind：仅按 create_time desc 取前 N 条，
        /// **不**过滤 isRead、**不**修改已读标记。与 NoticeRemindAsync 语义相反，
        /// 二者不能互相复用；#15 走 isRead=false 过滤，#98 走"无论是否已读"取最新。
        /// 参数化执行（FreeSql 表达式），禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="limit">返回条数上限，默认 5（对齐 A 侧 GetList(5, ...)）</param>
        /// <returns>最新 N 条新闻（未修改 isRead）</returns>
        Task<List<Message_news>> NewsRemindAsync(int limit = 5);
    }
}
