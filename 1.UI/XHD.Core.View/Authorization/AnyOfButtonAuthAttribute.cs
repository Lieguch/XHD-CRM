using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using XHD.Core.IServices;

namespace XHD.Core.View.Authorization
{
    /// <summary>
    /// “满足任一权限即放行”的声明式授权属性（OR 语义）。
    /// </summary>
    /// <remarks>
    /// <h3>为什么需要它</h3>
    /// Sprint 10.39 全量盘点（见 <c>auth_inventory.md</c> 3.4.2）发现 5 个方法
    /// 的既有授权语义是**双按钮 OR**：
    /// <list type="bullet">
    /// <item><c>SysRoleEmpController.Add</c> → <c>sys_role|edit</c> OR <c>sys_role|emp_add</c></item>
    /// <item><c>SysRoleEmpController.Remove</c> → <c>sys_role|edit</c> OR <c>sys_role|emp_del</c></item>
    /// <item><c>HrPostController.Delete</c> → <c>hr_post|del</c> OR <c>hr_post|edit</c></item>
    /// <item><c>HrPostController.UpdatePost</c> → <c>hr_post|edit</c> OR <c>hr_employee|edit</c></item>
    /// </list>
    /// 语义含义：这些操作横跨两个模块，持有任一模块的编辑权即可执行（例如
    /// “岗位”既能被岗位模块改，也能被职务模块改）。
    ///
    /// <h3>为什么不能叠多个 [ButtonAuth]</h3>
    /// 堆叠多个 <see cref="ButtonAuthAttribute"/> 是 **AND** 语义：过滤器按序执行，
    /// 第一个拒绝就会写入 <see cref="AuthorizationFilterContext.Result"/>，
    /// 后续过滤器见到非空 Result 即让路。结果是“必须同时拥有两个权限”，
    /// 与既有的 OR 语义正好相反，会造成权限收紧事故。
    /// 因此 OR 必须用本属性显式表达，避免读者误以为叠层即 OR。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class AnyOfButtonAuthAttribute : Attribute, IAsyncAuthorizationFilter
    {
        /// <summary>任一通过的 auth_id 列表，例如 <c>"sys_role|edit", "sys_role|emp_add"</c>。</summary>
        public IReadOnlyList<string> AuthIds { get; }

        public string DenyMessage { get; set; } = AuthDeny.DefaultMessage;

        /// <param name="authIds">至少 2 个 auth_id；传 1 个请用 <see cref="ButtonAuthAttribute"/>。</param>
        public AnyOfButtonAuthAttribute(params string[] authIds)
        {
            if (authIds == null || authIds.Length < 2)
                throw new ArgumentException(
                    "AnyOfButtonAuth 至少需要 2 个 auth_id；单权限请使用 [ButtonAuth]。",
                    nameof(authIds));

            var cleaned = authIds
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (cleaned.Count < 2)
                throw new ArgumentException(
                    "AnyOfButtonAuth 去重后至少需要 2 个非空 auth_id。", nameof(authIds));

            AuthIds = cleaned.AsReadOnly();
        }

        /// <inheritdoc/>
        /// <remarks>短路求值：命中即放行，不做多余数据库查询。</remarks>
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Result != null) return;

            var empId = context.HttpContext.User?.FindFirst(ClaimTypes.Sid)?.Value;

            // 无 Sid ⇒ 未登录，交由 [Authorize] 处理
            if (string.IsNullOrEmpty(empId)) return;

            if (string.Equals(empId, "admin", StringComparison.OrdinalIgnoreCase)) return;

            var auth = context.HttpContext.RequestServices.GetRequiredService<IDBAuthService>();

            foreach (var id in AuthIds)
            {
                if (await auth.GetAuth(empId, id).ConfigureAwait(false))
                {
                    return; // OR 命中，放行
                }
            }

            context.Result = AuthDeny.Create(DenyMessage);
        }
    }
}
