
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Claims;
using Newtonsoft.Json.Linq;
using XHD.Core.IServices;
using XHD.Core.Common;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// 系统错误日志控制器
    /// Sprint 8 #41 Sys_log_Err.GetLogtype（错误类型字典）
    /// 对应 A 侧 BLL.Sys_log_Err.GetLogtype（BLL/Sys_log_Err.cs:81）。
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
    }
}
