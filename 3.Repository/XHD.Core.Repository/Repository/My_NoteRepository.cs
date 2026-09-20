using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

namespace XHD.Core.Repository
{
    public class My_NoteRepository : BaseRepository<My_Note>, IMy_NoteRepository
    {
        public My_NoteRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 更新
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async new Task<int> UpdateAsync(My_Note model)
        {
            var result = await _fsql.Update<My_Note>()
                .SetSource(model)
                .IgnoreColumns(a => new { a.emp_id,a.Note_time })
                .ExecuteAffrowsAsync();

            return result;
        }

        /// <summary>
        /// Sprint 4 Wave 2 #14：便签未读提醒。
        /// 对应 A 侧 Server.Personal_notes.notesremind：按 note_time desc 取前 N 条。
        /// B 侧扩展：同时按 isRead=false 过滤（B 侧独有字段），并批量将返回列表的便签
        /// 标记为 isRead=true、read_time=now，实现"查询即读"的提醒语义。
        /// 参数化执行，禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="empId">员工 ID</param>
        /// <param name="limit">返回条数上限，默认 10</param>
        /// <returns>未读便签列表；若返回前已标记为已读</returns>
        public async Task<List<My_Note>> RemindAsync(string empId, int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(empId))
            {
                return new List<My_Note>();
            }

            if (limit < 1) limit = 1;
            if (limit > 50) limit = 50;

            // 1. 查询未读便签（按 Note_time 降序）
            var list = await _fsql.Select<My_Note>()
                .Where(a => a.emp_id == empId && !a.isRead)
                .OrderByDescending(a => a.Note_time)
                .Limit(limit)
                .ToListAsync();

            if (list == null || list.Count == 0)
            {
                return list ?? new List<My_Note>();
            }

            // 2. 就地标记为已读
            var now = DateTime.Now;
            foreach (var note in list)
            {
                note.isRead = true;
                note.read_time = now;
            }

            // 3. 批量持久化：仅更新本次返回的 ID 集，IgnoreColumns 排除业务只读字段
            var ids = list.Select(n => n.id).ToList();
            await _fsql.Update<My_Note>()
                .Set(a => a.isRead, true)
                .Set(a => a.read_time, now)
                .Where(a => ids.Contains(a.id))
                .IgnoreColumns(a => new { a.emp_id, a.Note_time })
                .ExecuteAffrowsAsync();

            return list;
        }
    }
}
