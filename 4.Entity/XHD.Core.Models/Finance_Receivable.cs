
using System;
using Newtonsoft.Json;
using FreeSql.DataAnnotations;

namespace XHD.Core.Models
{
    /// <summary>
    /// 应收单
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public partial class Finance_Receivable
    {
        /// <summary>
        /// 应收单id
        /// </summary>
        [JsonProperty, Column(StringLength = 50, IsPrimary = true)]
        public string id { get; set; } = string.Empty;

        /// <summary>
        /// 应收单号
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string receivable_no { get; set; } = string.Empty;

        /// <summary>
        /// 订单id
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string order_id { get; set; } = string.Empty;

        /// <summary>
        /// 应收时间
        /// </summary>
        [JsonProperty]
        public DateTime? receivable_time { get; set; }

        /// <summary>
        /// 应收金额
        /// </summary>
        [JsonProperty, Column(DbType = "decimal(18,2)")]
        public decimal? receivable_amount { get; set; }

        /// <summary>
        /// 已收金额
        /// </summary>
        [JsonProperty, Column(DbType = "decimal(18,2)")]
        public decimal? received_amount { get; set; }

        /// <summary>
        /// 未收金额
        /// </summary>
        [JsonProperty, Column(DbType = "decimal(18,2)")]
        public decimal? arrears_amount { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [JsonProperty, Column(StringLength = -1)]
        public string Remark { get; set; } = string.Empty;

        /// <summary>
        /// 创建人id
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string create_id { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        [JsonProperty]
        public DateTime? create_time { get; set; }

        /// <summary>
        /// 订单
        /// </summary>
        [JsonProperty]
        public Sale_order Order { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        [JsonProperty]
        public hr_employee Creater { get; set; }

        /// <summary>
        /// 是否删除
        /// </summary>
        [JsonProperty]
        public int? isDelete { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        [JsonProperty]
        public DateTime? Delete_time { get; set; }

        /// <summary>
        /// 删除人id
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string Delete_id { get; set; } = string.Empty;

        /// <summary>
        /// 删除人
        /// </summary>
        [JsonProperty]
        public hr_employee Deleter { get; set; }
    }
}
