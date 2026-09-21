using System;
using System.Linq;
using System.Threading.Tasks;
using XHD.Core.Models;
using XHD.Core.Common;
using XHD.Core.Common.DEncrypt;
using XHD.Core.Common.SMS;
using XHD.Core.IRepository;
using XHD.Core.IServices;
using Newtonsoft.Json.Linq;

namespace XHD.Core.Services
{
    /// <summary>
    /// SMS 服务实现
    /// Sprint 7 新增：#124 SMS.send + #125 getBalance。
    /// </summary>
    public class SMSService : ISMSService
    {
        private readonly ISMSRepository _smsRepo;
        private readonly ISys_infoService _infoService;
        private readonly ISMSHelper _helper;

        public SMSService(
            ISMSRepository smsRepo,
            ISys_infoService infoService,
            ISMSHelper helper)
        {
            _smsRepo = smsRepo;
            _infoService = infoService;
            _helper = helper;
        }

        /// <summary>
        /// #124 SMS.send：读取 SMS 记录 → 调 SMSHelper → 成功则更新状态。
        /// </summary>
        public async Task<string> SendAsync(string smsId, string empId)
        {
            if (string.IsNullOrWhiteSpace(smsId))
            {
                return XHDResult.Error("参数错误！").ToString();
            }

            if (string.IsNullOrWhiteSpace(empId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var sms = await _smsRepo.GetByIdAsync(smsId);
            if (sms == null)
            {
                return XHDResult.Error("系统错误，无数据！").ToString();
            }

            // 读取 SMS 服务商配置
            var (serialNo, key) = await LoadSmsCredentialsAsync();
            if (string.IsNullOrWhiteSpace(serialNo) || string.IsNullOrWhiteSpace(key))
            {
                return XHDResult.Error("短信服务未配置，请先在系统信息中配置短信服务商").ToString();
            }

            // 解析手机号
            var mobiles = (sms.sms_mobiles ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            // GUID → long smsid（对齐 A 侧 BitConverter.ToInt64 语义）
            long smsNumId;
            if (Guid.TryParse(smsId, out var guid))
            {
                smsNumId = BitConverter.ToInt64(guid.ToByteArray(), 0);
            }
            else
            {
                return XHDResult.Error("短信 ID 格式错误").ToString();
            }

            int result;
            try
            {
                result = _helper.SendSMS(serialNo, key, mobiles, sms.sms_content, smsNumId);
            }
            catch (Exception ex)
            {
                return XHDResult.Error("短信发送异常：" + ex.Message).ToString();
            }

            if (result == 0)
            {
                await _smsRepo.UpdateSendStateAsync(smsId, empId, DateTime.Now);
                return XHDResult.Success("发送成功！").ToString();
            }

            return XHDResult.Error(ISMSHelper.SmsResult(result)).ToString();
        }

        /// <summary>
        /// #125 getBalance：查询余额。
        /// 未配置 SMS 时返回 0（不抛异常）。
        /// </summary>
        public async Task<double> GetBalanceAsync()
        {
            var (serialNo, key) = await LoadSmsCredentialsAsync();
            if (string.IsNullOrWhiteSpace(serialNo) || string.IsNullOrWhiteSpace(key))
            {
                return 0;
            }

            try
            {
                return _helper.GetBalance(serialNo, key);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 从 sys_info 读取 SMS 服务商凭据（sms_no / sms_key 解密 / sms_done）。
        /// </summary>
        private async Task<(string serialNo, string key)> LoadSmsCredentialsAsync()
        {
            var all = await _infoService.GridAsync(a => 1 == 1);
            var list = all?.data ?? new System.Collections.Generic.List<Sys_info>();

            var serialNo = "";
            var key = "";
            foreach (var row in list)
            {
                if (row.sys_key == "sms_no") serialNo = row.sys_value ?? "";
                else if (row.sys_key == "sms_key")
                {
                    try { key = DESEncrypt.Decrypt(row.sys_value ?? ""); }
                    catch { key = ""; }
                }
            }
            return (serialNo, key);
        }
    }
}
