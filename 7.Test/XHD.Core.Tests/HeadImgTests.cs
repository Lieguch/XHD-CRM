using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using Newtonsoft.Json.Linq;
using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.38 员工头像上传（UploadController.HeadImg）单元测试。
    /// 走控制器管线，Moq 构造 IFormFile，断言真实落盘到 wwwroot/Upload/Header/。
    /// </summary>
    public class HeadImgTests
    {
        /// <summary>
        /// 用 Moq 构造一个 IFormFile，从 byte[] 提供 Stream（供 Controller 测试用）。
        /// </summary>
        private static IFormFile CreateFormFile(string fileName, byte[] bytes)
        {
            var mock = new Mock<IFormFile>();
            mock.Setup(f => f.FileName).Returns(fileName);
            mock.Setup(f => f.Length).Returns(bytes.Length);
            mock.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(bytes));
            // IFormFile.CopyToAsync(Stream, CancellationToken) 的 CancellationToken 是可选参数，
            // 表达式树不允许省略可选参数的调用（CS0854），故显式传入 CancellationToken 占位。
            mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns((Stream target, CancellationToken token) =>
                {
                    using (var ms = new MemoryStream(bytes))
                    {
                        return ms.CopyToAsync(target, token);
                    }
                });
            return mock.Object;
        }

        /// <summary>
        /// 构造 UploadController，依赖均走 Moq；
        /// files 为 null 时挂一个空表单（HeadImg 从 Request.Form.Files 取文件）。
        /// </summary>
        private static UploadController CreateUploadController(IFormFile file)
        {
            var ctrl = new UploadController(
                new Mock<ICRM_Customer_attaService>().Object,
                new Mock<ILogger<UploadController>>().Object);

            var httpCtx = new DefaultHttpContext();
            httpCtx.Connection.RemoteIpAddress = IPAddress.Loopback;
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, "TEST_USER"),
                new Claim(ClaimTypes.Name, "Test User")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

            var files = file == null
                ? new List<IFormFile>()
                : new List<IFormFile> { file };
            httpCtx.Request.Form = new FormCollection(
                new Dictionary<string, StringValues>(),
                files);

            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return ctrl;
        }

        /// <summary>
        /// HeadImg 落盘根目录（<cwd>/wwwroot/Upload/Header），测试后清理。
        /// </summary>
        private static string HeaderRoot => Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Upload", "Header");

        [Fact]
        public async Task HeadImg_NoFile_ReturnsError()
        {
            var ctrl = CreateUploadController(null);

            var json = await ctrl.HeadImg();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("未接收到文件", (string)obj["msg"]!);
        }

        [Theory]
        [InlineData("a.exe")]
        [InlineData("a.txt")]
        public async Task HeadImg_InvalidExtension_ReturnsError(string fileName)
        {
            var ctrl = CreateUploadController(CreateFormFile(fileName, new byte[] { 1, 2, 3 }));

            var json = await ctrl.HeadImg();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("仅支持", (string)obj["msg"]!);
        }

        [Fact]
        public async Task HeadImg_Oversize_ReturnsError()
        {
            // 构造 2MB + 1 字节的大文件
            var bytes = new byte[2 * 1024 * 1024 + 1];
            new Random(42).NextBytes(bytes);

            var ctrl = CreateUploadController(CreateFormFile("big.png", bytes));

            var json = await ctrl.HeadImg();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("2MB", (string)obj["msg"]!);
        }

        [Fact]
        public async Task HeadImg_ValidPng_SavesToWwwrootAndReturnsUrl()
        {
            var createdDir = Path.Combine(HeaderRoot, DateTime.Now.ToString("yyyy-MM-dd"));
            var createdFiles = new List<string>();
            try
            {
                var bytes = new byte[] { (byte)'P', (byte)'N', (byte)'G' };
                var ctrl = CreateUploadController(CreateFormFile("avatar.png", bytes));

                var json = await ctrl.HeadImg();
                var obj = JObject.Parse(json);

                Assert.Equal(0, (int)obj["code"]!);

                var data = obj["data"] as JArray;
                Assert.NotNull(data);
                Assert.Single(data);
                var url = (string)data[0]["url"]!;
                Assert.StartsWith("/Upload/Header/", url);
                Assert.EndsWith(".png", url);

                // 服务端返回的是相对 wwwroot 的 URL，落盘路径与之对应
                var savedPath = Path.Combine(
                    Directory.GetCurrentDirectory(), "wwwroot",
                    url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(savedPath));
                Assert.Equal("PNG", File.ReadAllText(savedPath));
                createdFiles.Add(savedPath);
            }
            finally
            {
                foreach (var f in createdFiles)
                {
                    if (File.Exists(f)) File.Delete(f);
                }
                // 清理本测试产生的日期目录（仅当为空时，避免误删其他测试产物）
                if (Directory.Exists(createdDir) &&
                    Directory.GetFiles(createdDir).Length == 0 &&
                    Directory.GetDirectories(createdDir).Length == 0)
                {
                    Directory.Delete(createdDir);
                }
            }
        }
    }
}
