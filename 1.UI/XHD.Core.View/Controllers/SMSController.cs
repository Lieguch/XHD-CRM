using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using System.Security.Claims;
using Newtonsoft.Json.Linq;
using XHD.Core.IServices;
using XHD.Core.Common;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// SMS 短信控制器
    /// Sprint 7 新增：#124 SMS.send / #125 SMS_Helper.getBalance。
    /// Sprint 8 新增：#157 SMS_Helper.getReport → QueryStatus。
    /// </summary>
    [Authorize]
    public class SMSController : Controller
    {
        private readonly ILogger<SMSController> _logger;
        private readonly ISMSService _service;

        public SMSController(
            ILogger<SMSController> logger,
            ISMSService service)
        {
            _logger = logger;
            _service = service;
        }

        /// <summary>
        /// Sprint 7 #124 SMS.send：审核并发送短信（更新 isSend/sendtime/check_id）。
        /// 对应 A 侧 Server.SMS.send（Server/SMS.cs:187）。
        /// </summary>
        /// <param name="id">短信 id（GUID）</param>
        /// <returns>标准 XHDResult 字符串</returns>
        [HttpPost("send")]
        public async Task<string> Send(string id)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var result = await _service.SendAsync(id ?? string.Empty, userId);
            return result;
        }

        /// <summary>
        /// Sprint 7 #125 SMS_Helper.getBalance：查询短信余额。
        /// 未配置 SMS 或异常时返回 0（不抛异常）。
        /// </summary>
        [HttpGet("getBalance")]
        public async Task<string> GetBalance()
        {
            var balance = await _service.GetBalanceAsync();
            var obj = new JObject { { "balance", balance } };
            return XHDResult.Success(obj).ToString();
        }

        /// <summary>
        /// Sprint 8 #157 SMS_Helper.getReport：查询短信状态报告（回执）。
        /// 未配置 SMS 或异常时返回空 JArray。
        /// 对应 A 侧 SMS/SMSHelper.cs:167 getReport。
        /// </summary>
        [HttpGet("queryStatus")]
        public async Task<string> QueryStatus()
        {
            var arr = await _service.QueryStatusAsync();
            return XHDResult.Success(arr).ToString();
        }

        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.Sid)?.Value;
        }
    }
}
