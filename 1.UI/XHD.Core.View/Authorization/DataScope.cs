using System.Collections.Generic;
using XHD.Core.Common;

namespace XHD.Core.View.Authorization
{
    /// <summary>
    /// 数据权限（authtype）语义的**唯一收口点**。
    /// </summary>
    /// <remarks>
    /// Sprint 10.39 引入。背景：既有 24 个控制器各自手写同一段逻辑：
    /// <code>
    /// var roledata = await _dBAuthService.GetDataAuth(empId);
    /// if (roledata.authtype != 4)   // 有的写 != 4，有的写 &lt; 4
    ///     expWhere = expWhere.And(a =&gt; roledata.empList.Contains(a.emp_id));
    /// </code>
    /// 审计实测存在三处不一致：
    /// <list type="bullet">
    /// <item>判等写法混乱：<c>!= 4</c> / <c>&lt; 4</c> / <c>= 0</c> 三种都有</item>
    /// <item>部分方法在 <c>authtype == 0</c> 时提前返回空集，部分继续查询得到空结果</item>
    /// <item>员工 id 列表为 null 时有的判空、有的直接 <c>Contains</c> 抛异常</item>
    /// </list>
    ///
    /// authtype 语义（<c>IDBAuthService.GetAuthType</c> 注释定义）：
    /// <list type="table">
    /// <item>0 = 无权限（empList 为空 ⇒ 查询必然为空集）</item>
    /// <item>1 = 仅本人</item>
    /// <item>2 = 本部门</item>
    /// <item>3 = 本部门及下级</item>
    /// <item>4 = 全公司（**不追加过滤**）</item>
    /// </list>
    ///
    /// <b>注意 admin 语义</b>：<see cref="XHDRoleData"/> 由
    /// <c>DBAuthService.GetDataAuth</c> 生成，admin 返回 <c>authtype=4 + 空 empList</c>。
    /// 因此本 helper 用 <c>authtype >= 4</c> 判定“不过滤”，**不能**用
    /// <c>empList.Count == 0 ⇒ 不过滤</c>（那会把 authtype=0 的无权限用户当成全权限，
    /// 造成越权）。这是既有代码里最容易埋雷的一点，特此在注释中固化。
    /// </remarks>
    public static class DataScope
    {
        /// <summary>全公司数据权限级别（不追加 empList 过滤）。</summary>
        public const int ScopeAll = 4;

        /// <summary>数据权限解析结果。</summary>
        public sealed class Resolution
        {
            /// <summary>是否需要追加 empList 过滤。</summary>
            public bool NeedsFilter { get; }

            /// <summary>过滤用的员工 id 列表（<see cref="NeedsFilter"/> 为 false 时为 null）。</summary>
            public IReadOnlyList<string> EmployeeIds { get; }

            /// <summary>原始 authtype 值。</summary>
            public int AuthType { get; }

            internal Resolution(bool needsFilter, IReadOnlyList<string> employeeIds, int authType)
            {
                NeedsFilter = needsFilter;
                EmployeeIds = employeeIds;
                AuthType = authType;
            }
        }

        /// <summary>
        /// 把 <see cref="XHDRoleData"/> 解析为「是否过滤 + 过滤用 id 列表」。
        /// </summary>
        /// <param name="roleData">可为 null（等价于 authtype=0，强制空集过滤）。</param>
        /// <returns>
        /// <list type="bullet">
        /// <item><c>authtype >= 4</c> → <c>NeedsFilter=false</c>（全公司，不追加条件）</item>
        /// <item>其他 → <c>NeedsFilter=true</c>，<c>EmployeeIds</c> 为 empList
        /// （null 安全地归一为空列表 ⇒ 查询结果为空集，而非 NRE）</item>
        /// </list>
        /// </returns>
        public static Resolution Resolve(XHDRoleData roleData)
        {
            if (roleData == null)
            {
                return new Resolution(true, EmptyList, 0);
            }

            int authType = roleData.authtype;

            if (authType >= ScopeAll)
            {
                // 全公司：不追加 empList 过滤。
                // admin 走的就是这一支（authtype=4 + empList 为空），
                // 但“空 empList”在此**不代表**全权限 —— 判定只依赖 authtype。
                return new Resolution(false, null, authType);
            }

            var ids = roleData.empList ?? (IReadOnlyList<string>)EmptyList;
            return new Resolution(true, ids, authType);
        }

        private static readonly IReadOnlyList<string> EmptyList = new List<string>().AsReadOnly();
    }
}
