using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace XHD.Core.View.Authorization
{
    /// <summary>权限目录中的一个条目（由 <see cref="ButtonAuthAttribute"/> 属性字面量生成）。</summary>
    public sealed class AuthCatalogEntry
    {
        /// <summary>组合 auth_id，例如 <c>CRM_Customer|edit</c>。</summary>
        public string AuthId { get; }

        /// <summary>菜单段。</summary>
        public string Menu { get; }

        /// <summary>操作段。</summary>
        public string Operation { get; }

        /// <summary>声明处的控制器类型全名。</summary>
        public string Controller { get; }

        /// <summary>声明处的 action 方法名（类级属性时为 <c>&lt;class&gt;</c>）。</summary>
        public string Action { get; }

        public AuthCatalogEntry(string authId, string menu, string operation, string controller, string action)
        {
            AuthId = authId;
            Menu = menu;
            Operation = operation;
            Controller = controller;
            Action = action;
        }

        public override string ToString() => $"{Controller}.{Action}:{AuthId}";
    }

    /// <summary>启动期对账报告。</summary>
    public sealed class AuthReconcileReport
    {
        /// <summary>已声明但 <c>Sys_Button</c> 中缺失的按钮 —— 必须 upsert 补齐。</summary>
        public List<AuthCatalogEntry> Missing { get; } = new List<AuthCatalogEntry>();

        /// <summary><c>Sys_Button</c> 中存在但代码里已无人声明的孤儿按钮 —— 告警，不删除。</summary>
        public List<string> Orphans { get; } = new List<string>();

        /// <summary>代码里声明的全部 auth_id。</summary>
        public IReadOnlyList<AuthCatalogEntry> Declared { get; set; } = Array.Empty<AuthCatalogEntry>();
    }

    /// <summary>
    /// 权限目录的唯一真源（Single Source of Truth）。
    /// </summary>
    /// <remarks>
    /// Sprint 10.39 引入，用于根治「权限目录三处分裂」：
    /// <list type="bullet">
    /// <item>控制器魔法字符串（92 个字面量，大小写混乱：既有 <c>Sys_role</c> 也有 <c>sys_role</c>）</item>
    /// <item><c>ConfigData/SysButtons.json</c>（56 条，字段 schema 与 <c>Sys_Button</c> 实体不匹配）</item>
    /// <item><c>SeedData.cs</c> 硬编码（53 条，且 <c>sys_authority</c> 表 0 处引用）</item>
    /// </list>
    /// 三者互不同步 ⇒ 11 个 auth_id 缺失、43 个从未被引用。
    ///
    /// 本类的做法：**代码里的 <see cref="ButtonAuthAttribute"/> 字面量就是目录本身**。
    /// 通过反射枚举，不需要任何 JSON 或硬编码列表，天然与代码同步 ——
    /// 新增一个 <c>[ButtonAuth]</c>，目录自动多一条，不需要再改任何数据文件。
    /// </remarks>
    public static class AuthCatalog
    {
        private static readonly Lazy<IReadOnlyList<AuthCatalogEntry>> _self =
            new Lazy<IReadOnlyList<AuthCatalogEntry>>(
                () => Collect(Assembly.GetExecutingAssembly()));

        /// <summary>当前程序集内声明的全部权限条目（去重、按 AuthId 排序）。</summary>
        public static IReadOnlyList<AuthCatalogEntry> Current => _self.Value;

        /// <summary>
        /// 反射收集指定程序集中所有 <see cref="ButtonAuthAttribute"/> 声明。
        /// </summary>
        /// <remarks>
        /// 纯函数：只读反射，无 I/O，无副作用，可被单元测试直接调用
        /// （这也是 <see cref="AuthCatalogReconciler"/> 的可测性基础）。
        /// </remarks>
        public static IReadOnlyList<AuthCatalogEntry> Collect(Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                // 部分类型加载失败时仍尽量收集可用的
                types = ex.Types.Where(t => t != null).ToArray();
            }

            var entries = new List<AuthCatalogEntry>();

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract) continue;
                if (!typeof(ControllerBase).IsAssignableFrom(type)) continue;

                // 类级属性：对该控制器所有 action 生效
                foreach (var attr in GetButtonAuths(type, true))
                {
                    entries.Add(new AuthCatalogEntry(
                        attr.AuthId, attr.Menu, attr.Operation, type.FullName, "<class>"));
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    foreach (var attr in GetButtonAuths(method, true))
                    {
                        entries.Add(new AuthCatalogEntry(
                            attr.AuthId, attr.Menu, attr.Operation, type.FullName, method.Name));
                    }
                }
            }

            // 先按 (AuthId, Controller, Action) 全排序再 Distinct，保证「同一 auth_id
            // 在多处声明时」也产出完全确定的结果（测试断言依赖确定性）。
            return entries
                .OrderBy(e => e.AuthId, StringComparer.Ordinal)
                .ThenBy(e => e.Controller, StringComparer.Ordinal)
                .ThenBy(e => e.Action, StringComparer.Ordinal)
                .Distinct(AuthIdComparer.Instance)
                .ToList();
        }

        /// <summary>
        /// 把「代码声明集合」与「数据库中现有 Sys_Button 主键集合」求差集。
        /// </summary>
        /// <remarks>
        /// 纯函数，无任何 I/O —— 方便在单元测试里用假数据验证对账逻辑，
        /// 也方便 CI 做静态门禁（例如断言 Missing 为空）。
        /// </remarks>
        public static AuthReconcileReport Reconcile(
            IReadOnlyList<AuthCatalogEntry> declared,
            IEnumerable<string> existingButtonIds)
        {
            if (declared == null) throw new ArgumentNullException(nameof(declared));
            if (existingButtonIds == null) throw new ArgumentNullException(nameof(existingButtonIds));

            var report = new AuthReconcileReport { Declared = declared.ToList().AsReadOnly() };

            var existing = new HashSet<string>(existingButtonIds, StringComparer.Ordinal);

            foreach (var entry in declared)
            {
                if (!existing.Contains(entry.AuthId))
                {
                    report.Missing.Add(entry);
                }
            }

            // 孤儿：DB 里有、代码里没有。只告警不删除 —— 可能是尚未迁移的旧按钮，
            // 删除会瞬间打断存量角色绑定。
            var declaredSet = new HashSet<string>(declared.Select(d => d.AuthId), StringComparer.Ordinal);
            foreach (var existingId in existing)
            {
                if (!string.IsNullOrWhiteSpace(existingId) && !declaredSet.Contains(existingId))
                {
                    report.Orphans.Add(existingId);
                }
            }

            report.Orphans.Sort(StringComparer.Ordinal);
            return report;
        }

        /// <summary>按 <see cref="AuthCatalogEntry.AuthId"/> 做相等比较的 comparer。</summary>
        /// <remarks>
        /// Distinct 不接受 lambda（CS1660），必须给一个 IEqualityComparer&lt;T&gt;。
        /// 放这里而不是内联，是因为它同时是「同一 auth_id 多处声明时取哪一条」这一
        /// 行为的单一定义点。
        /// </remarks>
        private sealed class AuthIdComparer : IEqualityComparer<AuthCatalogEntry>
        {
            public static readonly AuthIdComparer Instance = new AuthIdComparer();

            public bool Equals(AuthCatalogEntry? x, AuthCatalogEntry? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null) return false;
                return string.Equals(x.AuthId, y.AuthId, StringComparison.Ordinal);
            }

            public int GetHashCode(AuthCatalogEntry obj)
            {
                return obj is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.AuthId);
            }
        }

        private static IEnumerable<ButtonAuthAttribute> GetButtonAuths(MemberInfo member, bool inherit)
        {
            // 三个授权属性都直接继承自 Attribute、互不派生，因此 GetCustomAttributes<T>
            // 只会精确命中 ButtonAuthAttribute 本身。CustomAttributeData 上没有
            // CreateInstance<T>()（CS1061），不能用它构造实例。
            return member.GetCustomAttributes<ButtonAuthAttribute>(inherit);
        }
    }
}
