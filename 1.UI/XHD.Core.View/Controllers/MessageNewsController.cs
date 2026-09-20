using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using System.Security.Claims;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Common;
using XHD.Core.Models;

using System.Linq.Expressions;
using Newtonsoft.Json.Converters;
using System.Collections;
using XHD.Core.View.Configs;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class MessageNewsController : Controller
    {
        private readonly ILogger<MessageNewsController> _logger;
        private readonly IMessage_newsService _service;
        //private hr_employee LoginUser=new hr_employee();
        private readonly ISys_logService _LogService;
        private readonly IDBAuthService _dBAuthService;
        private readonly SysLogExt<Message_news> logext = new SysLogExt<Message_news>();  //日志

        public MessageNewsController(ILogger<MessageNewsController> logger, IMessage_newsService service, ISys_logService LogService, IDBAuthService dBAuthService)
        {
            _service = service;
            _logger = logger;
            _LogService = LogService;

            _dBAuthService = dBAuthService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Add()
        {
            return View();
        }

        public async Task<string> Grid(PageView<Message_news> model)
        {
            Expression<Func<Message_news, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(Request.Query["T_name"]))
            {
                exp = exp.And(a => a.news_title.Contains(Request.Query["T_name"]));
            }

            var result = await _service.GridAsync(exp, model.Page, model.Limit, "a.create_time desc");

            return result.ToString();
        }

        public async Task<string> Save(Message_news model)
        {
            var result = 0;

            if (string.IsNullOrWhiteSpace(model.id))
            {
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.create_time = DateTime.Now;
                model.create_id = User.FindFirst(ClaimTypes.Sid).Value;

                //权限
                var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "Message_News|add");

                if (authbtn)
                {
                    result = await _service.AddAsync(model);
                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }
            else
            {
                //权限
                var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "Message_News|edit");

                if (authbtn)
                {
                    //日志
                    Expression<Func<Message_news, bool>> exp = a => a.id == model.id;
                    var checknulldata = await _service.GridAsync(exp, 1, 1);

                    if (checknulldata.count == 0)
                    {
                        return XHDResult.Error("找不到数据！").ToString();
                    }

                    result = await _service.UpdateAsync(model);

                    //对比实体差别

                    var content = logext.LogContent(checknulldata.data[0], model);

                    if (content.Length > 0)
                    {
                        //添加修改日志
                        Sys_log logmodels = new Sys_log();

                        logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                        logmodels.EventType = "[新闻]修改";
                        logmodels.EventID = model.id;
                        logmodels.EventTitle = model.news_title;
                        logmodels.UserID = User.FindFirst(ClaimTypes.Sid).Value;
                        logmodels.UserName = User.FindFirst(ClaimTypes.Name).Value;
                        logmodels.IPStreet = HttpContext.Connection.RemoteIpAddress.ToString();
                        logmodels.EventDate = DateTime.Now;
                        logmodels.Log_Content = content;

                        
                        await _LogService.UpdateLog(logmodels);
                    }
                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }

            if (result == 0)
            {
                return XHDResult.Error("操作失败，系统错误！").ToString();
            }

            return XHDResult.Success().ToString();
        }

        public async Task<string> Delete(string id)
        {
            var result = 0;

            //权限
            var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "Message_News|del");

            if (authbtn)
            {
                //判断是否有数据
                Expression<Func<Message_news, bool>> exp = a => a.id == id;
                var checkdata = await _service.GridAsync(exp, 1, 1);

                if (checkdata.count == 0)
                {
                    return XHDResult.Error("找不到此数据！").ToString();
                }

                result = await _service.DeleteAsync(id);

                //先存储删除的实体记录，用日志形式
                logext.getEntityText(checkdata.data[0]);

                //记录日志
                Sys_log logmodels = new Sys_log();

                logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                logmodels.EventType = "[新闻]删除";
                logmodels.EventID = id;
                logmodels.EventTitle = checkdata.data[0].news_title;
                logmodels.UserID = User.FindFirst(ClaimTypes.Sid).Value;
                logmodels.UserName = User.FindFirst(ClaimTypes.Name).Value;
                logmodels.IPStreet = HttpContext.Connection.RemoteIpAddress.ToString();
                logmodels.EventDate = DateTime.Now;
                //logmodels.Log_Content = checkdata.data[0].follow_content;

                
                await _LogService.DeleteLog(logmodels);
            }
            else
            {
                return XHDResult.Error("无权限！").ToString();
            }

            //var result = await _service.Delete(id);

            if (result == 0)
            {
                return XHDResult.Error("删除失败！").ToString();
            }

            return XHDResult.Success().ToString();
        }

        /// <summary>
        /// Sprint 4 Wave 2 #15：公告未读提醒。
        /// 对应 A 侧 Server.Public_notice.noticeremind（公告全局可见，按 create_time desc 取前 N 条）。
        /// B 侧扩展：仅返回 isRead=false 的公告，并在返回前批量标记为已读
        /// （isRead=true，read_time=now）。勘误 C1：B 侧 Entity 名是 Message_news（不是 Public_notice）。
        /// </summary>
        /// <param name="limit">返回条数上限，默认 10</param>
        /// <returns>标准 XHDResult 字符串，data 承载未读公告数组</returns>
        [HttpGet("NoticeRemind")]
        public async Task<string> NoticeRemind(int limit = 10)
        {
            var list = await _service.NoticeRemindAsync(limit);
            var arr = new JArray();
            if (list != null)
            {
                foreach (var item in list)
                {
                    arr.Add(JObject.FromObject(item));
                }
            }
            return XHDResult.Success(arr).ToString();
        }

        /// <summary>
        /// Sprint 6 Wave 1 #98：新闻提醒（最新 N 条）。
        /// 对应 A 侧 Server.Public_news.newsremind：仅按 create_time desc 取前 N 条。
        /// 与 NoticeRemind 语义相反：不过滤 isRead、不修改已读标记。
        /// </summary>
        /// <param name="limit">返回条数上限，默认 5（对齐 A 侧 GetList(5, ...)）</param>
        /// <returns>标准 XHDResult 字符串，data 承载最新新闻数组</returns>
        [HttpGet("NewsRemind")]
        public async Task<string> NewsRemind(int limit = 5)
        {
            var list = await _service.NewsRemindAsync(limit);
            var arr = new JArray();
            if (list != null)
            {
                foreach (var item in list)
                {
                    arr.Add(JObject.FromObject(item));
                }
            }
            return XHDResult.Success(arr).ToString();
        }
    }
}
