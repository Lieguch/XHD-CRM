
using System;
using Newtonsoft.Json;
using FreeSql.DataAnnotations;

namespace XHD.Core.Models
{
    /// <summary>
    /// 系统错误日志
    /// Sprint 8 #41：首次引入（与 Sys_log 是独立表，字段结构不同）
    /// 对应 A 侧 Model.Sys_log_Err / DAL.Sys_log_Err / BLL.Sys_log_Err
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public partial class Sys_log_Err
    {
        /// <summary>
        /// 主键
        /// </summary>
        [JsonProperty, Column(StringLength = 50, IsPrimary = true)]
        public string id { get; set; } = string.Empty;

        /// <summary>
        /// 错误类型 ID（字典）
        /// </summary>
        [JsonProperty]
        public int? Err_typeid { get; set; }

        /// <summary>
        /// 错误类型名称
        /// </summary>
        [JsonProperty, Column(StringLength = 100)]
        public string Err_type { get; set; } = string.Empty;

        /// <summary>
        /// 错误时间
        /// </summary>
        [JsonProperty]
        public DateTime Err_time { get; set; } = DateTime.Now;

        /// <summary>
        /// 错误 URL
        /// </summary>
        [JsonProperty, Column(StringLength = -1)]
        public string Err_url { get; set; } = string.Empty;

        /// <summary>
        /// 错误消息
        /// </summary>
        [JsonProperty, Column(StringLength = -1)]
        public string Err_message { get; set; } = string.Empty;

        /// <summary>
        /// 错误来源
        /// </summary>
        [JsonProperty, Column(StringLength = 250)]
        public string Err_source { get; set; } = string.Empty;

        /// <summary>
        /// 错误堆栈
        /// </summary>
        [JsonProperty, Column(StringLength = -1)]
        public string Err_trace { get; set; } = string.Empty;

        /// <summary>
        /// 触发员工 ID
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string Err_emp_id { get; set; } = string.Empty;

        /// <summary>
        /// 触发员工名称
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string Err_emp_name { get; set; } = string.Empty;

        /// <summary>
        /// 触发 IP
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string Err_ip { get; set; } = string.Empty;
    }
}
