using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;

namespace XHD.Core.View.Authorization
{
    /// <summary>
    /// 仅系统管理员（<c>emp_id == "admin"</c>）可访问。
    /// Sprint 10.39 引入：替代散落在 4 个控制器里的
    /// <c>if (User.FindFirst(ClaimTypes.Sid).Value != "admin") return XHDResult.Error(...)</c> 手写判断。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ButtonAuthAttribute"/> 一样实现 <see cref="IAsyncAuthorizationFilter"/>，
    /// 先于 action 方法体执行，因此不存在“先做参数校验再判 admin”的顺序陷阱。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class AdminOnlyAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public const string DefaultMessage = "仅系统管理员可执行此操作";

        public string DenyMessage { get; set; } = DefaultMessage;

        /// <inheritdoc/>
        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Result != null) return Task.CompletedTask;

            var empId = context.HttpContext.User?.FindFirst(ClaimTypes.Sid)?.Value;

            // 无 Sid ⇒ 未登录，交由 [Authorize] 处理
            if (string.IsNullOrEmpty(empId)) return Task.CompletedTask;

            if (string.Equals(empId, "admin", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            context.Result = AuthDeny.Create(DenyMessage);
            return Task.CompletedTask;
        }
    }
}
