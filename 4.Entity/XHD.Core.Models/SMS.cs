using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using FreeSql.DataAnnotations;

namespace XHD.Core.Models
{
    /// <summary>
    /// 短信主表
    /// Sprint 7 新增：#124 SMS.send 更新 isSend/sendtime/check_id 三字段。
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public partial class SMS
    {
        /// <summary>
        /// 短信 id（GUID）
        /// </summary>
        [JsonProperty, Column(StringLength = 50, IsPrimary = true)]
        public string id { get; set; } = string.Empty;

        /// <summary>
        /// 短信标题
        /// </summary>
        [JsonProperty, Column(StringLength = 255)]
        public string sms_title { get; set; } = string.Empty;

        /// <summary>
        /// 短信内容（含【公司名】前缀）
        /// </summary>
        [JsonProperty]
        public string sms_content { get; set; } = string.Empty;

        /// <summary>
        /// 联系人 id 列表（逗号分隔）
        /// </summary>
        [JsonProperty, Column(StringLength = 2000)]
        public string contact_ids { get; set; } = string.Empty;

        /// <summary>
        /// 手机号列表（逗号分隔）
        /// </summary>
        [JsonProperty, Column(StringLength = 2000)]
        public string sms_mobiles { get; set; } = string.Empty;

        /// <summary>
        /// 是否发送（0 未发送 / 1 已发送）
        /// </summary>
        [JsonProperty]
        public int? isSend { get; set; }

        /// <summary>
        /// 发送时间
        /// </summary>
        [JsonProperty]
        public DateTime? sendtime { get; set; }

        /// <summary>
        /// 审核人 id
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string check_id { get; set; } = string.Empty;

        /// <summary>
        /// 创建人 id
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string create_id { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        [JsonProperty]
        public DateTime? create_time { get; set; }
    }
}
