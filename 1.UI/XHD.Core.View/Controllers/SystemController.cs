using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XHD.Core.Common;
using XHD.Core.Common.Cache;
using XHD.Core.Common.CDKEY;
using XHD.Core.Common.Mail;
using XHD.Core.Common.RSA;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// 系统工具控制器（Sprint 9 新增）。
    /// 汇总 5 个 Common 工具类端点：
    /// #144 ECBC_CDKEY.Generate  → POST /System/GenerateCDKey
    /// #145 ECBC_CDKEY.Verify    → POST /System/VerifyCDKey
    /// #147 MailSender.SendMail  → POST /System/SendMail
    /// #148 RSACryption.*        → GET  /System/Rsa/GenerateKeyPair
    ///                              POST /System/Rsa/Encrypt
    ///                              POST /System/Rsa/Decrypt
    /// #158 DataCache.GetDataCache → GET /System/Cache/Get?key=xxx
    /// </summary>
    [Authorize]
    public class SystemController : Controller
    {
        private readonly ILogger<SystemController> _logger;
        private readonly ICDKEYHelper _cdkey;
        private readonly IMailHelper _mail;
        private readonly IRSACryptionHelper _rsa;
        private readonly IDataCacheHelper _cache;

        public SystemController(
            ILogger<SystemController> logger,
            ICDKEYHelper cdkey,
            IMailHelper mail,
            IRSACryptionHelper rsa,
            IDataCacheHelper cache)
        {
            _logger = logger;
            _cdkey = cdkey;
            _mail = mail;
            _rsa = rsa;
            _cache = cache;
        }

        /// <summary>
        /// Sprint 9 #144 ECBC_CDKEY.Generate：基于机器码生成 CDKEY。
        /// </summary>
        /// <param name="req">请求体 <c>{ "machineCode": "..." }</c></param>
        /// <returns>XHDResult；Data 包含 cdkey 字段</returns>
        [HttpPost("GenerateCDKey")]
        public async Task<string> GenerateCDKey([FromBody] GenerateCDKeyRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.MachineCode))
            {
                return XHDResult.Error("machineCode 不能为空").ToString();
            }
            try
            {
                string cdkey = await _cdkey.GenerateAsync(req.MachineCode);
                var obj = new JObject { { "machineCode", req.MachineCode }, { "cdkey", cdkey } };
                return XHDResult.Success(obj).ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GenerateCDKey failed");
                return XHDResult.Error("生成 CDKEY 失败：" + ex.Message).ToString();
            }
        }

        /// <summary>
        /// Sprint 9 #145 ECBC_CDKEY.Verify：校验 CDKEY。
        /// </summary>
        /// <param name="req">请求体 <c>{ "machineCode": "...", "cdkey": "..." }</c></param>
        /// <returns>XHDResult；Data 包含 valid 布尔</returns>
        [HttpPost("VerifyCDKey")]
        public async Task<string> VerifyCDKey([FromBody] VerifyCDKeyRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.MachineCode) || string.IsNullOrWhiteSpace(req.Cdkey))
            {
                return XHDResult.Error("machineCode 和 cdkey 均不能为空").ToString();
            }
            try
            {
                bool valid = await _cdkey.VerifyAsync(req.MachineCode, req.Cdkey);
                var obj = new JObject
                {
                    { "machineCode", req.MachineCode },
                    { "cdkey", req.Cdkey },
                    { "valid", valid },
                    { "message", valid ? "验证通过" : "验证失败" }
                };
                return valid ? XHDResult.Success(obj).ToString() : XHDResult.Error(obj.ToString()).ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VerifyCDKey failed");
                return XHDResult.Error("验证失败：" + ex.Message).ToString();
            }
        }

        /// <summary>
        /// Sprint 9 #147 MailSender.SendMail：发送邮件。
        /// </summary>
        /// <param name="req">请求体 <c>{ "recipient": "...", "subject": "...", "body": "...", "isBodyHtml": false }</c></param>
        /// <returns>XHDResult</returns>
        [HttpPost("SendMail")]
        public async Task<string> SendMail([FromBody] SendMailRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Recipient) || string.IsNullOrWhiteSpace(req.Subject))
            {
                return XHDResult.Error("recipient 和 subject 不能为空").ToString();
            }
            try
            {
                bool ok = await _mail.SendMailAsync(req.Recipient, req.Subject, req.Body ?? string.Empty, req.IsBodyHtml);
                return ok
                    ? XHDResult.Success("邮件发送成功").ToString()
                    : XHDResult.Error("邮件发送失败").ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendMail failed");
                return XHDResult.Error("邮件发送失败：" + ex.Message).ToString();
            }
        }

        /// <summary>
        /// Sprint 9 #148 RSACryption 密钥生成：生成 RSA 密钥对。
        /// </summary>
        /// <param name="keySize">密钥长度（bit），默认 2048</param>
        /// <returns>XHDResult；Data 包含 privateKey / publicKey / keySize</returns>
        [HttpGet("Rsa/GenerateKeyPair")]
        public async Task<string> GenerateRsaKeyPair(int keySize = 2048)
        {
            try
            {
                var pair = await _rsa.GenerateKeyPairAsync(keySize);
                var obj = new JObject
                {
                    { "keySize", pair.KeySize },
                    { "privateKey", pair.PrivateKey },
                    { "publicKey", pair.PublicKey }
                };
                return XHDResult.Success(obj).ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GenerateRsaKeyPair failed");
                return XHDResult.Error("生成密钥对失败：" + ex.Message).ToString();
            }
        }

        /// <summary>
        /// Sprint 9 #148 RSACryption.Encrypt：使用公钥加密。
        /// </summary>
        /// <param name="req">请求体 <c>{ "publicKey": "XML...", "plainText": "..." }</c></param>
        /// <returns>XHDResult；Data 包含 cipher</returns>
        [HttpPost("Rsa/Encrypt")]
        public async Task<string> RsaEncrypt([FromBody] RsaEncryptRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.PublicKey) || req.PlainText == null)
            {
                return XHDResult.Error("publicKey 和 plainText 不能为空").ToString();
            }
            try
            {
                string cipher = await _rsa.EncryptAsync(req.PublicKey, req.PlainText);
                if (string.IsNullOrEmpty(cipher))
                {
                    return XHDResult.Error("加密失败").ToString();
                }
                var obj = new JObject { { "cipher", cipher } };
                return XHDResult.Success(obj).ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RsaEncrypt failed");
                return XHDResult.Error("加密失败：" + ex.Message).ToString();
            }
        }

        /// <summary>
        /// Sprint 9 #148 RSACryption.Decrypt：使用私钥解密。
        /// </summary>
        /// <param name="req">请求体 <c>{ "privateKey": "XML...", "cipher": "Base64..." }</c></param>
        /// <returns>XHDResult；Data 包含 plainText</returns>
        [HttpPost("Rsa/Decrypt")]
        public async Task<string> RsaDecrypt([FromBody] RsaDecryptRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.PrivateKey) || string.IsNullOrWhiteSpace(req.Cipher))
            {
                return XHDResult.Error("privateKey 和 cipher 不能为空").ToString();
            }
            try
            {
                string plain = await _rsa.DecryptAsync(req.PrivateKey, req.Cipher);
                if (string.IsNullOrEmpty(plain))
                {
                    return XHDResult.Error("解密失败").ToString();
                }
                var obj = new JObject { { "plainText", plain } };
                return XHDResult.Success(obj).ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RsaDecrypt failed");
                return XHDResult.Error("解密失败：" + ex.Message).ToString();
            }
        }

        /// <summary>
        /// Sprint 9 #158 DataCache.GetDataCache：按 key 读取缓存。
        /// </summary>
        /// <param name="key">缓存键（query string）</param>
        /// <returns>XHDResult；Data 包含 key / value / found 字段</returns>
        [HttpGet("Cache/Get")]
        public async Task<string> CacheGet(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return XHDResult.Error("key 不能为空").ToString();
            }
            try
            {
                object value = await _cache.GetCacheAsync(key);
                bool found = value != null;
                var obj = new JObject
                {
                    { "key", key },
                    { "found", found },
                    { "value", found ? ToJsonValue(value) : JValue.CreateNull() }
                };
                return XHDResult.Success(obj).ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CacheGet failed");
                return XHDResult.Error("缓存读取失败：" + ex.Message).ToString();
            }
        }

        /// <summary>
        /// 缓存值 → JToken 转换（避免直接塞 JObject 导致类型不匹配）。
        /// </summary>
        private static JToken ToJsonValue(object value)
        {
            if (value is JToken jt)
            {
                return jt;
            }
            if (value is JArray ja)
            {
                return ja;
            }
            return JToken.FromObject(value);
        }
    }

    /// <summary>
    /// GenerateCDKey 请求 DTO。
    /// </summary>
    public class GenerateCDKeyRequest
    {
        public string MachineCode { get; set; }
    }

    /// <summary>
    /// VerifyCDKey 请求 DTO。
    /// </summary>
    public class VerifyCDKeyRequest
    {
        public string MachineCode { get; set; }
        public string Cdkey { get; set; }
    }

    /// <summary>
    /// SendMail 请求 DTO。
    /// </summary>
    public class SendMailRequest
    {
        public string Recipient { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public bool IsBodyHtml { get; set; }
    }

    /// <summary>
    /// RsaEncrypt 请求 DTO。
    /// </summary>
    public class RsaEncryptRequest
    {
        public string PublicKey { get; set; }
        public string PlainText { get; set; }
    }

    /// <summary>
    /// RsaDecrypt 请求 DTO。
    /// </summary>
    public class RsaDecryptRequest
    {
        public string PrivateKey { get; set; }
        public string Cipher { get; set; }
    }
}
