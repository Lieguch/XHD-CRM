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
    /// 在线用户表
    /// Sprint 7 新增：用于 Sys_base.GetOnline（#102）与 getUserTree（#101）在线判定。
    /// 主键 UserID 采用员工 id（varchar50），对齐 A 侧 Sys_online.UserID 字段。
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public partial class Sys_online
    {
        /// <summary>
        /// 用户 id（员工 id，主键）
        /// </summary>
        [JsonProperty, Column(StringLength = 50, IsPrimary = true)]
        public string UserID { get; set; } = string.Empty;

        /// <summary>
        /// 用户名
        /// </summary>
        [JsonProperty, Column(StringLength = 250)]
        public string UserName { get; set; } = string.Empty;

        /// <summary>
        /// 最后登录时间
        /// </summary>
        [JsonProperty]
        public DateTime? LastLogTime { get; set; }
    }
}
