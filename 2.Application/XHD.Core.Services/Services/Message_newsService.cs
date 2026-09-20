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
    internal class Message_newsService : BaseService<Message_news>, IMessage_newsService
    {
        // BaseService 的 _irepository 字段声明为 IXHDBaseRepository<Message_news>，
        // 拿不到 NoticeRemindAsync / NewsRemindAsync 这些扩展方法，因此额外持有一个具体接口引用。
        private readonly IMessage_newsRepository _irepositoryBase;

        public Message_newsService(IMessage_newsRepository repository)
        {
            _irepository = repository;
            _irepositoryBase = repository;
        }

        /// <summary>
        /// Sprint 4 Wave 2 #15：公告未读提醒，Service 层薄封装，委托 Repository 执行。
        /// </summary>
        public async Task<List<Message_news>> NoticeRemindAsync(int limit = 10)
        {
            return await _irepositoryBase.NoticeRemindAsync(limit);
        }

        /// <summary>
        /// Sprint 6 Wave 1 #98：新闻提醒（最新 N 条），Service 层薄封装。
        /// </summary>
        public async Task<List<Message_news>> NewsRemindAsync(int limit = 5)
        {
            return await _irepositoryBase.NewsRemindAsync(limit);
        }
    }
}
