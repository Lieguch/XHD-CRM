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
    /// 角色-员工 关联表
    /// Sprint 5 Wave 1 新增：B 侧原本仅有 hr_employee.role_id 单值字段，
    /// A 侧通过此表支持一名员工绑定多角色。
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public partial class Sys_role_emp {
        /// <summary>
        /// 主键id（B 侧规范统一带主键，避免 Composite PK）
        /// </summary>
        [JsonProperty, Column(StringLength = 50, IsPrimary = true)]
        public string id { get; set; } = string.Empty;

        /// <summary>
        /// 角色id（FK → Sys_role.id）
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string role_id { get; set; } = string.Empty;

        /// <summary>
        /// 员工id（FK → hr_employee.id）
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string emp_id { get; set; } = string.Empty;

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
        /// 角色导航
        /// </summary>
        [JsonProperty]
        public Sys_role Role { get; set; }

        /// <summary>
        /// 员工导航
        /// </summary>
        [JsonProperty]
        public hr_employee Employee { get; set; }
    }
}
