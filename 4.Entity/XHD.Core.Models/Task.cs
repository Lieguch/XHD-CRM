
using System;
using Newtonsoft.Json;
using FreeSql.DataAnnotations;

namespace XHD.Core.Models
{
    /// <summary>
    /// 任务实体
    /// Sprint 10.22：将 A 版本 Model.Task（12 字段）移植到 B 侧，
    /// 补齐 Sprint 8 遗留的 Task_follow.task_id orphan 字段问题。
    /// 对应 A 侧 Model.Task / BLL.Task（ext_rar2018/Model/Task.cs）。
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public partial class Task
    {
        /// <summary>
        /// 主键
        /// </summary>
        [JsonProperty, Column(StringLength = 50, IsPrimary = true)]
        public string id { get; set; } = string.Empty;

        /// <summary>
        /// 任务标题
        /// </summary>
        [JsonProperty, Column(StringLength = 200)]
        public string task_title { get; set; } = string.Empty;

        /// <summary>
        /// 任务内容（大文本）
        /// </summary>
        [JsonProperty, Column(StringLength = -1)]
        public string task_content { get; set; } = string.Empty;

        /// <summary>
        /// 任务类别 ID（对应 Sys_Param，params_type=TaskType）
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string task_type_id { get; set; } = string.Empty;

        /// <summary>
        /// 相关客户 ID（软引用，可为空）
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string customer_id { get; set; } = string.Empty;

        /// <summary>
        /// 指派人 ID
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string assign_id { get; set; } = string.Empty;

        /// <summary>
        /// 执行人 ID
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string executive_id { get; set; } = string.Empty;

        /// <summary>
        /// 执行（截止）时间；逾期时前端以红色背景显示
        /// </summary>
        [JsonProperty]
        public DateTime? executive_time { get; set; }

        /// <summary>
        /// 任务状态：0=进行中 / 1=已完成 / 2=已中止
        /// </summary>
        [JsonProperty]
        public int? task_status_id { get; set; } = 0;

        /// <summary>
        /// 优先级：0=高 / 1=中 / 2=低
        /// </summary>
        [JsonProperty]
        public int? priority_id { get; set; } = 1;

        /// <summary>
        /// 提醒时间
        /// </summary>
        [JsonProperty]
        public DateTime? remind_time { get; set; }

        /// <summary>
        /// 完成勾选（0=未勾选 / 1=已勾选），与 task_status_id=1 配合使用
        /// </summary>
        [JsonProperty]
        public int? is_check { get; set; } = 0;

        /// <summary>
        /// 创建人 ID
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
