using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using MiniExcelLibs;
using UUIDNext;
using XHD.Core.Common.Excel;
using XHD.Core.Models;

namespace XHD.Core.View.Helpers
{
    /// <summary>
    /// Excel 导入代码表数据束（Sprint 4 Wave 3 使用）。
    /// 每个字典 key = 中文/文本值，value = 数据库主键 ID。
    /// 由 <see cref="ExcelImportHelper.LoadCodeTablesAsync"/> 一次性加载，避免逐行 DB 查询。
    /// </summary>
    public class CodeTableBundle
    {
        /// <summary>行业：params_type='cus_industry'，params_name → id</summary>
        public Dictionary<string, string> Industry { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>客户类型：params_type='cus_type'</summary>
        public Dictionary<string, string> Type { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>客户级别：params_type='cus_level'</summary>
        public Dictionary<string, string> Level { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>客户来源：params_type='cus_source'</summary>
        public Dictionary<string, string> Source { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>省份：Sys_Param_Provinces.Provinces → id（限 Provinces_type='sys'）</summary>
        public Dictionary<string, string> Provinces { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>城市：Sys_Param_City.City → id（限 City_type='sys'）</summary>
        public Dictionary<string, string> Cities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>员工：hr_employee.name → id（供 "员工/归属" 列解析）</summary>
        public Dictionary<string, string> Employees { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>客户：CRM_Customer.cus_name → id（供联系人的 "客户名字/客户名称" 列解析）</summary>
        public Dictionary<string, string> Customers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Excel 导入助手（Sprint 4 Wave 3 #04/#05/#06）。
    /// 对应 A 侧 Excel.Excel_new（ext_rar2018/Excel/Excel_new.cs）——但 B 侧统一用 MiniExcel 读取，
    /// 不用 NPOI 依赖。列名与 A 侧模板 Customer.xml / contact.xml 的 HeaderText 100% 对齐，
    /// 并额外兼容 B 侧 Export 端点输出的中文列名（如 "客户名字"、"网址"）。
    /// </summary>
    public static class ExcelImportHelper
    {
        /// <summary>Excel 单文件大小上限（10 MB）——超出则视为不合法文件</summary>
        public const long MaxFileSizeBytes = 10 * 1024 * 1024;

        /// <summary>
        /// 读取 Excel 文件的所有数据行（第一行视为表头）。
        /// MiniExcel 1.46.0 的 QueryAsync(useHeaderRow:true) 返回 Task&lt;IEnumerable&lt;dynamic&gt;&gt;，
        /// 每个 dynamic 实际为 ExpandoObject（实现 IDictionary&lt;string,object&gt;），
        /// 字典 key 为表头文本。此处统一转换为 Dictionary&lt;string, object&gt; 供下游使用。
        /// </summary>
        public static async Task<List<Dictionary<string, object>>> ReadRowsAsync(Stream stream)
        {
            if (stream == null) return new List<Dictionary<string, object>>();
            var rows = await stream.QueryAsync(useHeaderRow: true);
            var result = new List<Dictionary<string, object>>();
            foreach (var row in rows)
            {
                if (row == null) continue;
                if (row is IDictionary<string, object> dict)
                {
                    result.Add(new Dictionary<string, object>(dict));
                }
                else if (row is Dictionary<string, object> direct)
                {
                    result.Add(direct);
                }
                else
                {
                    // 兜底：尝试通过反射读取 ExpandoObject 的属性
                    var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in row.GetType().GetProperties())
                    {
                        d[prop.Name] = prop.GetValue(row);
                    }
                    result.Add(d);
                }
            }
            return result;
        }

        /// <summary>
        /// 一次性加载 Excel 导入需要的所有代码表。
        /// </summary>
        public static async Task<CodeTableBundle> LoadCodeTablesAsync(IFreeSql fsql)
        {
            var b = new CodeTableBundle();

            b.Industry = await LoadParamsByNameAsync(fsql, "cus_industry");
            b.Type = await LoadParamsByNameAsync(fsql, "cus_type");
            b.Level = await LoadParamsByNameAsync(fsql, "cus_level");
            b.Source = await LoadParamsByNameAsync(fsql, "cus_source");

            var provinces = await fsql.Select<Sys_Param_Provinces>()
                .Where(p => p.Provinces_type == "sys" && p.isDelete != 1)
                .ToListAsync();
            b.Provinces = provinces.ToDictionary(p => p.Provinces ?? "", p => p.id, StringComparer.OrdinalIgnoreCase);

            var cities = await fsql.Select<Sys_Param_City>()
                .Where(c => c.City_type == "sys" && c.isDelete != 1)
                .ToListAsync();
            b.Cities = cities.ToDictionary(c => c.City ?? "", c => c.id, StringComparer.OrdinalIgnoreCase);

            var emps = await fsql.Select<hr_employee>().ToListAsync();
            // 员工姓名可能重复，遇到重复保留首次出现的（A 侧模板 CodeKey=cus_emp 也是这个语义）
            foreach (var e in emps)
            {
                if (!string.IsNullOrWhiteSpace(e.name) && !b.Employees.ContainsKey(e.name))
                {
                    b.Employees[e.name] = e.id;
                }
            }

            var customers = await fsql.Select<CRM_Customer>()
                .Where(c => c.isDelete != 1)
                .ToListAsync();
            foreach (var c in customers)
            {
                if (!string.IsNullOrWhiteSpace(c.cus_name) && !b.Customers.ContainsKey(c.cus_name))
                {
                    b.Customers[c.cus_name] = c.id;
                }
            }

            return b;
        }

        /// <summary>
        /// 从单行 Excel 数据构建 CRM_Customer 实体。
        /// 返回值 = 成功构建的实体；null = 该行失败（已把失败原因写进 result）。
        /// </summary>
        public static CRM_Customer? BuildCustomer(
            Dictionary<string, object> row,
            string empId,
            CodeTableBundle tables,
            int rowNum,
            ExcelImportResult result)
        {
            // —— 客户名（必填）——
            var cusName = GetString(row, "客户名字") ?? GetString(row, "客户");
            if (string.IsNullOrWhiteSpace(cusName))
            {
                result.Fail(rowNum, "客户/客户名字不能为空");
                return null;
            }

            var cusAdd = GetString(row, "地址") ?? "";
            var cusTel = GetString(row, "电话") ?? "";
            var cusFax = GetString(row, "传真") ?? "";
            var cusWebsite = GetString(row, "网址") ?? GetString(row, "网站") ?? "";

            // 省份 / 城市（CodeKey 解析）
            var provincesText = GetString(row, "省份") ?? "";
            var cityText = GetString(row, "城市") ?? "";
            var provincesId = "";
            var cityId = "";
            if (!string.IsNullOrWhiteSpace(provincesText))
            {
                if (tables.Provinces.TryGetValue(provincesText, out var pid))
                {
                    provincesId = pid;
                }
                else
                {
                    result.Fail(rowNum, $"省份【{provincesText}】在系统中找不到");
                    return null;
                }
            }
            if (!string.IsNullOrWhiteSpace(cityText))
            {
                if (tables.Cities.TryGetValue(cityText, out var cid))
                {
                    cityId = cid;
                }
                else
                {
                    result.Fail(rowNum, $"城市【{cityText}】在系统中找不到");
                    return null;
                }
            }

            // 行业 / 客户类型 / 级别 / 来源（CodeKey 解析，字段名兼容 A 侧和 B 侧 Export 两套）
            var industryText = GetString(row, "行业") ?? "";
            var typeText = GetString(row, "客户类型") ?? GetString(row, "类别") ?? "";
            var levelText = GetString(row, "客户级别") ?? GetString(row, "级别") ?? "";
            var sourceText = GetString(row, "客户来源") ?? GetString(row, "来源") ?? "";

            var industryId = ResolveParam(industryText, tables.Industry, rowNum, "行业", result);
            if (industryId == null) return null;
            var typeId = ResolveParam(typeText, tables.Type, rowNum, "客户类型", result);
            if (typeId == null) return null;
            var levelId = ResolveParam(levelText, tables.Level, rowNum, "级别", result);
            if (levelId == null) return null;
            var sourceId = ResolveParam(sourceText, tables.Source, rowNum, "来源", result);
            if (sourceId == null) return null;

            // 员工归属（CodeKey 解析；未填则回落到当前登录人 empId）
            var empText = GetString(row, "员工") ?? GetString(row, "归属");
            var ownerId = empId;
            if (!string.IsNullOrWhiteSpace(empText))
            {
                if (tables.Employees.TryGetValue(empText, out var eid))
                {
                    ownerId = eid;
                }
                else
                {
                    result.Fail(rowNum, $"员工【{empText}】在系统中找不到");
                    return null;
                }
            }

            // 公私（"公客" → 1，"私客" → 0；默认 1=公客，与 A 侧模板 DefaultValue="0" 相反，
            // A 侧 "0" 语义是"公私" 字段的原始值 0=公客；B 侧 CRM_Customer.isPrivate 语义为 1=公客 / 0=私客）
            var isPrivate = ParseIsPrivate(GetString(row, "公私") ?? "公客");

            var entity = new CRM_Customer
            {
                id = Uuid.NewSequential().ToString(),
                cus_name = cusName,
                cus_add = cusAdd,
                cus_tel = cusTel,
                cus_fax = cusFax,
                cus_website = cusWebsite,
                Provinces_id = provincesId,
                City_id = cityId,
                cus_industry_id = industryId ?? "",
                cus_type_id = typeId ?? "",
                cus_level_id = levelId ?? "",
                cus_source_id = sourceId ?? "",
                emp_id = ownerId,
                create_id = empId,
                create_time = DateTime.Now,
                isPrivate = isPrivate,
                isDelete = 0,
                state = 0,
                DesCripe = GetString(row, "描述") ?? "",
                Remarks = GetString(row, "备注") ?? ""
            };

            return entity;
        }

        /// <summary>
        /// 从单行 Excel 数据构建 CRM_Contact 实体。
        /// 联系人必须有 C_name（姓名）+ customer_id（可通过 "客户名字/客户名称" 列解析，也可显式传 ID）。
        /// </summary>
        public static CRM_Contact? BuildContact(
            Dictionary<string, object> row,
            string empId,
            CodeTableBundle tables,
            int rowNum,
            ExcelImportResult result)
        {
            var cName = GetString(row, "姓名");
            if (string.IsNullOrWhiteSpace(cName))
            {
                result.Fail(rowNum, "姓名不能为空");
                return null;
            }

            var sexText = GetString(row, "性别");
            var cSex = ParseSex(sexText);

            // 客户归属：优先取 "客户名字/客户名称"（CodeKey），若为空则允许显式 customer_id
            var cusName = GetString(row, "客户名字") ?? GetString(row, "客户名称");
            var customerId = GetString(row, "customer_id");
            if (!string.IsNullOrWhiteSpace(cusName))
            {
                if (tables.Customers.TryGetValue(cusName, out var cid))
                {
                    customerId = cid;
                }
                else
                {
                    result.Fail(rowNum, $"客户【{cusName}】在系统中找不到");
                    return null;
                }
            }

            if (string.IsNullOrWhiteSpace(customerId))
            {
                result.Fail(rowNum, "客户名字/customer_id 不能为空");
                return null;
            }

            var entity = new CRM_Contact
            {
                id = Uuid.NewSequential().ToString(),
                C_name = cName,
                C_sex = cSex,
                C_birthday = GetString(row, "生日") ?? "",
                C_department = GetString(row, "部门") ?? "",
                C_position = GetString(row, "职务") ?? "",
                C_tel = GetString(row, "电话") ?? "",
                C_mob = GetString(row, "手机") ?? "",
                C_email = GetString(row, "邮箱") ?? GetString(row, "email") ?? "",
                C_QQ = GetString(row, "QQ") ?? "",
                C_add = GetString(row, "地址") ?? "",
                C_hobby = GetString(row, "爱好") ?? "",
                C_remarks = GetString(row, "备注") ?? "",
                customer_id = customerId,
                create_id = empId,
                create_time = DateTime.Now,
                isDelete = 0
            };

            return entity;
        }

        /// <summary>
        /// 生成一个内存 Excel 流（供单元测试使用）。
        /// 使用 DataTable 写入（首行为表头，后续行为数据），MiniExcel.QueryAsync 可直接反解析。
        /// </summary>
        public static MemoryStream BuildExcelStream(DataTable data, string sheetName = "Sheet1")
        {
            var ms = new MemoryStream();
            ms.SaveAs(data, sheetName: sheetName);
            return ms;
        }

        // ============ 私有辅助 ============

        private static async Task<Dictionary<string, string>> LoadParamsByNameAsync(IFreeSql fsql, string paramsType)
        {
            var list = await fsql.Select<Sys_Param>()
                .Where(p => p.params_type == paramsType && p.isDelete != 1)
                .ToListAsync();
            return list.ToDictionary(p => p.params_name ?? "", p => p.id, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 从字典安全取值；若不存在或 null 则返回 ""。
        /// </summary>
        /// <summary>
        /// 从行字典安全取值；若 key 不存在、值为 null/DBNull/空白字符串则返回 null，
        /// 以便调用方通过 ?? 链回退到备选列名。
        /// </summary>
        private static string? GetString(Dictionary<string, object>? row, string key)
        {
            if (row == null) return null;
            if (!row.TryGetValue(key, out var v)) return null;
            if (v == null || v == DBNull.Value) return null;
            var s = v.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        /// <summary>
        /// 解析代码表字段；空文本返回 ""（合法，A 侧模板允许空）；非空但未命中返回 null + 记录失败。
        /// </summary>
        private static string? ResolveParam(
            string? text,
            Dictionary<string, string> table,
            int rowNum,
            string fieldLabel,
            ExcelImportResult result)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            if (table.TryGetValue(text, out var id)) return id;
            result.Fail(rowNum, $"{fieldLabel}【{text}】在系统中找不到");
            return null;
        }

        /// <summary>
        /// 解析公私字段：B 侧 CRM_Customer.isPrivate 语义 1=公客 / 0=私客。
        /// 兼容 "公客" / "私客" / "1" / "0" 四种形式。
        /// </summary>
        private static int? ParseIsPrivate(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 1; // 默认公客
            var t = text.Trim();
            if (t == "公客" || t == "1" || t.Equals("1", StringComparison.OrdinalIgnoreCase)) return 1;
            if (t == "私客" || t == "0" || t.Equals("0", StringComparison.OrdinalIgnoreCase)) return 0;
            // 无法识别时按公客处理
            return 1;
        }

        /// <summary>
        /// 解析联系人性别：1=男、2=女、其他/空 = 0（未知）。
        /// 兼容 "男" / "女" / "1" / "2"。
        /// </summary>
        private static int? ParseSex(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var t = text.Trim();
            if (t == "男" || t == "1") return 1;
            if (t == "女" || t == "2") return 2;
            return 0;
        }
    }
}
