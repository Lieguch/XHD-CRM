using System;
using System.Security.Cryptography;
using System.Text;

namespace XHD.Core.Common.DEncrypt
{
    /// <summary>
    ///     Encrypt 的摘要说明。
    ///     Copyright (C) XHD
    /// </summary>
    public class DEncrypt
    {
        #region 使用 缺省密钥字符串 加密/解密string

        /// <summary>
        ///     使用缺省密钥字符串加密string
        /// </summary>
        /// <param name="original">明文</param>
        /// <returns>密文</returns>
        public static string Encrypt(string original)
        {
            // Sprint 10.36: 硬编码密钥 "XHD" 不安全，建议通过配置系统注入密钥
            return Encrypt(original, "XHD");
        }

        /// <summary>
        ///     使用缺省密钥字符串解密string
        /// </summary>
        /// <param name="original">密文</param>
        /// <returns>明文</returns>
        public static string Decrypt(string original)
        {
            // Sprint 10.36: 硬编码密钥 "XHD" 不安全，建议通过配置系统注入密钥
            return Decrypt(original, "XHD", Encoding.Default);
        }

        #endregion

        #region 使用 给定密钥字符串 加密/解密string

        /// <summary>
        ///     使用给定密钥字符串加密string
        /// </summary>
        /// <param name="original">原始文字</param>
        /// <param name="key">密钥</param>
        /// <param name="encoding">字符编码方案</param>
        /// <returns>密文</returns>
        public static string Encrypt(string original, string key)
        {
            byte[] buff = Encoding.Default.GetBytes(original);
            byte[] kb = Encoding.Default.GetBytes(key);
            return Convert.ToBase64String(Encrypt(buff, kb));
        }

        /// <summary>
        ///     使用给定密钥字符串解密string
        /// </summary>
        /// <param name="original">密文</param>
        /// <param name="key">密钥</param>
        /// <returns>明文</returns>
        public static string Decrypt(string original, string key)
        {
            return Decrypt(original, key, Encoding.Default);
        }

        /// <summary>
        ///     使用给定密钥字符串解密string,返回指定编码方式明文
        /// </summary>
        /// <param name="encrypted">密文</param>
        /// <param name="key">密钥</param>
        /// <param name="encoding">字符编码方案</param>
        /// <returns>明文</returns>
        public static string Decrypt(string encrypted, string key, Encoding encoding)
        {
            byte[] buff = Convert.FromBase64String(encrypted);
            byte[] kb = Encoding.Default.GetBytes(key);
            return encoding.GetString(Decrypt(buff, kb));
        }

        #endregion

        #region 使用 缺省密钥字符串 加密/解密/byte[]

        /// <summary>
        ///     使用缺省密钥字符串解密byte[]
        /// </summary>
        /// <param name="encrypted">密文</param>
        /// <param name="key">密钥</param>
        /// <returns>明文</returns>
        public static byte[] Decrypt(byte[] encrypted)
        {
            // Sprint 10.36: 硬编码密钥 "XHD" 不安全
            byte[] key = Encoding.Default.GetBytes("XHD");
            return Decrypt(encrypted, key);
        }

        /// <summary>
        ///     使用缺省密钥字符串加密
        /// </summary>
        /// <param name="original">原始数据</param>
        /// <param name="key">密钥</param>
        /// <returns>密文</returns>
        public static byte[] Encrypt(byte[] original)
        {
            // Sprint 10.36: 硬编码密钥 "XHD" 不安全
            byte[] key = Encoding.Default.GetBytes("XHD");
            return Encrypt(original, key);
        }

        #endregion

        #region  使用 给定密钥 加密/解密/byte[]

        /// <summary>
        ///     生成密钥派生摘要（Sprint 10.36: MD5 → SHA256）
        /// </summary>
        /// <param name="original">数据源</param>
        /// <returns>摘要（32字节 SHA256，取前24字节用于 TripleDES 密钥）</returns>
        public static byte[] MakeKeyDerivation(byte[] original)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] fullHash = sha256.ComputeHash(original);
                byte[] key = new byte[24];
                Array.Copy(fullHash, 0, key, 0, 24);
                return key;
            }
        }


        /// <summary>
        ///     使用给定密钥加密
        /// </summary>
        /// <param name="original">明文</param>
        /// <param name="key">密钥</param>
        /// <returns>密文</returns>
        public static byte[] Encrypt(byte[] original, byte[] key)
        {
            // Sprint 10.30: SYSLIB0021 — TripleDESCryptoServiceProvider → TripleDES.Create(); Sprint 10.36: ECB → CBC
            using (var des = TripleDES.Create())
            {
                des.Key = MakeKeyDerivation(key);
                des.Mode = CipherMode.CBC;
                // Sprint 10.36: IV derived from key
                des.IV = new byte[8];
                Array.Copy(MakeKeyDerivation(key), 24, des.IV, 0, 8);
                using (var enc = des.CreateEncryptor())
                {
                    return enc.TransformFinalBlock(original, 0, original.Length);
                }
            }
        }

        /// <summary>
        ///     使用给定密钥解密数据
        /// </summary>
        /// <param name="encrypted">密文</param>
        /// <param name="key">密钥</param>
        /// <returns>明文</returns>
        public static byte[] Decrypt(byte[] encrypted, byte[] key)
        {
            // Sprint 10.30: SYSLIB0021 — TripleDESCryptoServiceProvider → TripleDES.Create(); Sprint 10.36: ECB → CBC
            using (var des = TripleDES.Create())
            {
                des.Key = MakeKeyDerivation(key);
                des.Mode = CipherMode.CBC;
                // Sprint 10.36: IV derived from key
                des.IV = new byte[8];
                Array.Copy(MakeKeyDerivation(key), 24, des.IV, 0, 8);
                using (var dec = des.CreateDecryptor())
                {
                    return dec.TransformFinalBlock(encrypted, 0, encrypted.Length);
                }
            }
        }

        #endregion
    }
}