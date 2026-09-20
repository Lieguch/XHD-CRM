using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XHD.Core.Common.Excel
{
    /// <summary>
    /// Excel 导入统一返回结果（Sprint 4 Wave 3 #04/#05/#06 通用）。
    /// 语义：
    ///   Success — 成功新增条数（管理员覆盖场景下 = 新增条数）
    ///   Update  — 成功覆盖条数（普通导入场景 = 0，只有 AdminImport 会累加）
    ///   Error   — 失败条数（缺失必填 / 代码表值找不到 / 主键冲突 / 数据行解析失败等）
    ///   Message — 逐条错误详情（多段用 "；" 分隔），供前端/日志直接展示
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ExcelImportResult
    {
        /// <summary>成功新增条数</summary>
        [JsonProperty]
        public int Success { get; set; } = 0;

        /// <summary>成功覆盖更新条数（仅管理员覆盖导入累加）</summary>
        [JsonProperty]
        public int Update { get; set; } = 0;

        /// <summary>失败条数（含跳过）</summary>
        [JsonProperty]
        public int Error { get; set; } = 0;

        /// <summary>错误详情（每条一段，用 "；" 分隔）</summary>
        [JsonProperty]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 组装最终返回的 JSON 消息字符串。
        /// 与 A 侧 Excel.Excel_new.Import() 返回格式对齐（`{success,error,message}`）：
        /// `{"success":N,"update":M,"error":E,"message":"..."}`
        /// </summary>
        public string ToJson()
        {
            var o = new JObject
            {
                ["success"] = Success,
                ["update"] = Update,
                ["error"] = Error,
                ["message"] = Message ?? string.Empty
            };
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// 转换为字典，用于写入 XHDResult.data 字段（保留 success/update/error/message 键）。
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["success"] = Success,
                ["update"] = Update,
                ["error"] = Error,
                ["message"] = Message ?? string.Empty,
            };
        }

        /// <summary>追加一条失败记录（Error++，Message 拼接一行详情）</summary>
        public void Fail(int rowNum, string reason)
        {
            Error++;
            AppendMessage($"第【{rowNum}】行【{reason}】");
        }

        /// <summary>追加一条失败记录（Error++，Message 拼接原始详情）</summary>
        public void Fail(string detail)
        {
            Error++;
            AppendMessage(detail);
        }

        /// <summary>追加一条成功记录</summary>
        public void Add()
        {
            Success++;
        }

        /// <summary>追加一条覆盖更新记录（AdminImport 专用）</summary>
        public void AddUpdate()
        {
            Update++;
        }

        /// <summary>是否完全成功（无失败、无更新，全部新增；或无失败且至少有一条新增）</summary>
        public bool IsAllSuccess => Error == 0 && (Success + Update) > 0;

        private void AppendMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Message = string.IsNullOrEmpty(Message) ? text : Message + "；" + text;
        }
    }
}
