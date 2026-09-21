using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;
using XHD.Core.Common.CDKEY;
using XHD.Core.Common.Mail;
using XHD.Core.Common.RSA;
using XHD.Core.Common.Cache;
using MimeKit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 9 单元测试（5 个 Common 工具类 + Controller 层校验）。
    /// 覆盖 #144 ECBC_CDKEY.Generate / #145 ECBC_CDKEY.Verify /
    ///      #147 MailSender.SendMail / #148 RSACryption.* / #158 DataCache.GetDataCache。
    /// 目标 25-30 用例。
    /// </summary>
    public class Sprint9CommonTests
    {
        #region ICDKEYHelper（#144 / #145）

        [Fact]
        public async Task Cdkey_GenerateAsync_NullMachineCode_ThrowsArgumentException()
        {
            var helper = new CDKEYHelper();
            await Assert.ThrowsAsync<ArgumentException>(() => helper.GenerateAsync(null));
        }

        [Fact]
        public async Task Cdkey_GenerateAsync_BlankMachineCode_ThrowsArgumentException()
        {
            var helper = new CDKEYHelper();
            await Assert.ThrowsAsync<ArgumentException>(() => helper.GenerateAsync("   "));
        }

        [Fact]
        public async Task Cdkey_GenerateAsync_ReturnsExpectedFormat()
        {
            var helper = new CDKEYHelper();
            string cdkey = await helper.GenerateAsync("TEST-MACHINE-001");
            // 格式：XHDRC-XXXXX-XXXXX-XXXXX
            Assert.StartsWith("XHDRC-", cdkey);
            string[] parts = cdkey.Split('-');
            Assert.Equal(4, parts.Length);
            Assert.Equal("XHDRC", parts[0]);
            Assert.Equal(5, parts[1].Length);
            Assert.Equal(5, parts[2].Length);
            Assert.Equal(5, parts[3].Length);
            // 全大写
            Assert.Equal(cdkey.ToUpperInvariant(), cdkey);
        }

        [Fact]
        public async Task Cdkey_GenerateAsync_SameMachineCode_IsDeterministic()
        {
            var helper = new CDKEYHelper();
            string c1 = await helper.GenerateAsync("MACHINE-A");
            string c2 = await helper.GenerateAsync("MACHINE-A");
            Assert.Equal(c1, c2);
        }

        [Fact]
        public async Task Cdkey_GenerateAsync_DifferentMachineCode_DifferentCdkey()
        {
            var helper = new CDKEYHelper();
            string c1 = await helper.GenerateAsync("MACHINE-A");
            string c2 = await helper.GenerateAsync("MACHINE-B");
            Assert.NotEqual(c1, c2);
        }

        [Fact]
        public async Task Cdkey_VerifyAsync_ValidCdkey_ReturnsTrue()
        {
            var helper = new CDKEYHelper();
            string machineCode = "VM-XYZ-12345";
            string cdkey = await helper.GenerateAsync(machineCode);
            bool valid = await helper.VerifyAsync(machineCode, cdkey);
            Assert.True(valid);
        }

        [Fact]
        public async Task Cdkey_VerifyAsync_WrongCdkey_ReturnsFalse()
        {
            var helper = new CDKEYHelper();
            bool valid = await helper.VerifyAsync("VM-XYZ-12345", "XHDRC-AAAAA-BBBBB-CCCCC");
            Assert.False(valid);
        }

        [Fact]
        public async Task Cdkey_VerifyAsync_NullInputs_ReturnsFalse()
        {
            var helper = new CDKEYHelper();
            Assert.False(await helper.VerifyAsync(null, "any"));
            Assert.False(await helper.VerifyAsync("machine", null));
            Assert.False(await helper.VerifyAsync(null, null));
        }

        [Fact]
        public async Task Cdkey_VerifyAsync_MalformedCdkey_ReturnsFalse()
        {
            var helper = new CDKEYHelper();
            Assert.False(await helper.VerifyAsync("VM-XYZ-12345", "not-a-cdkey"));
            Assert.False(await helper.VerifyAsync("VM-XYZ-12345", ""));
        }

        [Fact]
        public async Task Cdkey_GetMachineCodeAsync_ReturnsXmkFormat()
        {
            var helper = new CDKEYHelper();
            string machineCode = await helper.GetMachineCodeAsync("harddisk-serial-abc");
            Assert.StartsWith("XMK-", machineCode);
            string[] parts = machineCode.Split('-');
            // 格式：XMK-XX-XXXXX-XXXXX-XXXXX（1 prefix + 2 header + 3 body = 5 parts）
            Assert.Equal(5, parts.Length);
            Assert.Equal("XMK", parts[0]);
            Assert.Equal(2, parts[1].Length);
            Assert.Equal(5, parts[2].Length);
            Assert.Equal(5, parts[3].Length);
            Assert.Equal(5, parts[4].Length);
        }

        [Fact]
        public async Task Cdkey_GetMachineCodeAsync_SameSeed_IsDeterministic()
        {
            var helper = new CDKEYHelper();
            string m1 = await helper.GetMachineCodeAsync("seed-1");
            string m2 = await helper.GetMachineCodeAsync("seed-1");
            Assert.Equal(m1, m2);
        }

        #endregion

        #region IMailHelper（#147）

        /// <summary>
        /// Mock SmtpClientFactory：捕获邮件而不真正发信。
        /// </summary>
        private sealed class MockSmtpClientFactory : SmtpClientFactory
        {
            public List<MimeMessage> Sent { get; } = new List<MimeMessage>();
            public bool Throw { get; set; } = false;

            public ISmtpClient Create(string host, int port, bool useTls, string username, string password)
            {
                return new CapturingClient(this);
            }
        }

        private sealed class CapturingClient : ISmtpClient
        {
            private readonly MockSmtpClientFactory _parent;
            public CapturingClient(MockSmtpClientFactory parent) { _parent = parent; }
            public Task<bool> SendMailAsync(MimeMessage message)
            {
                if (_parent.Throw)
                {
                    return Task.FromResult(false);
                }
                _parent.Sent.Add(message);
                return Task.FromResult(true);
            }
            public void Disconnect() { }
        }

        [Fact]
        public async Task Mail_SendMailAsync_ValidRecipient_InvokesSmtpClient()
        {
            var factory = new MockSmtpClientFactory();
            var helper = new MailHelper("smtp.example.com", 587, true, "from@example.com", "user", "pass", smtpFactory: factory);
            bool ok = await helper.SendMailAsync("user@example.com", "Hello", "World");
            Assert.True(ok);
            Assert.Single(factory.Sent);
            Assert.Equal("Hello", factory.Sent[0].Subject);
        }

        [Fact]
        public async Task Mail_SendMailAsync_MultipleRecipients_AllAdded()
        {
            var factory = new MockSmtpClientFactory();
            var helper = new MailHelper("smtp.example.com", 587, true, "from@example.com", "user", "pass", smtpFactory: factory);
            await helper.SendMailAsync("a@example.com,b@example.com;c@example.com", "Hi", "Body");
            Assert.Single(factory.Sent);
            Assert.Equal(3, factory.Sent[0].To.Count);
        }

        [Fact]
        public async Task Mail_SendMailAsync_EmptyRecipient_ReturnsFalse_NoSend()
        {
            var factory = new MockSmtpClientFactory();
            var helper = new MailHelper("smtp.example.com", 587, true, "from@example.com", "user", "pass", smtpFactory: factory);
            bool ok = await helper.SendMailAsync("", "Subject", "Body");
            Assert.False(ok);
            Assert.Empty(factory.Sent);
        }

        [Fact]
        public async Task Mail_SendMailAsync_HtmlBody_SetsHtmlBody()
        {
            var factory = new MockSmtpClientFactory();
            var helper = new MailHelper("smtp.example.com", 587, true, "from@example.com", "user", "pass", smtpFactory: factory);
            await helper.SendMailAsync("a@example.com", "Hi", "<p>Hello</p>", isBodyHtml: true);
            Assert.Single(factory.Sent);
            Assert.Contains("<p>Hello</p>", factory.Sent[0].Body.ToString());
        }

        [Fact]
        public async Task Mail_SendMailAsync_SmtpClientFails_ReturnsFalse()
        {
            var factory = new MockSmtpClientFactory { Throw = true };
            var helper = new MailHelper("smtp.example.com", 587, true, "from@example.com", "user", "pass", smtpFactory: factory);
            bool ok = await helper.SendMailAsync("a@example.com", "Hi", "Body");
            Assert.False(ok);
        }

        [Fact]
        public void Mail_Ctor_NullHost_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                new MailHelper(null, 587, true, "from", "user", "pass"));
        }

        #endregion

        #region IRSACryptionHelper（#148）

        [Fact]
        public async Task Rsa_GenerateKeyPairAsync_ReturnsValidPair()
        {
            var helper = new RSACryptionHelper();
            var pair = await helper.GenerateKeyPairAsync(1024);
            Assert.NotNull(pair);
            Assert.Equal(1024, pair.KeySize);
            Assert.NotEmpty(pair.PrivateKey);
            Assert.NotEmpty(pair.PublicKey);
            Assert.StartsWith("<RSAKeyValue>", pair.PrivateKey);
            Assert.StartsWith("<RSAKeyValue>", pair.PublicKey);
            // 私钥应比公钥长（含额外密钥材料）
            Assert.True(pair.PrivateKey.Length > pair.PublicKey.Length,
                $"PrivKey length should exceed PubKey length; actual {pair.PrivateKey.Length} vs {pair.PublicKey.Length}");
        }

        [Fact]
        public async Task Rsa_EncryptDecrypt_RoundTrip_ReturnsOriginal()
        {
            var helper = new RSACryptionHelper();
            var pair = await helper.GenerateKeyPairAsync(1024);
            string plain = "Hello, Sprint 9!";
            string cipher = await helper.EncryptAsync(pair.PublicKey, plain);
            Assert.NotEmpty(cipher);
            Assert.NotEqual(plain, cipher);
            string decrypted = await helper.DecryptAsync(pair.PrivateKey, cipher);
            Assert.Equal(plain, decrypted);
        }

        [Fact]
        public async Task Rsa_EncryptDecrypt_ChineseText_RoundTrip()
        {
            var helper = new RSACryptionHelper();
            var pair = await helper.GenerateKeyPairAsync(1024);
            string plain = "小黄豆 CRM Sprint 9 测试";
            string cipher = await helper.EncryptAsync(pair.PublicKey, plain);
            string decrypted = await helper.DecryptAsync(pair.PrivateKey, cipher);
            Assert.Equal(plain, decrypted);
        }

        [Fact]
        public async Task Rsa_EncryptAsync_NullPublicKey_ReturnsEmpty()
        {
            var helper = new RSACryptionHelper();
            string cipher = await helper.EncryptAsync(null, "plain");
            Assert.Empty(cipher);
        }

        [Fact]
        public async Task Rsa_EncryptAsync_MalformedPublicKey_ReturnsEmpty()
        {
            var helper = new RSACryptionHelper();
            string cipher = await helper.EncryptAsync("not-xml", "plain");
            Assert.Empty(cipher);
        }

        [Fact]
        public async Task Rsa_DecryptAsync_WrongPrivateKey_ReturnsEmpty()
        {
            var helper = new RSACryptionHelper();
            var pairA = await helper.GenerateKeyPairAsync(1024);
            var pairB = await helper.GenerateKeyPairAsync(1024);
            string cipher = await helper.EncryptAsync(pairA.PublicKey, "secret");
            string decrypted = await helper.DecryptAsync(pairB.PrivateKey, cipher);
            Assert.Empty(decrypted);
        }

        [Fact]
        public async Task Rsa_DecryptAsync_NullCipher_ReturnsEmpty()
        {
            var helper = new RSACryptionHelper();
            var pair = await helper.GenerateKeyPairAsync(1024);
            string decrypted = await helper.DecryptAsync(pair.PrivateKey, null);
            Assert.Empty(decrypted);
        }

        [Fact]
        public async Task Rsa_SaveKeyAsync_WritesJsonFile()
        {
            var helper = new RSACryptionHelper();
            var pair = await helper.GenerateKeyPairAsync(1024);
            string tmpDir = Path.Combine(Path.GetTempPath(), "xhd-sprint9-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            string filePath = Path.Combine(tmpDir, "rsa-key.json");
            try
            {
                int written = await helper.SaveKeyAsync(pair, filePath);
                Assert.True(written > 0);
                Assert.True(File.Exists(filePath));
                string content = File.ReadAllText(filePath);
                Assert.Contains("privateKey", content);
                Assert.Contains("publicKey", content);
                Assert.Contains("keySize", content);
                // 内容应包含实际私钥片段
                Assert.Contains(pair.PrivateKey.Substring(0, 30), content);
            }
            finally
            {
                try { Directory.Delete(tmpDir, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task Rsa_SaveKeyAsync_NullPath_ReturnsZero()
        {
            var helper = new RSACryptionHelper();
            var pair = await helper.GenerateKeyPairAsync(1024);
            int written = await helper.SaveKeyAsync(pair, null);
            Assert.Equal(0, written);
        }

        #endregion

        #region IDataCacheHelper（#158）

        [Fact]
        public void Cache_SetGet_StringValue_RoundTrip()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var helper = new DataCacheHelper(cache);
            helper.SetCache("k1", "v1");
            object value = helper.GetCache("k1");
            Assert.Equal("v1", value);
        }

        [Fact]
        public void Cache_Get_NonexistentKey_ReturnsNull()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var helper = new DataCacheHelper(cache);
            Assert.Null(helper.GetCache("missing"));
        }

        [Fact]
        public void Cache_Get_EmptyKey_ReturnsNull()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var helper = new DataCacheHelper(cache);
            Assert.Null(helper.GetCache(""));
            Assert.Null(helper.GetCache(null));
            Assert.Null(helper.GetCache("   "));
        }

        [Fact]
        public void Cache_SetGet_ComplexObject_RoundTrip()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var helper = new DataCacheHelper(cache);
            var payload = new Dictionary<string, object>
            {
                { "id", 42 },
                { "name", "xhdcrm" },
                { "active", true }
            };
            helper.SetCache("complex", payload);
            var result = helper.GetCache("complex") as Dictionary<string, object>;
            Assert.NotNull(result);
            Assert.Equal(42, result["id"]);
            Assert.Equal("xhdcrm", result["name"]);
            Assert.Equal(true, result["active"]);
        }

        [Fact]
        public async Task Cache_SetCacheAsync_WithExpiration_RespectsSlidingExpiration()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var helper = new DataCacheHelper(cache);
            await helper.SetCacheAsync("exp1", "value", slidingExpiration: TimeSpan.FromDays(1));
            Assert.Equal("value", helper.GetCache("exp1"));
        }

        [Fact]
        public void Cache_SetCacheAsync_NullKey_NoException()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var helper = new DataCacheHelper(cache);
            // 不应抛异常
            helper.SetCache(null, "v");
            helper.SetCache("", "v");
            Assert.Null(helper.GetCache(null));
        }

        [Fact]
        public void Cache_RemoveCache_RemovesExistingKey()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var helper = new DataCacheHelper(cache);
            helper.SetCache("to-remove", "v");
            bool removed = helper.RemoveCache("to-remove");
            Assert.True(removed);
            Assert.Null(helper.GetCache("to-remove"));
        }

        [Fact]
        public void Cache_RemoveCache_NonexistentKey_ReturnsFalse()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var helper = new DataCacheHelper(cache);
            Assert.False(helper.RemoveCache("never-set"));
        }

        [Fact]
        public void Cache_GetAllKeys_ReturnsAllActiveKeys()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var helper = new DataCacheHelper(cache);
            helper.SetCache("a", 1);
            helper.SetCache("b", 2);
            helper.SetCache("c", 3);
            var keys = helper.GetAllKeys();
            Assert.Equal(3, keys.Count);
            Assert.Contains("a", keys);
            Assert.Contains("b", keys);
            Assert.Contains("c", keys);
        }

        [Fact]
        public async Task Cache_GetCacheAsync_ReturnsSameAsGetCache()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var helper = new DataCacheHelper(cache);
            helper.SetCache("async-k", "async-v");
            object value = await helper.GetCacheAsync("async-k");
            Assert.Equal("async-v", value);
        }

        #endregion

        #region SystemController 层集成校验（辅助 DTO 与端点路由）

        [Fact]
        public void Controller_RequestDtos_ExposeRequiredProperties()
        {
            var g = new XHD.Core.View.Controllers.GenerateCDKeyRequest { MachineCode = "m" };
            Assert.Equal("m", g.MachineCode);

            var v = new XHD.Core.View.Controllers.VerifyCDKeyRequest { MachineCode = "m", Cdkey = "c" };
            Assert.Equal("m", v.MachineCode);
            Assert.Equal("c", v.Cdkey);

            var s = new XHD.Core.View.Controllers.SendMailRequest
            {
                Recipient = "a@b.com",
                Subject = "s",
                Body = "b",
                IsBodyHtml = true
            };
            Assert.Equal("a@b.com", s.Recipient);
            Assert.Equal("s", s.Subject);
            Assert.Equal("b", s.Body);
            Assert.True(s.IsBodyHtml);

            var e = new XHD.Core.View.Controllers.RsaEncryptRequest { PublicKey = "pk", PlainText = "p" };
            Assert.Equal("pk", e.PublicKey);
            Assert.Equal("p", e.PlainText);

            var d = new XHD.Core.View.Controllers.RsaDecryptRequest { PrivateKey = "sk", Cipher = "c" };
            Assert.Equal("sk", d.PrivateKey);
            Assert.Equal("c", d.Cipher);
        }

        [Fact]
        public async Task Cdkey_EndToEnd_MachineCodeFlow_GeneratesMachineCodeThenCdkey()
        {
            // 端到端：seed → machineCode → cdkey → verify
            var helper = new CDKEYHelper();
            string machineCode = await helper.GetMachineCodeAsync("cpu-serial-abc-mac-00:11:22");
            string cdkey = await helper.GenerateAsync(machineCode);
            bool valid = await helper.VerifyAsync(machineCode, cdkey);
            Assert.True(valid);

            // 换一个 seed，cdkey 不同
            string machineCode2 = await helper.GetMachineCodeAsync("cpu-serial-xyz-mac-aa:bb:cc");
            string cdkey2 = await helper.GenerateAsync(machineCode2);
            Assert.NotEqual(cdkey, cdkey2);
            Assert.False(await helper.VerifyAsync(machineCode, cdkey2));
        }

        [Fact]
        public async Task Rsa_MultipleEncrypts_AreNonDeterministic()
        {
            // RSA 加密同一明文应产生不同密文（内含随机 padding）
            var helper = new RSACryptionHelper();
            var pair = await helper.GenerateKeyPairAsync(1024);
            string cipher1 = await helper.EncryptAsync(pair.PublicKey, "same plaintext");
            string cipher2 = await helper.EncryptAsync(pair.PublicKey, "same plaintext");
            Assert.NotEqual(cipher1, cipher2);
            // 但都能正确解密
            string d1 = await helper.DecryptAsync(pair.PrivateKey, cipher1);
            string d2 = await helper.DecryptAsync(pair.PrivateKey, cipher2);
            Assert.Equal("same plaintext", d1);
            Assert.Equal("same plaintext", d2);
        }

        #endregion
    }
}
