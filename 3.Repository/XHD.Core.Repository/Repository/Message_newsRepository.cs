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
    public class Message_newsRepository : BaseRepository<Message_news>, IMessage_newsRepository
    {
        public Message_newsRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 更新
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async new Task<int> UpdateAsync(Message_news model)
        {
            var result = await _fsql.Update<Message_news>()
                .SetSource(model)
                .IgnoreColumns(a => new { a.create_id,a.create_time })
                .ExecuteAffrowsAsync();

            return result;
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="Page"></param>
        /// <param name="Limit"></param>
        /// <param name="expOrder"></param>
        /// <returns></returns>
        public async new Task<XHDData<Message_news>> GridAsync(Expression<Func<Message_news, bool>> expWhere, int Page, int Limit)
        {
            var data = await _fsql.Select<Message_news>()
                .LeftJoin(a => a.NewsType.id == a.news_type_id)
                .Where(expWhere)
                //.OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync();

            //构建返回数据
            XHDData<Message_news> result = new XHDData<Message_news>()
            {
                data = data,
                count = total
            };

            return result;
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="Page"></param>
        /// <param name="Limit"></param>
        /// <param name="expOrder"></param>
        /// <returns></returns>
        public async new Task<XHDData<Message_news>> GridAsync(Expression<Func<Message_news, bool>> expWhere, int Page, int Limit, string orderby)
        {
            if (string.IsNullOrWhiteSpace(orderby))
            {
                return await GridAsync(expWhere, Page, Limit);
            }

            var data = await _fsql.Select<Message_news>()
                .LeftJoin(a => a.NewsType.id == a.news_type_id)
                .Where(expWhere)
                .OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync();

            //构建返回数据
            XHDData<Message_news> result = new XHDData<Message_news>()
            {
                data = data,
                count = total
            };

            return result;
        }

        /// <summary>
        /// 普通条件查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <returns></returns>
        public async new Task<List<Message_news>> GridAsync(Expression<Func<Message_news, bool>> expWhere)
        {
            var data = await _fsql.Select<Message_news>()
                .LeftJoin(a => a.NewsType.id == a.news_type_id)
                .Where(expWhere)
                .ToListAsync();

            return data;
        }

        /// <summary>
        /// 普通条件查询带排序
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="OrderBy"></param>
        /// <returns></returns>
        public async new Task<List<Message_news>> GridAsync(Expression<Func<Message_news, bool>> expWhere, string OrderBy)
        {
            if (string.IsNullOrWhiteSpace(OrderBy))
            {
                return await GridAsync(expWhere);
            }

            var data = await _fsql.Select<Message_news>()
                .LeftJoin(a => a.NewsType.id == a.news_type_id)
                .Where(expWhere)
                .OrderBy(OrderBy)
                .ToListAsync();

            return data;
        }

        /// <summary>
        /// Sprint 4 Wave 2 #15：公告未读提醒。
        /// 对应 A 侧 Server.Public_notice.noticeremind（按 create_time desc 取前 N 条）。
        /// B 侧扩展：按 isRead=false 过滤（B 侧独有字段），并批量将返回列表的公告
        /// 标记为 isRead=true、read_time=now，实现"查询即读"的提醒语义。
        /// 公告为全局可见，不按 emp_id 过滤。
        /// 参数化执行，禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="limit">返回条数上限，默认 10</param>
        /// <returns>未读公告列表；若返回前已标记为已读</returns>
        public async Task<List<Message_news>> NoticeRemindAsync(int limit = 10)
        {
            if (limit < 1) limit = 1;
            if (limit > 50) limit = 50;

            // 1. 查询未读公告（按 create_time 降序）
            var list = await _fsql.Select<Message_news>()
                .Where(a => !a.isRead)
                .OrderByDescending(a => a.create_time)
                .Limit(limit)
                .ToListAsync();

            if (list == null || list.Count == 0)
            {
                return list ?? new List<Message_news>();
            }

            // 2. 就地标记为已读
            var now = DateTime.Now;
            foreach (var n in list)
            {
                n.isRead = true;
                n.read_time = now;
            }

            // 3. 批量持久化：仅更新本次返回的 ID 集
            var ids = list.Select(n => n.id).ToList();
            await _fsql.Update<Message_news>()
                .Set(a => a.isRead, true)
                .Set(a => a.read_time, now)
                .Where(a => ids.Contains(a.id))
                .IgnoreColumns(a => new { a.create_id, a.create_time })
                .ExecuteAffrowsAsync();

            return list;
        }
    }
}
