using System;
using System.Threading.Tasks;
using XHD.Core.Models;
using XHD.Core.Common;
using Newtonsoft.Json.Linq;

namespace XHD.Core.IServices
{
    /// <summary>
    /// SMS 服务接口
    /// Sprint 7 新增：#124 SMS.send + #125 getBalance 集中实现。
    /// </summary>
    public interface ISMSService
    {
        /// <summary>
        /// Sprint 7 #124 SMS.send：发送短信，更新 isSend/sendtime/check_id。
        /// </summary>
        /// <param name="smsId">短信 id（GUID）</param>
        /// <param name="empId">审核人 id</param>
        /// <returns>标准 XHDResult 字符串</returns>
        Task<string> SendAsync(string smsId, string empId);

        /// <summary>
        /// Sprint 7 #125 getBalance：查询短信余额（double）。
        /// </summary>
        /// <returns>余额（未配置或异常时返回 0）</returns>
        Task<double> GetBalanceAsync();

        /// <summary>
        /// Sprint 8 #157 getReport：查询短信状态报告。
        /// </summary>
        /// <returns>状态报告 JArray（未配置或异常时返回空 JArray）</returns>
        Task<JArray> QueryStatusAsync();
    }
}
