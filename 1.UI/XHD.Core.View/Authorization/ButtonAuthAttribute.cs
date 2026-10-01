using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using XHD.Core.IServices;

namespace XHD.Core.View.Authorization
{
    /// <summary>
    /// 声明式按钮/菜单授权属性 —— Sprint 10.39 授权子系统根本性重构的核心。
    /// </summary>
    /// <remarks>
    /// <h3>被替代的旧模式</h3>
    /// 本仓库 36 个控制器中有 92 处以下手写样板（Sprint 10.38 审计实测）：
    /// <code>
    /// if (!await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "CRM_Customer|edit"))
    ///     return XHDResult.Error("无操作权限").ToString();
    /// </code>
    /// 审计发现三类结构性缺陷：
    /// <list type="number">
    /// <item><b>权限目录三处分裂</b>：控制器魔法字符串（92 个字面量）/
    /// <c>ConfigData/SysButtons.json</c>（56 条，字段名 <c>title/menu_id</c>，小写 <c>sys_role|add</c>）/
    /// <c>SeedData.cs</c> 硬编码（53 条）。三者互不同步，且 JSON 与控制器**大小写都不一致**
    /// （JSON 是 <c>sys_role</c>，控制器是 <c>Sys_role</c>），导致 11 个 auth_id 缺失、43 个未引用。</item>
    /// <item><b>顺序陷阱</b>：216 个方法在授权检查**之前**做参数校验。未授权用户可以
    /// 通过反复提交拿到参数 schema 信息，且用户看到“参数错误”而非“无权限”，排查困难。</item>
    /// <item><b>无 admin 之外的任何兜底</b>：<c>sys_authority</c>（角色↔按钮绑定表）在
    /// <c>SeedData.cs</c> 中 0 处引用 ⇒ 新建角色默认拿不到任何按钮权限，
    /// 必须靠人工在页面上逐条勾选，极易漏配。</item>
    /// </list>
    ///
    /// <h3>本属性如何根治</h3>
    /// <list type="number">
    /// <item><b>属性字面量 = auth_id 唯一真源</b>。不再存在第二份列表，
    /// 由 <see cref="AuthCatalog"/> 反射枚举。新增权限只需写一处属性。</item>
    /// <item><b>实现 <see cref="IAsyncAuthorizationFilter"/></b>，先于 action 方法体执行
    /// ⇒ 授权优先于参数校验，顺序陷阱在结构上不可能复现。</item>
    /// <item>配合 <see cref="AuthCatalogReconciler"/> 在**启动期**把缺失的按钮
    /// upsert 进 <c>Sys_Button</c> ⇒ 漂移从“事后审计发现”变为“启动即失败/即补齐”。</item>
    /// </list>
    ///
    /// <h3>使用</h3>
    /// <code>
    /// [ButtonAuth("CRM_Customer", "edit")]
    /// public async Task&lt;string&gt; Edit(CRM_Customer model) { ... }
    /// </code>
    ///
    /// <h3>与 [Authorize] 的分工</h3>
    /// <see cref="AuthorizeAttribute"/> 负责**认证**（cookie 有效性，所有控制器类级已挂），
    /// 本属性负责**授权**（按钮/菜单权限）。未登录时 <c>[Authorize]</c> 先跑并把用户
    /// 重定向到登录页，本属性拿到的一定是已登录用户，不重复处理未认证分支。
    /// 注意本属性**不**继承 <see cref="AuthorizeAttribute"/> —— 继承会让基类的
    /// 同步 <c>OnAuthorization</c> 与本属性的异步检查叠加执行，可能抢先发出
    /// 302/401 而破坏 <c>{"code":-1}</c> 的 JSON 契约。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ButtonAuthAttribute : Attribute, IAsyncAuthorizationFilter
    {
        /// <summary>菜单段（对应 <c>Sys_Menu.id</c>，例如 <c>CRM_Customer</c>）。</summary>
        public string Menu { get; }

        /// <summary>操作段（例如 <c>add</c> / <c>edit</c> / <c>del</c>）。</summary>
        public string Operation { get; }

        /// <summary>
        /// 组合后的 auth_id，与既有 <c>Menu|operation</c> 约定完全一致。
        /// 同时作为 <see cref="AuthCatalog"/> 的唯一真源键。
        /// </summary>
        public string AuthId { get; }

        /// <summary>拒绝时返回的业务消息，默认 <see cref="AuthDeny.DefaultMessage"/>。</summary>
        public string DenyMessage { get; set; } = AuthDeny.DefaultMessage;

        /// <param name="menu">菜单 id，例如 <c>"CRM_Customer"</c>。</param>
        /// <param name="operation">操作名，例如 <c>"edit"</c>。</param>
        public ButtonAuthAttribute(string menu, string operation)
        {
            Menu = RequireNonEmpty(menu, nameof(menu));
            Operation = RequireNonEmpty(operation, nameof(operation));
            AuthId = $"{menu}|{operation}";
        }

        /// <inheritdoc/>
        /// <remarks>
        /// 关键设计：
        /// <list type="bullet">
        /// <item><see cref="context.HttpContext.RequestServices"/> 解析 <see cref="IDBAuthService"/>
        /// 而非构造函数注入 —— 授权过滤器每个请求都会 new 一份属性实例，
        /// 用 DI 解析比静态持有更干净，也与 <c>IDBAuthService</c> 的 scoped 生命周期一致。</item>
        /// <item><see cref="admin"/> 绝对放行，沿用
        /// <c>DBAuthService.GetAuth</c> / <c>DBAuthRepository.GetAuth</c> 已有的双层短路语义
        /// （大小写不敏感）。</item>
        /// <item><see cref="context.Result"/> 已被前置过滤器（如 <c>[Authorize]</c>）设定时
        /// 直接让路，绝不覆盖。</item>
        /// </list>
        /// </remarks>
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Result != null) return;

            var empId = context.HttpContext.User?.FindFirst(ClaimTypes.Sid)?.Value;

            // 无 Sid claim ⇒ 未登录或登录态损坏。交给 [Authorize] 处理，这里不发 JSON。
            if (string.IsNullOrEmpty(empId)) return;

            var services = context.HttpContext.RequestServices;
            var auth = services.GetRequiredService<IDBAuthService>();

            if (string.Equals(empId, "admin", StringComparison.OrdinalIgnoreCase)) return;

            var allowed = await auth.GetAuth(empId, AuthId).ConfigureAwait(false);
            if (!allowed)
            {
                context.Result = AuthDeny.Create(DenyMessage);
            }
        }

        private static string RequireNonEmpty(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("auth_id 段不能为空。", paramName);
            return value.Trim();
        }
    }
}
