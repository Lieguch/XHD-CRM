using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using XHD.Core.IServices;
using XHD.Core.View.Authorization;
using Xunit;

namespace XHD.Core.Tests.Authorization
{
    /// <summary>
    /// Sprint 10.39 声明式授权迁移专用的测试 harness。
    /// </summary>
    /// <remarks>
    /// <b>为什么需要它</b>：授权检查从「方法体内的内联 if」迁移到了
    /// <c>[ButtonAuth]</c> 的 <c>IAsyncAuthorizationFilter</c>。过滤器只在真正的 MVC
    /// 管道里执行，<b>单元测试直接调用控制器方法不会再触发授权</b>——这是迁移的预期行为，
    /// 不是缺陷。因此「无权限 ⇒ code=-1」这类断言必须改成驱动真实的过滤器，
    /// 否则等于放弃了这批 P0 授权修复的回归覆盖。
    /// <para>
    /// 本 harness 构造一个最小可用的 <see cref="AuthorizationFilterContext"/>
    /// （带 Sid claim + 注册了 <c>IDBAuthService</c> 的 RequestServices），
    /// 让 <see cref="ButtonAuthAttribute.OnAuthorizationAsync"/> 真实跑一遍。
    /// </para>
    /// </remarks>
    public static class AuthFilterTestHarness
    {
        /// <summary>
        /// 以用户 <paramref name="empId"/> 真实运行一次 <paramref name="attribute"/>。
        /// </summary>
        /// <returns>过滤器设置的 Result；未拒绝（放行）时为 <c>null</c>。</returns>
        public static async Task<ContentResult?> RunAuthFilterAsync(
            ButtonAuthAttribute attribute,
            string empId,
            IDBAuthService authService)
        {
            // ButtonAuthAttribute 通过 context.HttpContext.RequestServices 解析
            // IDBAuthService，必须提供一个挂载了该服务的 ServiceProvider。
            var services = new ServiceCollection()
                .AddSingleton(authService)
                .BuildServiceProvider(validateScopes: false);

            var user = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Sid, empId) }, "Test"));

            var httpCtx = new DefaultHttpContext { User = user };
            httpCtx.RequestServices = services;

            var actionCtx = new ActionContext(
                httpCtx,
                new RouteData(),
                new ActionDescriptor());

            var filterCtx = new AuthorizationFilterContext(
                actionCtx,
                new List<IFilterMetadata> { attribute });

            await attribute.OnAuthorizationAsync(filterCtx);
            return filterCtx.Result as ContentResult;
        }

        /// <summary>
        /// 断言拒绝结果命中 <c>code == -1</c> 且业务消息包含指定片段。
        /// </summary>
        public static void AssertDenied(ContentResult? result, string expectedMsgFragment)
        {
            Assert.NotNull(result);
            Assert.Equal("application/json; charset=utf-8", result!.ContentType);
            var json = JObject.Parse(result.Content);
            Assert.Equal(-1, json["code"]!.Value<int>());
            Assert.Contains(expectedMsgFragment, json["msg"]!.Value<string>());
        }
    }
}
