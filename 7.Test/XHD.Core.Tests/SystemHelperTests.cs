using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using Moq;
using MimeKit;
using Newtonsoft.Json.Linq;
using Xunit;

using XHD.Core.Common;
using XHD.Core.Common.Cache;
using XHD.Core.Common.CDKEY;
using XHD.Core.Common.Mail;
using XHD.Core.Common.RSA;
using XHD.Core.View.Controllers;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.38 系统基座线（二）：SystemController 的 RSA / CDKEY / Cache / Mail 端点真测试。
    /// 与 Sprint9CommonTests 的区别：前者覆盖 5 个 Helper 的单元行为，
    /// 本文件覆盖 **Controller 层**——全部注入真实 Helper 实例（RSACryptionHelper /
    /// CDKEYHelper / DataCacheHelper / MailHelper），验证端点装配、参数校验分支与
    /// 加解密配对、CDKEY 生成即验证、缓存两态等根因级行为。
    /// </summary>
    public class SystemHelperTests : IDisposable
    {
        private readonly IFreeSql _fsql;

        public SystemHelperTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 装配辅助 ============

        /// <summary>
        /// 不发真邮件的 SMTP 桩：捕获 MimeMessage，仅用于覆盖「发送成功」分支。
        /// 生产侧的 SMTP 抽象（SmtpClientFactory）本就是为此类注入设计的。
        /// </summary>
        private sealed class CapturingSmtpFactory : SmtpClientFactory
        {
            public List<MimeMessage> Sent { get; } = new List<MimeMessage>();

            public ISmtpClient Create(string host, int port, bool useTls, string username, string password)
            {
                return new CapturingClient(this);
            }
        }

        private sealed class CapturingClient : ISmtpClient
        {
            private readonly CapturingSmtpFactory _parent;

            public CapturingClient(CapturingSmtpFactory parent)
            {
                _parent = parent;
            }

            public Task<bool> SendMailAsync(MimeMessage message)
            {
                _parent.Sent.Add(message);
                return Task.FromResult(true);
            }

            public void Disconnect()
            {
                // 桩无需真实断连
            }
        }

        /// <summary>
        /// 构造注入真实 Helper 的 SystemController。
        /// </summary>
        /// <param name="cache">可传入已预置数据的真实 DataCacheHelper；默认新建空缓存</param>
        /// <param name="smtpFactory">SMTP 工厂；null 表示用真实默认工厂（连接不可达主机必然失败）</param>
        private SystemController CreateController(DataCacheHelper cache = null, SmtpClientFactory smtpFactory = null)
        {
            cache = cache ?? new DataCacheHelper(new MemoryCache(new MemoryCacheOptions()));

            var mail = smtpFactory != null
                ? new MailHelper("smtp.example.com", 587, true, "from@example.com", "user", "pass", smtpFactory: smtpFactory)
                // 真实默认 SMTP 工厂 + 不可达主机：连接被拒，SendMailAsync 内部捕获并返回 false
                : new MailHelper("127.0.0.1", 1, false, "from@example.com", "user", "pass");

            return TestControllerHelper.CreateWithHttpContext<SystemController>(
                string.Empty, "TEST_USER", "Test User",
                new Mock<ILogger<SystemController>>().Object,
                new CDKEYHelper(),
                mail,
                new RSACryptionHelper(),
                cache);
        }

        private static JObject Ok(string json)
        {
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
            return obj;
        }

        private static JObject Fail(string json)
        {
            var obj = JObject.Parse(json);
            Assert.Equal(-1, (int)obj["code"]!);
            return obj;
        }

        // ============ RSA 密钥对生成 ============

        [Fact]
        public async Task GenerateRsaKeyPair_DefaultKeySize_ReturnsXmlKeyPair()
        {
            var ctrl = CreateController();

            var data = Ok(await ctrl.GenerateRsaKeyPair());

            Assert.Equal(2048, (int)data["data"]![0]!["keySize"]!);
            Assert.StartsWith("<RSAKeyValue>", (string)data["data"]![0]!["publicKey"]!);
            Assert.StartsWith("<RSAKeyValue>", (string)data["data"]![0]!["privateKey"]!);
            // 私钥含额外密钥材料，必然长于公钥
            Assert.True(((string)data["data"]![0]!["privateKey"]!).Length > ((string)data["data"]![0]!["publicKey"]!).Length);
        }

        [Fact]
        public async Task GenerateRsaKeyPair_OutOfRangeKeySize_ClampedTo2048()
        {
            // Helper 内部把 <512 或 >65536 的 keySize 兜底为 2048，Controller 透传结果
            var ctrl = CreateController();

            var data = Ok(await ctrl.GenerateRsaKeyPair(keySize: 64));

            Assert.Equal(2048, (int)data["data"]![0]!["keySize"]!);
        }

        // ============ RSA 加解密往返（根因级：必须配对） ============

        [Theory]
        [InlineData("Hello, XHD CRM!")]
        [InlineData("小黄豆 CRM Sprint 10.38")]
        [InlineData("0123456789")]
        public async Task RsaEncryptDecrypt_RoundTrip_ReturnsOriginalPlain(string plain)
        {
            var ctrl = CreateController();

            var keyPair = Ok(await ctrl.GenerateRsaKeyPair(1024))["data"]![0]!;
            string publicKey = (string)keyPair["publicKey"]!;
            string privateKey = (string)keyPair["privateKey"]!;

            var encData = Ok(await ctrl.RsaEncrypt(new RsaEncryptRequest { PublicKey = publicKey, PlainText = plain }));
            string cipher = (string)encData["data"]![0]!["cipher"]!;
            Assert.NotEqual(plain, cipher);

            var decData = Ok(await ctrl.RsaDecrypt(new RsaDecryptRequest { PrivateKey = privateKey, Cipher = cipher }));

            Assert.Equal(plain, (string)decData["data"]![0]!["plainText"]!);
        }

        [Fact]
        public async Task RsaEncrypt_EmptyPublicKey_ReturnsValidationError()
        {
            var ctrl = CreateController();

            var obj = Fail(await ctrl.RsaEncrypt(new RsaEncryptRequest { PublicKey = "", PlainText = "x" }));

            Assert.Equal("publicKey 和 plainText 不能为空", (string)obj["msg"]!);
        }

        [Fact]
        public async Task RsaEncrypt_MalformedPublicKey_ReturnsEncryptFailed()
        {
            // Helper 对非法公钥吞异常返回空串，Controller 转为「加密失败」
            var ctrl = CreateController();

            var obj = Fail(await ctrl.RsaEncrypt(new RsaEncryptRequest { PublicKey = "not-xml", PlainText = "x" }));

            Assert.Equal("加密失败", (string)obj["msg"]!);
        }

        [Fact]
        public async Task RsaDecrypt_WrongPrivateKey_ReturnsDecryptFailed()
        {
            // 密文来自密钥对 A，用密钥对 B 的私钥解密必然失败
            var ctrl = CreateController();

            var pairA = Ok(await ctrl.GenerateRsaKeyPair(1024))["data"]![0]!;
            var pairB = Ok(await ctrl.GenerateRsaKeyPair(1024))["data"]![0]!;
            string cipher = (string)Ok(await ctrl.RsaEncrypt(
                new RsaEncryptRequest { PublicKey = (string)pairA["publicKey"]!, PlainText = "secret" }))["data"]![0]!["cipher"]!;

            var obj = Fail(await ctrl.RsaDecrypt(new RsaDecryptRequest
            {
                PrivateKey = (string)pairB["privateKey"]!,
                Cipher = cipher
            }));

            Assert.Equal("解密失败", (string)obj["msg"]!);
        }

        // ============ CDKEY 生成即验证 ============

        [Fact]
        public async Task GenerateCDKey_ValidMachineCode_ReturnsFormattedCdkey()
        {
            var ctrl = CreateController();

            var data = Ok(await ctrl.GenerateCDKey(new GenerateCDKeyRequest { MachineCode = "VM-XYZ-12345" }));

            Assert.Equal("VM-XYZ-12345", (string)data["data"]![0]!["machineCode"]!);
            string cdkey = (string)data["data"]![0]!["cdkey"]!;
            Assert.StartsWith("XHDRC-", cdkey);
            Assert.Equal(4, cdkey.Split('-').Length);
        }

        [Fact]
        public async Task GenerateCDKey_EmptyMachineCode_ReturnsError()
        {
            var ctrl = CreateController();

            var obj = Fail(await ctrl.GenerateCDKey(new GenerateCDKeyRequest { MachineCode = "  " }));

            Assert.Equal("machineCode 不能为空", (string)obj["msg"]!);
        }

        [Fact]
        public async Task GenerateCDKey_NullRequest_ReturnsError()
        {
            var ctrl = CreateController();

            var obj = Fail(await ctrl.GenerateCDKey(null!));

            Assert.Equal("machineCode 不能为空", (string)obj["msg"]!);
        }

        [Fact]
        public async Task VerifyCDKey_GeneratedCdkey_ReturnsValid()
        {
            // 生成即验证通过：同一 Helper 实例（同一盐值）的幂等性
            var ctrl = CreateController();
            const string MachineCode = "VM-XYZ-12345";

            string cdkey = (string)Ok(await ctrl.GenerateCDKey(new GenerateCDKeyRequest { MachineCode = MachineCode }))["data"]![0]!["cdkey"]!;

            var data = Ok(await ctrl.VerifyCDKey(new VerifyCDKeyRequest { MachineCode = MachineCode, Cdkey = cdkey }));

            Assert.True((bool)data["data"]![0]!["valid"]!);
            Assert.Equal("验证通过", (string)data["data"]![0]!["message"]!);
        }

        [Fact]
        public async Task VerifyCDKey_IllegalCdkey_ReturnsInvalid()
        {
            var ctrl = CreateController();

            var obj = Fail(await ctrl.VerifyCDKey(new VerifyCDKeyRequest
            {
                MachineCode = "VM-XYZ-12345",
                Cdkey = "XHDRC-AAAAA-BBBBB-CCCCC"
            }));

            Assert.False((bool)obj["data"]![0]!["valid"]!);
            Assert.Equal("验证失败", (string)obj["data"]![0]!["message"]!);
        }

        [Fact]
        public async Task VerifyCDKey_MissingFields_ReturnsError()
        {
            var ctrl = CreateController();

            var obj = Fail(await ctrl.VerifyCDKey(new VerifyCDKeyRequest { MachineCode = "VM-XYZ-12345", Cdkey = "" }));

            Assert.Equal("machineCode 和 cdkey 均不能为空", (string)obj["msg"]!);
        }

        // ============ 缓存读取两态 ============

        [Fact]
        public async Task CacheGet_ExistingKey_ReturnsFoundAndValue()
        {
            var cache = new DataCacheHelper(new MemoryCache(new MemoryCacheOptions()));
            cache.SetCache("test-cache-key", "test-cache-value");
            var ctrl = CreateController(cache);

            var data = Ok(await ctrl.CacheGet("test-cache-key"));

            Assert.True((bool)data["data"]![0]!["found"]!);
            Assert.Equal("test-cache-value", (string)data["data"]![0]!["value"]!);
        }

        [Fact]
        public async Task CacheGet_MissingKey_ReturnsNotFound()
        {
            var ctrl = CreateController();

            var data = Ok(await ctrl.CacheGet("no-such-key"));

            Assert.False((bool)data["data"]![0]!["found"]!);
            Assert.Equal(JTokenType.Null, data["data"]![0]!["value"]!.Type);
        }

        // ============ 邮件：参数校验 + 配置缺失/不可达分支（不发真邮件） ============

        [Fact]
        public async Task SendMail_MissingRecipient_ReturnsError()
        {
            var ctrl = CreateController();

            var obj = Fail(await ctrl.SendMail(new SendMailRequest { Recipient = "", Subject = "S", Body = "B" }));

            Assert.Equal("recipient 和 subject 不能为空", (string)obj["msg"]!);
        }

        [Fact]
        public async Task SendMail_MissingSubject_ReturnsError()
        {
            var ctrl = CreateController();

            var obj = Fail(await ctrl.SendMail(new SendMailRequest { Recipient = "a@example.com", Subject = "" }));

            Assert.Equal("recipient 和 subject 不能为空", (string)obj["msg"]!);
        }

        [Fact]
        public async Task SendMail_NullRequest_ReturnsError()
        {
            var ctrl = CreateController();

            var obj = Fail(await ctrl.SendMail(null!));

            Assert.Equal("recipient 和 subject 不能为空", (string)obj["msg"]!);
        }

        [Fact]
        public async Task SendMail_UnreachableSmtpHost_ReturnsSendFailed()
        {
            // 真实 MailHelper + 真实默认 SmtpClientFactory：127.0.0.1:1 必然连接失败，
            // Helper 内部捕获后返回 false → Controller 报「邮件发送失败」。整条链路无 Mock。
            var ctrl = CreateController();

            var obj = Fail(await ctrl.SendMail(new SendMailRequest
            {
                Recipient = "a@example.com",
                Subject = "CI 测试邮件",
                Body = "不应真正发出"
            }));

            Assert.Equal("邮件发送失败", (string)obj["msg"]!);
        }

        [Fact]
        public async Task SendMail_ValidRequest_ReturnsSuccess()
        {
            // 覆盖成功分支：网络边界用捕获桩，消息构造（收件人/主题/正文）全走真实 Helper
            var factory = new CapturingSmtpFactory();
            var ctrl = CreateController(smtpFactory: factory);

            var data = Ok(await ctrl.SendMail(new SendMailRequest
            {
                Recipient = "a@example.com",
                Subject = "主题",
                Body = "<p>正文</p>",
                IsBodyHtml = true
            }));

            Assert.Equal("邮件发送成功", (string)data["msg"]!);
            Assert.Single(factory.Sent);
            Assert.Equal("主题", factory.Sent[0].Subject);
            Assert.Single(factory.Sent[0].To);
        }
    }
}
