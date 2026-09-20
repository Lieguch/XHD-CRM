using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using FreeSql.DataAnnotations;

namespace XHD.Core.Models {
    /// <summary>
    /// 岗位表（含员工岗位分配）
    /// Sprint 5 Wave 1 新增：与 hr_position（职务级别）区分，
    /// 一个部门内的具体岗位，可挂接员工与职务级别。
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public partial class hr_post {
        /// <summary>
        /// 岗位id
        /// </summary>
        [JsonProperty, Column(StringLength = 50, IsPrimary = true)]
        public string id { get; set; } = string.Empty;

        /// <summary>
        /// 岗位名称
        /// </summary>
        [JsonProperty, Column(StringLength = 250)]
        public string post_name { get; set; } = string.Empty;

        /// <summary>
        /// 职务级别id（FK → hr_position.id）
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string position_id { get; set; } = string.Empty;

        /// <summary>
        /// 部门id（FK → hr_department.id）
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string dep_id { get; set; } = string.Empty;

        /// <summary>
        /// 所属员工id（FK → hr_employee.id）；空字符串表示此岗位未分配员工
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string emp_id { get; set; } = string.Empty;

        /// <summary>
        /// 是否此员工的默认岗位（0 否 / 1 是）
        /// </summary>
        [JsonProperty]
        public int? default_post { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        [JsonProperty]
        public string note { get; set; } = string.Empty;

        /// <summary>
        /// 岗位描述（补充）
        /// </summary>
        [JsonProperty]
        public string post_descript { get; set; } = string.Empty;

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

        /// <summary>
        /// 部门
        /// </summary>
        [JsonProperty]
        public hr_department department { get; set; }

        /// <summary>
        /// 职务级别
        /// </summary>
        [JsonProperty]
        public hr_position position { get; set; }

        /// <summary>
        /// 岗位员工
        /// </summary>
        [JsonProperty]
        public hr_employee employee { get; set; }
    }
}
