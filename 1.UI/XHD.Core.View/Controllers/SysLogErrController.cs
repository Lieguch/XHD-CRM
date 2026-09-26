
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Security.Claims;
using Newtonsoft.Json.Linq;
using XHD.Core.IServices;
using XHD.Core.Common;
using XHD.Core.Models;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// 系统错误日志控制器
    /// Sprint 8 #41 Sys_log_Err.GetLogtype（错误类型字典）
    /// Sprint 10.27a 补齐错误日志查看（Index + Grid 分页查询）
    /// 对应 A 侧 BLL.Sys_log_Err.GetLogtype / 前端 sys_log_err.aspx 的 LigGrid。
    /// </summary>
    [Authorize]
    public class SysLogErrController : Controller
    {
        private readonly ILogger<SysLogErrController> _logger;
        private readonly ISys_log_ErrService _service;

        public SysLogErrController(
            ILogger<SysLogErrController> logger,
            ISys_log_ErrService service)
        {
            _logger = logger;
            _service = service;
        }

        /// <summary>
        /// 错误日志列表页
        /// </summary>
        [HttpGet("Index")]
        public IActionResult Index()
        {
            return View("Index");
        }

        /// <summary>
        /// 获取错误类型字典（用于前端下拉选择）。
        /// </summary>
        /// <returns>标准 XHDResult 字符串（data 为 [{typeid,type}]）</returns>
        [HttpGet("GetLogtype")]
        public async Task<string> GetLogtype()
        {
            var list = await _service.GetLogtypeAsync();
            var arr = new JArray();

            foreach (var item in list)
            {
                arr.Add(new JObject
                {
                    { "typeid", item.typeid },
                    { "type", item.type }
                });
            }

            return XHDResult.Success(arr).ToString();
        }

        /// <summary>
        /// 错误日志分页查询（支持按错误类型、时间范围、关键字筛选）
        /// </summary>
        [HttpGet("Grid")]
        public async Task<string> Grid(PageView<Sys_log_Err> model)
        {
            Expression<Func<Sys_log_Err, bool>> exp = a => true;

            // 错误类型筛选：typeid 命中即匹配
            if (!string.IsNullOrWhiteSpace(Request.Query["logtype"]))
            {
                if (int.TryParse(Request.Query["logtype"], out var typeid))
                {
                    exp = exp.And(a => a.Err_typeid == typeid);
                }
            }

            // 时间范围筛选
            if (!string.IsNullOrWhiteSpace(Request.Query["date1"]))
            {
                if (DateTime.TryParse(Request.Query["date1"], out var d1))
                {
                    exp = exp.And(a => a.Err_time >= d1);
                }
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["date2"]))
            {
                if (DateTime.TryParse(Request.Query["date2"], out var d2))
                {
                    // 允许只填日期：补齐到当天 23:59:59
                    var d2end = d2.Date.AddDays(1).AddTicks(-1);
                    exp = exp.And(a => a.Err_time <= d2end);
                }
            }

            // 关键字筛选（错误消息 / 堆栈）
            if (!string.IsNullOrWhiteSpace(Request.Query["keyword"]))
            {
                var kw = Request.Query["keyword"];
                exp = exp.And(a => a.Err_message.Contains(kw) || a.Err_trace.Contains(kw));
            }

            var result = await _service.GridAsync(exp, model.Page, model.Limit, "Err_time Desc");

            return result.ToString();
        }
    }
}
