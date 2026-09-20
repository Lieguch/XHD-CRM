using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;
using XHD.Core.Common;
using XHD.Core.IRepository;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.View.Configs;

namespace XHD.Core.View.Controllers
{
    public class MyNoteController : Controller
    {
        private readonly IMy_NoteService _service;
        public MyNoteController(IMy_NoteService service)
        {
            _service = service;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<string> Grid()
        {
            var emp_id = User.FindFirst(ClaimTypes.Sid).Value;

            Expression<Func<My_Note, bool>> exp = a => a.emp_id == emp_id;

            var result = await _service.GridAsync(exp);

            return result.ToString();
        }

        public async Task<string> Save(My_Note model)
        {
            model.id = UUIDNext.Uuid.NewSequential().ToString();
            model.Note_time = DateTime.Now;
            model.emp_id = User.FindFirst(ClaimTypes.Sid).Value;

            await _service.AddAsync(model);

            return XHDResult.Success(model.id).ToString();
        }

        public async Task<string> Update(My_Note model)
        {
            Expression<Func<My_Note, My_Note>> expnote = a => new My_Note { 
                content=model.content,
                color=model.color
            };

            Expression<Func<My_Note, bool>> expwhere = a => a.id == model.id;

            await _service.UpdateAsync(expnote, expwhere);

            return XHDResult.Success(model.id).ToString();
        }

        public async Task<string> UpdateXY(My_Note model)
        {
            Expression<Func<My_Note, My_Note>> expnote = a => new My_Note
            {
                top= model.top,
                left = model.left
            };

            Expression<Func<My_Note, bool>> expwhere = a => a.id == model.id;

            await _service.UpdateAsync(expnote, expwhere);

            return XHDResult.Success(model.id).ToString();
        }

        public async Task<string> Delete(string id)
        {
            var result = 0;            

            result = await _service.DeleteAsync(id);


            return XHDResult.Success().ToString();
        }

        /// <summary>
        /// Sprint 4 Wave 2 #14：便签未读提醒（首页弹窗）。
        /// 对应 A 侧 Server.Personal_notes.notesremind：按 Note_time desc 取前 N 条。
        /// B 侧扩展：仅返回当前登录员工 isRead=false 的便签，并在返回前批量标记为已读
        /// （isRead=true，read_time=now），下次调用将不再重复返回同一批便签。
        /// 勘误 C4：时间字段名是 Note_time（不是 create_time）。
        /// </summary>
        /// <param name="limit">返回条数上限，默认 10</param>
        /// <returns>标准 XHDResult 字符串，data 承载未读便签数组</returns>
        [HttpGet("Remind")]
        public async Task<string> Remind(int limit = 10)
        {
            var userId = User.FindFirst(ClaimTypes.Sid)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var list = await _service.RemindAsync(userId, limit);
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
