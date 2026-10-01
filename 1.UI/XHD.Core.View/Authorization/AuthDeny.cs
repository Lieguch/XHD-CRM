using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using XHD.Core.Common;

namespace XHD.Core.View.Authorization
{
    /// <summary>
    /// 授权拒绝响应的唯一构造点。
    /// </summary>
    /// <remarks>
    /// 背景：本仓库大量 action 以 <c>Task&lt;string&gt;</c> 返回
    /// <c>XHDResult.Error(msg).ToString()</c>。
    /// <c>XHDResult.Error</c> 返回 <see cref="Newtonsoft.Json.Linq.JObject"/>，
    /// 其 <see cref="Newtonsoft.Json.Linq.JObject.ToString()"/> 产生的**就是裸 JSON 文本**
    /// （Newtonsoft 不会像 <c>System.Text.Json</c> 那样把字符串再包一层引号）。
    /// 本项目的 MVC 未注册 <c>AddNewtonsoftJson()</c> / 自定义 formatter（见 Startup.cs:60
    /// 仅有 <c>AddControllersWithViews()</c>），因此线上字节即该 JSON 文本本身，
    /// jQuery 侧 <c>$.ajax({ dataType: "json" })</c>（Views/Customer/Index.cshtml:395）
    /// 显式声明 dataType 后会**强制按 JSON 解析**，与 Content-Type 无关。
    ///
    /// 因此拒绝响应只需：<c>ContentResult</c> + <c>application/json</c> +
    /// <c>XHDResult.Error(msg).ToString()</c>。
    ///
    /// 用 200 而非 403 的原因：既有前端契约是
    /// <c>success: function(res) { if (res.code == 0) {...} else layer.alert(res.msg) }</c>
    /// （Index.cshtml:396-402）。返回 4xx 会走 <c>error</c> 回调，弹出
    /// “操作失败！！！403” 而非业务消息，与全仓 61 处 <c>res.code</c> 判断不一致。
    /// </remarks>
    public static class AuthDeny
    {
        public const string DefaultMessage = "无操作权限";

        public static ContentResult Create(string message = DefaultMessage)
        {
            return new ContentResult
            {
                Content = XHDResult.Error(message).ToString(),
                ContentType = "application/json; charset=utf-8",
                StatusCode = StatusCodes.Status200OK
            };
        }
    }
}
