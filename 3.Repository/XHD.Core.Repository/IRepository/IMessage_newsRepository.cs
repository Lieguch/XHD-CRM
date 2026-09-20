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
    public interface IMessage_newsRepository: IXHDBaseRepository<Message_news>
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
    }
}
