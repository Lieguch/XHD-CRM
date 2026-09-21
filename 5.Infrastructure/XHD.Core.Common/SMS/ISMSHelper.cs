using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XHD.Core.Common.SMS
{
    /// <summary>
    /// 短信服务商抽象接口
    /// Sprint 7 新增：抽象 A 侧 SMSHelper（Yimei SMS Server WebService）。
    /// B 侧通过 DI 注入，测试使用 Moq 桥接，避免真实 HTTP 调用。
    /// 错误码约定：0=成功，非 0 由 <see cref="SmsResult"/> 映射中文消息。
    /// </summary>
    public interface ISMSHelper
    {
        /// <summary>
        /// 注册企业版短信账号（#115 regSMS 调用）。
        /// </summary>
        /// <param name="softwareSerialNo">软件序列号</param>
        /// <param name="key">密钥</param>
        /// <param name="serialpass">软件口令</param>
        /// <returns>0 成功，非 0 错误码</returns>
        int RegistEx(string softwareSerialNo, string key, string serialpass);

        /// <summary>
        /// 发送短信（#124 SMS.send 调用）。
        /// </summary>
        /// <param name="softwareSerialNo">软件序列号</param>
        /// <param name="key">密钥</param>
        /// <param name="mobiles">手机号数组</param>
        /// <param name="smsContent">短信内容（含【公司名】前缀）</param>
        /// <param name="smsId">短信记录 ID（长整型，由 GUID 转换）</param>
        /// <returns>0 成功，非 0 错误码</returns>
        int SendSMS(string softwareSerialNo, string key, string[] mobiles, string smsContent, long smsId);

        /// <summary>
        /// 查询短信余额（#125 getBalance 调用）。
        /// </summary>
        /// <param name="softwareSerialNo">软件序列号</param>
        /// <param name="key">密钥</param>
        /// <returns>剩余条数（double，兼容 A 侧返回类型）</returns>
        double GetBalance(string softwareSerialNo, string key);

        /// <summary>
        /// 错误码转中文消息（静态工具，无外部调用）。
        /// </summary>
        /// <param name="code">错误码</param>
        /// <returns>中文错误消息</returns>
        static string SmsResult(int code)
        {
            return code switch
            {
                0 => "成功",
                -1 => "序列号或密钥错误",
                -2 => "序列号无剩余短信",
                -3 => "网络错误",
                -4 => "服务器繁忙",
                -5 => "手机号格式错误",
                -6 => "短信内容超长",
                -7 => "未通过审核",
                -8 => "账号未注册",
                _ => $"未知错误（code={code}）"
            };
        }
    }
}
