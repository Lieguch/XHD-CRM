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
    public interface IMy_NoteRepository: IXHDBaseRepository<My_Note>
    {
        /// <summary>
        /// Sprint 4 Wave 2 #14：便签未读提醒。
        /// 对应 A 侧 Server.Personal_notes.notesremind。
        /// 按 Note_time 降序返回指定员工的未读便签（isRead=false），
        /// 并批量将返回列表的便签标记为已读（isRead=true，read_time=now）。
        /// 参数化执行（FreeSql 表达式），禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="empId">员工 ID</param>
        /// <param name="limit">返回条数上限，默认 10</param>
        /// <returns>未读便签列表（已就地标记为已读）</returns>
        Task<List<My_Note>> RemindAsync(string empId, int limit = 10);
    }
}
