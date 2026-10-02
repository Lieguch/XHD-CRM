using System;
using System.Security.Cryptography;
using System.Text;

namespace XHD.Core.Common.DEncrypt
{
    /// <summary>
    /// Sprint 10.38（P1-7）：密码哈希从**无盐 MD5**迁移到**加盐 PBKDF2-SHA256**。
    ///
    /// 为什么不能直接存 PBKDF2(明文)：
    ///     已部署的移动端 App（A 版 2018 协议）登录时发送的是 MD5(pwd) 的**大写十六进制**，
    ///     服务端无法拿到明文（<see cref="M:XHD.Core.View.Controllers.APIController.Login"/>）。
    ///     因此采用「规范密钥」折中：规范密钥 = MD5(明文).ToUpper()，
    ///     落库的是 PBKDF2(规范密钥, 随机盐)。这既保持移动端协议不变，
    ///     又消除了无盐 MD5 的两个真实风险：
    ///         1. 库泄露后彩虹表/字典攻击直接反推明文；
    ///         2. 相同密码的用户哈希值完全相同（关联攻击）。
    ///     每个用户独立随机盐 + 10 万次迭代，库泄露后破解成本提高数个数量级。
    ///
    /// 存量兼容：旧记录的 pwd 就是规范密钥本身（无盐 MD5），<see cref="Verify"/> 仍能识别；
    ///     <see cref="NeedsRehash"/> 返回 true 时由登录/改密路径**透明升级**，
    ///     无需停机迁移脚本，用户无感知。
    ///
    /// 存储格式：XHD-PBKDF2${迭代次数}${Base64(盐)}${Base64(哈希)}
    /// </summary>
    public static class PasswordHasher
    {
        private const string Prefix = "XHD-PBKDF2";
        private const int SaltBytes = 16;
        private const int HashBytes = 32;
        private const int Iterations = 100_000;
        private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

        /// <summary>
        /// 由明文密码得到规范密钥（兼容已部署移动端协议；Web/移动端两条链路在此收敛）。
        /// </summary>
        public static string CanonicalSecret(string plaintext)
        {
            return MD5Comm.MD5Hash(plaintext ?? string.Empty);
        }

        /// <summary>
        /// 用随机盐哈希规范密钥。
        /// </summary>
        public static string Hash(string secret)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(secret ?? string.Empty), salt, Algorithm, Iterations, HashBytes);
            return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// 校验密钥。<paramref name="stored"/> 为新格式时走 PBKDF2 定时比较；
        /// 为旧格式（无盐 MD5，即规范密钥本身）时直接比较，命中后由调用方触发升级。
        /// </summary>
        public static bool Verify(string stored, string secret)
        {
            if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(secret))
            {
                return false;
            }

            if (stored.StartsWith(Prefix + "$", StringComparison.Ordinal))
            {
                string[] parts = stored.Split('$');
                if (parts.Length != 4)
                {
                    return false;
                }
                if (!int.TryParse(parts[1], out int iterations) || iterations < 1)
                {
                    return false;
                }
                byte[] salt;
                byte[] expected;
                try
                {
                    salt = Convert.FromBase64String(parts[2]);
                    expected = Convert.FromBase64String(parts[3]);
                }
                catch (FormatException)
                {
                    return false;
                }
                if (expected.Length == 0)
                {
                    return false;
                }
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(secret), salt, Algorithm, iterations, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }

            // 旧格式：无盐 MD5（大写 32 位十六进制）== 规范密钥
            return string.Equals(stored, secret, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 是否仍是旧格式（无盐 MD5），需要在不阻塞业务时升级为 PBKDF2。
        /// </summary>
        public static bool NeedsRehash(string stored)
        {
            return !string.IsNullOrEmpty(stored) && !stored.StartsWith(Prefix + "$", StringComparison.Ordinal);
        }
    }
}
