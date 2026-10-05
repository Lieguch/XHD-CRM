using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.Services;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// 缺口 D：版本号展示 + 在线版本检查。
    /// 纯函数部分（VersionHelper）零依赖直接断言 A 版四段加权口径；
    /// CheckUpdate action 走真实 SQLite 库 + 真实 Sys_infoService（Sys_infoRepository），
    /// HttpClient 由本地 HttpMessageHandler 桩替换（IHttpClientFactory.CreateClient 返回桩客户端），
    /// 全程无真实网络请求。
    /// </summary>
    public class VersionCheckTests : IDisposable
    {
        private readonly IFreeSql _fsql;

        public VersionCheckTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
        }

        public void Dispose() => _fsql?.Dispose();

        // ============ 测试数据工厂 ============

        private Task InsertSysInfo(string key, string value)
            => _fsql.Insert(new Sys_info { sys_key = key, sys_value = value }).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        private static IConfiguration EmptyConfig()
        {
            // 未配置 VersionCheck:Url：Moq 对未 Setup 的索引器返回 null
            return new Mock<IConfiguration>().Object;
        }

        private static IConfiguration ConfigWithUrl(string url)
        {
            var cfg = new Mock<IConfiguration>();
            cfg.Setup(c => c["VersionCheck:Url"]).Returns(url);
            return cfg.Object;
        }

        /// <summary>
        /// 本地 HttpMessageHandler 桩：按 responder 造响应，并记录请求供断言 query 参数。
        /// </summary>
        private sealed class StubMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            public StubMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
            {
                _responder = responder;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return await _responder(request);
            }
        }

        private static (IHttpClientFactory factory, StubMessageHandler handler) FactoryWith(
            Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            var handler = new StubMessageHandler(responder);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler, disposeHandler: false));
            return (factory.Object, handler);
        }

        private static (IHttpClientFactory factory, StubMessageHandler handler) FactoryWithJson(string json)
        {
            return FactoryWith(req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            }));
        }

        private SysInfoController CreateController(IConfiguration? config = null, IHttpClientFactory? factory = null)
        {
            // 真实 Service + 真实 Repository（SQLite in-memory），与 SysBaseUploadSMSTests 同款装配
            var infoService = new Sys_infoService(new Sys_infoRepository(_fsql));
            var ctrl = new SysInfoController(
                new Mock<ILogger<SysLogController>>().Object,
                infoService,
                new Mock<ISMSHelper>().Object,
                config ?? EmptyConfig(),
                factory ?? new Mock<IHttpClientFactory>().Object);

            var httpCtx = new DefaultHttpContext();
            httpCtx.Connection.RemoteIpAddress = IPAddress.Loopback;
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, "TEST_USER"),
                new Claim(ClaimTypes.Name, "Test User")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return ctrl;
        }

        // =========================================================
        // 纯函数：VersionWeight（A 版 Sys_version.aspx:59-81 口径）
        // =========================================================

        [Fact]
        public void VersionWeight_SeedVersionValue_MatchesAVersionAlgorithm()
        {
            // SeedData.cs:799 sys_version = v3.0.20250920.0
            // a=[3,0,20250920,0]，a[3]=0 < 10000 → 0
            // 0 + 20250920*1e5 + 0*1e7 + 3*1e9 = 2028092000000
            Assert.Equal(2028092000000L, VersionHelper.VersionWeight("v3.0.20250920.0"));
        }

        [Fact]
        public void VersionWeight_VPrefixStripped()
        {
            // A 版：toLowerCase().replace('v','')
            Assert.Equal(VersionHelper.VersionWeight("v3.0.20250920.0"), VersionHelper.VersionWeight("3.0.20250920.0"));
            Assert.Equal(VersionHelper.VersionWeight("V1.2.3.4"), VersionHelper.VersionWeight("1.2.3.4"));
        }

        [Fact]
        public void VersionWeight_FourthSegmentUnder10000_MultipliedByTen()
        {
            // A 版：if (a[3]*1 < 10000) a[3] = a[3]*10
            Assert.Equal(99990L, VersionHelper.VersionWeight("v1.0.0.9999"));
            Assert.Equal(10000L, VersionHelper.VersionWeight("v1.0.0.10000"));
            Assert.Equal(10000L, VersionHelper.VersionWeight("v1.0.0.1000"));
        }

        [Fact]
        public void VersionWeight_FourthSegmentBoundary_SmallerNumberWeighsMore()
        {
            // A 版口径的已知特性：9999 经 ×10 后（99990）反而大于 10000（未 ×10）。
            // 这是回归锚点：换用任何「更合理」的比较口径都会改变此断言。
            Assert.True(VersionHelper.VersionWeight("v1.0.0.9999") > VersionHelper.VersionWeight("v1.0.0.10000"));
        }

        [Fact]
        public void VersionWeight_WeightedSegments_OrderedBySignificance()
        {
            // a[0] 权重 1e9 > a[1] 权重 1e7 > a[2] 权重 1e5 > a[3]
            Assert.True(VersionHelper.VersionWeight("v2.0.0.0") > VersionHelper.VersionWeight("v1.9.999999.99999"));
            Assert.True(VersionHelper.VersionWeight("v1.2.0.0") > VersionHelper.VersionWeight("v1.1.999999.99999"));
            Assert.True(VersionHelper.VersionWeight("v1.0.5.0") > VersionHelper.VersionWeight("v1.0.4.99999"));
        }

        [Fact]
        public void VersionWeight_ExtraSegments_IgnoredLikeAVersion()
        {
            // A 版只取 a[0]~a[3]，多段忽略
            Assert.Equal(VersionHelper.VersionWeight("v1.2.3.4"), VersionHelper.VersionWeight("v1.2.3.4.5.6"));
        }

        [Fact]
        public void VersionWeight_LessThanFourSegments_ReturnsNull()
        {
            Assert.Null(VersionHelper.VersionWeight("v1.2.3"));
            Assert.Null(VersionHelper.VersionWeight("1"));
            Assert.Null(VersionHelper.VersionWeight(""));
            Assert.Null(VersionHelper.VersionWeight(null));
            Assert.Null(VersionHelper.VersionWeight("   "));
        }

        [Fact]
        public void VersionWeight_NonNumericSegment_ReturnsNull()
        {
            Assert.Null(VersionHelper.VersionWeight("v1.2.abc.4"));
            Assert.Null(VersionHelper.VersionWeight("vx.0.0.0"));
            Assert.Null(VersionHelper.VersionWeight("v1.0.0.0-beta"));
        }

        // =========================================================
        // 纯函数：HasUpdate（降级语义）
        // =========================================================

        [Fact]
        public void HasUpdate_SeedVersionVsItself_NoUpdate()
        {
            // 真实形态：本地与远程同为种子版本号
            Assert.False(VersionHelper.HasUpdate("v3.0.20250920.0", "v3.0.20250920.0"));
        }

        [Fact]
        public void HasUpdate_RemoteNewerDate_HasUpdate()
        {
            Assert.True(VersionHelper.HasUpdate("v3.0.20250920.0", "v3.0.20250921.0"));
        }

        [Fact]
        public void HasUpdate_RemoteOlder_NoUpdate()
        {
            Assert.False(VersionHelper.HasUpdate("v3.0.20250921.0", "v3.0.20250920.0"));
        }

        [Fact]
        public void HasUpdate_RemoteNewerMajorVersion_HasUpdate()
        {
            Assert.True(VersionHelper.HasUpdate("v3.0.20250920.0", "v4.0.0.0"));
        }

        [Fact]
        public void HasUpdate_CurrentUnparseable_TreatedAsMustUpdate()
        {
            // 降级：本地版本无法解析 → 保守视为必须更新
            Assert.True(VersionHelper.HasUpdate("abc", "v3.0.20250920.0"));
            Assert.True(VersionHelper.HasUpdate("", "v3.0.20250920.0"));
        }

        [Fact]
        public void HasUpdate_LatestUnparseable_TreatedAsNoUpdate()
        {
            // 降级：远程版本无法解析 → 无法比较，不打扰用户
            Assert.False(VersionHelper.HasUpdate("v3.0.20250920.0", "abc"));
            Assert.False(VersionHelper.HasUpdate("v3.0.20250920.0", ""));
        }

        [Fact]
        public void HasUpdate_BothUnparseable_NoUpdate()
        {
            Assert.False(VersionHelper.HasUpdate("abc", "def"));
        }

        // =========================================================
        // CheckUpdate action：版本号缺失分支
        // =========================================================

        [Fact]
        public async Task CheckUpdate_NoVersionRow_ReturnsError()
        {
            // 空库，无 sys_version 行
            var ctrl = CreateController(config: ConfigWithUrl("http://test-server/version"));

            var json = await ctrl.CheckUpdate();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("未读取到当前版本号", (string)obj["msg"]!);
        }

        [Fact]
        public async Task CheckUpdate_EmptyVersionValue_ReturnsError()
        {
            await InsertSysInfo("sys_version", "  ");

            var ctrl = CreateController(config: ConfigWithUrl("http://test-server/version"));

            var json = await ctrl.CheckUpdate();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("未读取到当前版本号", (string)obj["msg"]!);
        }

        // =========================================================
        // CheckUpdate action：Url 未配置分支
        // =========================================================

        [Fact]
        public async Task CheckUpdate_UrlNotConfigured_ReturnsError()
        {
            await InsertSysInfo("sys_version", "v3.0.20250920.0");

            var ctrl = CreateController(config: EmptyConfig());

            var json = await ctrl.CheckUpdate();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("未配置版本检查服务器（VersionCheck:Url）", (string)obj["msg"]!);
        }

        [Fact]
        public async Task CheckUpdate_UrlBlank_ReturnsError()
        {
            await InsertSysInfo("sys_version", "v3.0.20250920.0");

            var ctrl = CreateController(config: ConfigWithUrl("   "));

            var json = await ctrl.CheckUpdate();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("未配置版本检查服务器（VersionCheck:Url）", (string)obj["msg"]!);
        }

        // =========================================================
        // CheckUpdate action：远程有更新
        // =========================================================

        [Fact]
        public async Task CheckUpdate_RemoteNewer_ReturnsHasUpdateTrue()
        {
            await InsertSysInfo("sys_version", "v3.0.20250920.0");
            await InsertSysInfo("sys_name", "小黄豆CRM");

            var (factory, handler) = FactoryWithJson("{\"version\":\"v3.0.20250921.0\"}");
            var ctrl = CreateController(config: ConfigWithUrl("http://test-server/version"), factory: factory);

            var json = await ctrl.CheckUpdate();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Single(obj["data"]!);
            Assert.Equal("v3.0.20250920.0", (string)obj["data"][0]["current"]!);
            Assert.Equal("v3.0.20250921.0", (string)obj["data"][0]["latest"]!);
            Assert.True((bool)obj["data"][0]["hasUpdate"]!);

            // A 版 checkup() 传 Action=getversion + T_name（企业名）
            Assert.Single(handler.Requests);
            var query = handler.Requests[0].RequestUri!.Query;
            Assert.Contains("Action=getversion", query);
            Assert.Contains("T_name=" + Uri.EscapeDataString("小黄豆CRM"), query);
        }

        // =========================================================
        // CheckUpdate action：远程无更新
        // =========================================================

        [Fact]
        public async Task CheckUpdate_RemoteSameVersion_ReturnsHasUpdateFalse()
        {
            await InsertSysInfo("sys_version", "v3.0.20250920.0");

            var (factory, _) = FactoryWithJson("{\"version\":\"v3.0.20250920.0\"}");
            var ctrl = CreateController(config: ConfigWithUrl("http://test-server/version"), factory: factory);

            var json = await ctrl.CheckUpdate();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.False((bool)obj["data"][0]["hasUpdate"]!);
        }

        [Fact]
        public async Task CheckUpdate_RemoteOlder_ReturnsHasUpdateFalse()
        {
            await InsertSysInfo("sys_version", "v3.0.20250921.0");

            var (factory, _) = FactoryWithJson("{\"version\":\"v3.0.20250920.0\"}");
            var ctrl = CreateController(config: ConfigWithUrl("http://test-server/version"), factory: factory);

            var json = await ctrl.CheckUpdate();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.False((bool)obj["data"][0]["hasUpdate"]!);
        }

        // =========================================================
        // CheckUpdate action：远程异常分支
        // =========================================================

        [Fact]
        public async Task CheckUpdate_RemoteNon200_ReturnsConnectionError()
        {
            await InsertSysInfo("sys_version", "v3.0.20250920.0");

            var (factory, _) = FactoryWith(req
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

            var ctrl = CreateController(config: ConfigWithUrl("http://test-server/version"), factory: factory);

            var json = await ctrl.CheckUpdate();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无法连接版本检查服务器", (string)obj["msg"]!);
        }

        [Fact]
        public async Task CheckUpdate_RemoteThrows_ReturnsConnectionError()
        {
            await InsertSysInfo("sys_version", "v3.0.20250920.0");

            var (factory, _) = FactoryWith(req
                => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));

            var ctrl = CreateController(config: ConfigWithUrl("http://test-server/version"), factory: factory);

            var json = await ctrl.CheckUpdate();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无法连接版本检查服务器", (string)obj["msg"]!);
        }

        // =========================================================
        // CheckUpdate action：远程返回格式异常分支
        // =========================================================

        [Fact]
        public async Task CheckUpdate_RemoteJsonWithoutVersionField_ReturnsFormatError()
        {
            await InsertSysInfo("sys_version", "v3.0.20250920.0");

            var (factory, _) = FactoryWithJson("{\"foo\":\"bar\"}");
            var ctrl = CreateController(config: ConfigWithUrl("http://test-server/version"), factory: factory);

            var json = await ctrl.CheckUpdate();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("版本检查服务器返回数据格式异常", (string)obj["msg"]!);
        }

        [Fact]
        public async Task CheckUpdate_RemoteNotJson_ReturnsFormatError()
        {
            await InsertSysInfo("sys_version", "v3.0.20250920.0");

            var (factory, _) = FactoryWithJson("not-json-at-all");
            var ctrl = CreateController(config: ConfigWithUrl("http://test-server/version"), factory: factory);

            var json = await ctrl.CheckUpdate();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("版本检查服务器返回数据格式异常", (string)obj["msg"]!);
        }
    }
}
