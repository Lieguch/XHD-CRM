using System;

namespace XHD.Core.View.Models.Dtos
{
    /// <summary>
    /// Sprint 3 #12：客户总数 KPI 查询 DTO。
    /// 所有字段对应 A 侧 Server.CRM_Customer.c_count 中的动态过滤参数。
    /// </summary>
    public class CustomerCountQuery
    {
        /// <summary>
        /// 归属员工 ID
        /// </summary>
        public string emp_id { get; set; }

        /// <summary>
        /// 行业 ID
        /// </summary>
        public string industry_val { get; set; }

        /// <summary>
        /// 客户类型 ID
        /// </summary>
        public string cus_type_id { get; set; }

        /// <summary>
        /// 客户等级 ID
        /// </summary>
        public string cus_level_id { get; set; }

        /// <summary>
        /// 客户来源 ID
        /// </summary>
        public string cus_source_id { get; set; }

        /// <summary>
        /// 创建起始时间
        /// </summary>
        public string startdate { get; set; }

        /// <summary>
        /// 创建结束时间
        /// </summary>
        public string enddate { get; set; }

        /// <summary>
        /// 最后跟进起始时间
        /// </summary>
        public string startfollow { get; set; }

        /// <summary>
        /// 最后跟进结束时间
        /// </summary>
        public string endfollow { get; set; }

        /// <summary>
        /// 省份 ID
        /// </summary>
        public string Provinces_val { get; set; }

        /// <summary>
        /// 城市 ID
        /// </summary>
        public string City_val { get; set; }

        /// <summary>
        /// 公私标记（0=私客 1=公客），可选
        /// </summary>
        public string isPrivate { get; set; }
    }
}
