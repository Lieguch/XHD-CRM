using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace XHD.Core.View.Authorization
{
    /// <summary>
    /// 全局结果过滤器：把「返回 JSON 文本字符串」的 action 输出为真正的 <c>application/json</c>。
    /// </summary>
    /// <remarks>
    /// <h3>为什么需要它</h3>
    /// 本仓库绝大多数 action 以 <c>Task&lt;string&gt;</c> 返回
    /// <c>XHDResult.*(...).ToString()</c>。MVC 处理 <c>string</c> 返回值时走
    /// <see cref="StringResult"/>，默认 <c>ContentType</c> 为 <c>text/plain</c>。
    /// 也就是说：<b>线上 body 是正确的 JSON 文本，但 Content-Type 声明是 text/plain</b>。
    ///
    /// 现状之所以「能跑」，纯粹依赖前端显式写了
    /// <c>$.ajax({ dataType: "json" })</c>（如 <c>Views/Customer/Index.cshtml:395</c>）——
    /// jQuery 在 <c>dataType</c> 被显式指定时会**强制按 JSON 解析 body，忽略 Content-Type**。
    /// 这是一个隐蔽的隐式契约：
    /// <list type="bullet">
    /// <item>任何新增页面若忘写 <c>dataType</c>，就会拿到 <c>res.code === undefined</c>
    /// （因为 body 被当纯文本），进而走不进 <c>else { layer.alert(res.msg) }</c> 分支，
    /// 表现为「点了没反应」的假成功。</item>
    /// <item>任何非 jQuery 客户端（Postman、Python、curl 后接 jq）都会因 Content-Type 报错。</item>
    /// <item><see cref="ButtonAuthAttribute"/> / <see cref="AdminOnlyAttribute"/> /
    /// <see cref="AuthDeny"/> 的拒绝响应一律用 <c>application/json</c>。
    /// 不加本过滤器，同一接口会出现两种 Content-Type，契约不自洽。</item>
    /// </list>
    ///
    /// <h3>安全性</h3>
    /// 仅当 <c>ObjectResult.Value</c> 是字符串**且**首字符为 <c>{</c> 或 <c>[</c>
    /// （即明显是 JSON 字面量）时才改写。普通文本返回（如 <c>return "OK";</c>）
    /// 完全不受影响，<c>ViewResult</c> / <c>ContentResult</c> / <c>FileResult</c> 也全部不受影响。
    ///
    /// <h3>对单元测试无影响</h3>
    /// 本仓库测试直接调用控制器方法（<c>controller.Save(model)</c>），
    /// 不经过 MVC 管道，因此 <c>IResultFilter</c> 不会介入，既有断言全部保持不变。
    /// </remarks>
    public sealed class RawJsonStringResultFilter : IResultFilter
    {
        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context == null || context.Result is not ObjectResult obj) return;

            if (obj.Value is not string text) return;
            if (string.IsNullOrEmpty(text)) return;

            // 只对明显是 JSON 字面量的字符串生效，避免误伤普通文本返回。
            char first = text[0];
            if (first != '{' && first != '[') return;

            // 用 System.Text.Json 做一次廉价的结构校验，
            // 防止把「以 { 开头的非 JSON 字符串」错误标成 application/json。
            try { JsonSerializer.Deserialize<JsonElement>(text); }
            catch { return; }

            // ObjectResult 没有 ContentType 属性（内容类型由格式化器协商）。
            // 这里把结果换成 ContentResult：body 原样保留，只把 Content-Type 钉死成 JSON。
            context.Result = new ContentResult
            {
                Content = text,
                ContentType = "application/json; charset=utf-8",
                StatusCode = obj.StatusCode
            };
        }

        public void OnResultExecuted(ResultExecutedContext context)
        {
            // 无状态，不做事后处理。
        }
    }
}
