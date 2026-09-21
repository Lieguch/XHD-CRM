
using System;

namespace XHD.Core.Common.SMS
{
    /// <summary>
    /// 短信状态报告 DTO（对应 A 侧 SMS_Server.statusReport）。
    /// Sprint 8 #157 QueryStatus 使用：服务商拉取已发送短信的回执状态。
    /// </summary>
    public class SMSStatusReport
    {
        /// <summary>
        /// 接收手机号
        /// </summary>
        public string Phone { get; set; } = string.Empty;

        /// <summary>
        /// 短信内容
        /// </summary>
        public string SmsContent { get; set; } = string.Empty;

        /// <summary>
        /// 状态码（0=成功 / 非 0=失败）
        /// </summary>
        public int SmsStatus { get; set; }

        /// <summary>
        /// 错误描述（成功时为空）
        /// </summary>
        public string Err { get; set; } = string.Empty;

        public SMSStatusReport()
        {
        }

        public SMSStatusReport(string phone, string smsContent, int smsStatus, string err)
        {
            Phone = phone ?? string.Empty;
            SmsContent = smsContent ?? string.Empty;
            SmsStatus = smsStatus;
            Err = err ?? string.Empty;
        }
    }
}
