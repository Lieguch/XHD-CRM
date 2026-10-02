using XHD.Core.Common.DEncrypt;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.38 P1-7：密码哈希（无盐 MD5 → 加盐 PBKDF2）回归测试。
    /// 覆盖 4 条链路：哈希/校验往返、存量 MD5 兼容、透明升级判定、空值与格式异常。
    /// </summary>
    public class PasswordHasherTests
    {
        [Fact]
        public void Hash_Verify_Roundtrip()
        {
            string secret = PasswordHasher.CanonicalSecret("P@ssw0rd-正确密码");

            string hash = PasswordHasher.Hash(secret);

            Assert.NotEqual(secret, hash);
            Assert.True(PasswordHasher.Verify(hash, secret));
            Assert.False(PasswordHasher.NeedsRehash(hash));
        }

        [Fact]
        public void Hash_PerUserSalt_DiffersForSameSecret()
        {
            string secret = PasswordHasher.CanonicalSecret("123456");

            string a = PasswordHasher.Hash(secret);
            string b = PasswordHasher.Hash(secret);

            Assert.NotEqual(a, b); // 每个用户独立随机盐 ⇒ 相同密码不同哈希
            Assert.True(PasswordHasher.Verify(a, secret));
            Assert.True(PasswordHasher.Verify(b, secret));
        }

        [Fact]
        public void Verify_RejectsWrongSecret()
        {
            string hash = PasswordHasher.Hash(PasswordHasher.CanonicalSecret("right"));

            Assert.False(PasswordHasher.Verify(hash, PasswordHasher.CanonicalSecret("wrong")));
            // 定时比较：错误长度也不应抛异常
            Assert.False(PasswordHasher.Verify(hash, "garbage"));
        }

        [Fact]
        public void Verify_AcceptsLegacyUnsaltedMd5_AndFlagsRehash()
        {
            // 存量记录：pwd 就是规范密钥本身（无盐 MD5 大写十六进制）
            string legacy = PasswordHasher.CanonicalSecret("123456");
            Assert.Equal("E10ADC3949BA59ABBE56E057F20F883E", legacy);

            Assert.True(PasswordHasher.Verify(legacy, legacy));
            Assert.True(PasswordHasher.NeedsRehash(legacy));

            // 升级后可继续校验且不再标记
            string upgraded = PasswordHasher.Hash(legacy);
            Assert.True(PasswordHasher.Verify(upgraded, legacy));
            Assert.False(PasswordHasher.NeedsRehash(upgraded));
        }

        [Fact]
        public void CanonicalSecret_MatchesOldMd5Behaviour()
        {
            Assert.Equal(MD5Comm.MD5Hash("abc"), PasswordHasher.CanonicalSecret("abc"));
        }

        [Theory]
        [InlineData("", "secret")]
        [InlineData("secret", "")]
        [InlineData("", "")]
        public void Verify_EmptyInputs_Rejected(string stored, string secret)
        {
            Assert.False(PasswordHasher.Verify(stored, secret));
        }

        [Fact]
        public void Verify_MalformedNewFormat_Rejected()
        {
            Assert.False(PasswordHasher.Verify("XHD-PBKDF2$abc$notbase64$!!", "secret"));
            Assert.False(PasswordHasher.Verify("XHD-PBKDF2$100000$", "secret"));
            Assert.False(PasswordHasher.Verify("XHD-PBKDF2$0$AA==$AA==", "secret"));
        }
    }
}
