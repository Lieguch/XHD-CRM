using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;
using System.Threading.Tasks;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

namespace XHD.Core.Services
{
    internal class My_NoteService : BaseService<My_Note>, IMy_NoteService
    {
        // BaseService 的 _irepository 字段声明为 IXHDBaseRepository&lt;My_Note&gt;，
        // 拿不到 RemindAsync 这个扩展方法，因此额外持有一个具体接口引用。
        private readonly IMy_NoteRepository _irepositoryBase;

        public My_NoteService(IMy_NoteRepository repository)
        {
            _irepository = repository;
            _irepositoryBase = repository;
        }

        /// <summary>
        /// Sprint 4 Wave 2 #14：便签未读提醒，service 层薄封装，委托 Repository 执行。
        /// </summary>
        /// <param name="empId">员工 ID</param>
        /// <param name="limit">返回条数上限，默认 10</param>
        /// <returns>未读便签列表（已就地标记为已读）</returns>
        public async Task<List<My_Note>> RemindAsync(string empId, int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(empId))
            {
                return new List<My_Note>();
            }

            return await _irepositoryBase.RemindAsync(empId, limit);
        }
    }
}