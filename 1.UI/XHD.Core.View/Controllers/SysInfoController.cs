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
using System.Linq.Expressions;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Common;
using XHD.Core.Common.DEncrypt;
using XHD.Core.Common.SMS;
using XHD.Core.Models;

namespace XHD.Core.View.Controllers
{
    public class SysInfoController : Controller
    {
        private readonly ILogger<SysLogController> _logger;
        private readonly ISys_infoService _service;
        private readonly ISMSHelper _smsHelper;

        public SysInfoController(ILogger<SysLogController> logger, ISys_infoService service, ISMSHelper smsHelper)
        {
            _service = service;
            _logger = logger;
            _smsHelper = smsHelper;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<string> Grid()
        {
            Expression<Func<Sys_info, bool>> exp = a => 1 == 1;

            var result = await _service.GridAsync(exp);

            return result.ToString();
        }

        public async Task<string> Save(string key,string value)
        {
            Sys_info model=new Sys_info();

            model.sys_key = key;
            model.sys_value = value;

            var result = await _service.UpdateAsync(model);

            return XHDResult.Success().ToString();

        }

        /// <summary>
        /// Sprint 7 #115 Sys_info.regSMS：短信服务商注册。
        /// 对应 A 侧 Server.Sys_info.regSMS（Server/Sys_info.cs:80-106）：
        /// 1. 写 sys_info sms_no = T_SerialNo
        /// 2. 写 sys_info sms_key = DESEncrypt.Encrypt(T_key)
        /// 3. 调 SMSHelper.RegistEx；成功 → 写 sms_done="1" + 返回 Success；失败 → 返回 Error
        /// </summary>
        /// <param name="serialNo">短信服务商序列号（T_SerialNo）</param>
        /// <param name="key">服务商密钥（T_key）</param>
        /// <returns>标准 XHDResult 字符串</returns>
        [HttpPost("regSMS")]
        public async Task<string> RegSMS(string serialNo, string key)
        {
            if (string.IsNullOrWhiteSpace(serialNo))
            {
                return XHDResult.Error("序列号不能为空").ToString();
            }
            if (string.IsNullOrWhiteSpace(key))
            {
                return XHDResult.Error("密钥不能为空").ToString();
            }

            // 1. 写 sms_no
            var modelNo = new Sys_info { sys_key = "sms_no", sys_value = serialNo };
            await _service.UpdateAsync(modelNo);

            // 2. 写 sms_key（DES 加密）
            var encryptedKey = DESEncrypt.Encrypt(key);
            var modelKey = new Sys_info { sys_key = "sms_key", sys_value = encryptedKey };
            await _service.UpdateAsync(modelKey);

            // 3. 调 SMSHelper.RegistEx
            int result;
            try
            {
                result = _smsHelper.RegistEx(serialNo, key, key);
            }
            catch (Exception ex)
            {
                return XHDResult.Error("短信服务商注册异常：" + ex.Message).ToString();
            }

            if (result == 0)
            {
                var modelDone = new Sys_info { sys_key = "sms_done", sys_value = "1" };
                await _service.UpdateAsync(modelDone);
                return XHDResult.Success().ToString();
            }

            return XHDResult.Error(ISMSHelper.SmsResult(result)).ToString();
        }
    }
}
