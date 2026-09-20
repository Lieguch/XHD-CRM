
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
	/// 新闻
	/// </summary>
	[JsonObject(MemberSerialization.OptIn)]
	public partial class Message_news {
		/// <summary>
		/// 主键
		/// </summary>
		[JsonProperty, Column(StringLength = 50, IsPrimary = true)]
		public string id { get; set; } = string.Empty;

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
		/// 新闻内容
		/// </summary>
		[JsonProperty, Column(StringLength = -1)]
		public string news_content { get; set; } = string.Empty;

		/// <summary>
		/// 新闻标题
		/// </summary>
		[JsonProperty, Column(StringLength = 250)]
		public string news_title { get; set; } = string.Empty;

		/// <summary>
		/// 新闻类别id
		/// </summary>
		[JsonProperty, Column(StringLength = 50)]
		public string news_type_id { get; set; } = string.Empty;

		/// <summary>
		/// 新闻类别
		/// </summary>
		[JsonProperty]
		public Sys_Param NewsType { get; set; }

		/// <summary>
		/// 是否已读（false 未读 / true 已读）
		/// Sprint 4 Wave 2 #15 noticeremind 提醒列表依赖
		/// </summary>
		[JsonProperty]
		public bool isRead { get; set; } = false;

		/// <summary>
		/// 阅读时间
		/// Sprint 4 Wave 2 #15 noticeremind 提醒列表依赖
		/// </summary>
		[JsonProperty]
		public DateTime? read_time { get; set; }

	}

}
