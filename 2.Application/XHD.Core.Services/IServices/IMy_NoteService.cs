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
    public interface IMy_NoteService:IBaseService<My_Note>
    {
        /// <summary>
        /// Sprint 4 Wave 2 #14：便签未读提醒，委托 Repository 执行。
        /// 对应 A 侧 Server.Personal_notes.notesremind。
        /// </summary>
        /// <param name="empId">员工 ID</param>
        /// <param name="limit">返回条数上限，默认 10</param>
        /// <returns>未读便签列表（已就地标记为已读）</returns>
        Task<List<My_Note>> RemindAsync(string empId, int limit = 10);
    }
}