
using System;
using Newtonsoft.Json;
using FreeSql.DataAnnotations;

namespace XHD.Core.Models
{
    /// <summary>
    /// 任务跟进记录
    /// Sprint 8 #39：首次引入（与 CRM_follow 是独立表，字段结构不同）
    /// 对应 A 侧 Model.Task_follow / DAL.Task_follow / BLL.Task_follow
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public partial class Task_follow
    {
        /// <summary>
        /// 主键
        /// </summary>
        [JsonProperty, Column(StringLength = 50, IsPrimary = true)]
        public string id { get; set; } = string.Empty;

        /// <summary>
        /// 任务 ID（级联删除的关键字段）
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string task_id { get; set; } = string.Empty;

        /// <summary>
        /// 跟进人 ID
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string follow_id { get; set; } = string.Empty;

        /// <summary>
        /// 跟进时间
        /// </summary>
        [JsonProperty]
        public DateTime? follow_time { get; set; }

        /// <summary>
        /// 跟进内容
        /// </summary>
        [JsonProperty, Column(StringLength = -1)]
        public string follow_content { get; set; } = string.Empty;

        /// <summary>
        /// 跟进状态（0=进行中 / 1=完成）
        /// </summary>
        [JsonProperty]
        public int? follow_status { get; set; }
    }
}
