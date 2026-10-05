using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using FreeSql.DataAnnotations;

namespace XHD.Core.Models {
    /// <summary>
    /// 数据权限-指定部门 表（缺口 E）
    /// Sprint 10.43 新增：对齐 A 版 Model/Sys_data_authority.cs。
    /// 当角色 Sys_role.DataAuth == 4（指定部门）时，本表记录该角色勾选的部门集合；
    /// DBAuthRepository.GetDataAuth 的 case 4 据此展开可见员工 id 列表
    /// （对应 A 版 Controller/GetDataAuth.cs:187 get_depAp_emp_ids）。
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public partial class Sys_data_authority {
        /// <summary>
        /// 主键id（B 侧规范统一带主键，A 版表无此列，为联合业务键 Role_id+dep_id）
        /// </summary>
        [JsonProperty, Column(StringLength = 50, IsPrimary = true)]
        public string id { get; set; } = string.Empty;

        /// <summary>
        /// 角色id（FK → Sys_role.id）
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string Role_id { get; set; } = string.Empty;

        /// <summary>
        /// 部门id（FK → hr_department.id）
        /// </summary>
        [JsonProperty, Column(StringLength = 50)]
        public string dep_id { get; set; } = string.Empty;

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
    }
}
