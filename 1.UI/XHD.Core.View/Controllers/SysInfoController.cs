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
using System.Net.Http;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
using XHD.Core.View.Authorization;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class SysInfoController : Controller
    {
        private readonly ILogger<SysLogController> _logger;
        private readonly ISys_infoService _service;
        private readonly ISMSHelper _smsHelper;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        /// <summary>
        /// CheckUpdate 需要 IConfiguration（读 VersionCheck:Url）与 IHttpClientFactory（发 HTTP GET）。
        /// 两者在生产环境均已注册：Host.CreateDefaultBuilder 注册 IConfiguration；
        /// Startup.cs:73 的 AddHttpClient&lt;SMSHelper&gt; 同时注册 IHttpClientFactory。
        /// </summary>
        public SysInfoController(ILogger<SysLogController> logger, ISys_infoService service, ISMSHelper smsHelper, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _service = service;
            _logger = logger;
            _smsHelper = smsHelper;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
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

        [ButtonAuth("sysconfig", "save")]
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

        /// <summary>
        /// 在线版本检查（缺口 D）。
        /// 对应 A 版 Sys_version.aspx:46-88 checkup() + xhd_Service.getVersion.xhd：
        /// 1. 从 sys_info 取当前版本号（sys_version）与企业名（sys_name，A 版「用户体验计划」复选框控制是否传，B 版统一传、可空）
        /// 2. GET 版本检查服务器，query 带 Action=getversion / T_name
        /// 3. 用 <see cref="VersionHelper"/> 按 A 版四段加权口径比较本地与远程版本号
        /// A 版的 SOAP 端点（server.xhdcrm.com）已失效，B 版改为 HTTP GET + 可配置 Url（appsettings.json VersionCheck:Url），
        /// 远程约定返回 JSON，字段 version。Url 未配置 / 网络异常 / 返回格式异常时均明确报错，不假装成功。
        /// </summary>
        /// <returns>标准 XHDResult 字符串，data[0] 含 current / latest / hasUpdate</returns>
        [HttpPost("CheckUpdate")]
        public async Task<string> CheckUpdate()
        {
            // 1. 取当前版本号 + 企业名（Sys_infoService.GridAsync 返回 XHDData<Sys_info>）
            var infos = await _service.GridAsync(a => a.sys_key == "sys_version" || a.sys_key == "sys_name");

            var currentVersion = infos.data.FirstOrDefault(a => a.sys_key == "sys_version")?.sys_value;
            if (string.IsNullOrWhiteSpace(currentVersion))
            {
                return XHDResult.Error("未读取到当前版本号").ToString();
            }

            var companyName = infos.data.FirstOrDefault(a => a.sys_key == "sys_name")?.sys_value ?? string.Empty;

            // 2. 版本检查服务器 Url 必须显式配置，不硬编码任何地址
            var url = _configuration?["VersionCheck:Url"];
            if (string.IsNullOrWhiteSpace(url))
            {
                return XHDResult.Error("未配置版本检查服务器（VersionCheck:Url）").ToString();
            }

            // 3. HTTP GET 远程（短超时，避免前端长时间等待）
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var query = new StringBuilder("?Action=getversion")
                .Append("&T_name=").Append(Uri.EscapeDataString(companyName));

            try
            {
                using var response = await client.GetAsync(url + query);
                if (!response.IsSuccessStatusCode)
                {
                    return XHDResult.Error("无法连接版本检查服务器").ToString();
                }

                var body = await response.Content.ReadAsStringAsync();

                // 4. 解析远程 JSON，约定字段 version
                string latestVersion;
                try
                {
                    latestVersion = JObject.Parse(body)["version"]?.ToString() ?? string.Empty;
                }
                catch (Exception)
                {
                    return XHDResult.Error("版本检查服务器返回数据格式异常").ToString();
                }

                if (string.IsNullOrWhiteSpace(latestVersion))
                {
                    return XHDResult.Error("版本检查服务器返回数据格式异常").ToString();
                }

                // 5. 按 A 版口径比较，返回 current / latest / hasUpdate
                var info = new JObject
                {
                    ["current"] = currentVersion,
                    ["latest"] = latestVersion,
                    ["hasUpdate"] = VersionHelper.HasUpdate(currentVersion, latestVersion)
                };

                return XHDResult.Success(info).ToString();
            }
            catch (Exception)
            {
                // 网络异常 / 超时 / DNS 失败等，统一提示无法连接
                return XHDResult.Error("无法连接版本检查服务器").ToString();
            }
        }
    }
}
