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
            return Encrypt(original, "XHD");
        }

        /// <summary>
        ///     使用缺省密钥字符串解密string
        /// </summary>
        /// <param name="original">密文</param>
        /// <returns>明文</returns>
        public static string Decrypt(string original)
        {
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
            byte[] key = Encoding.Default.GetBytes("XHD");
            return Encrypt(original, key);
        }

        #endregion

        #region  使用 给定密钥 加密/解密/byte[]

        /// <summary>
        ///     生成MD5摘要
        /// </summary>
        /// <param name="original">数据源</param>
        /// <returns>摘要</returns>
        public static byte[] MakeMD5(byte[] original)
        {
            // Sprint 10.30: SYSLIB0021 — MD5CryptoServiceProvider → MD5.Create()
            using (var hashmd5 = MD5.Create())
            {
                return hashmd5.ComputeHash(original);
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
            // Sprint 10.30: SYSLIB0021 — TripleDESCryptoServiceProvider → TripleDES.Create()
            using (var des = TripleDES.Create())
            {
                des.Key = MakeMD5(key);
                des.Mode = CipherMode.ECB;
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
            // Sprint 10.30: SYSLIB0021 — TripleDESCryptoServiceProvider → TripleDES.Create()
            using (var des = TripleDES.Create())
            {
                des.Key = MakeMD5(key);
                des.Mode = CipherMode.ECB;
                using (var dec = des.CreateDecryptor())
                {
                    return dec.TransformFinalBlock(encrypted, 0, encrypted.Length);
                }
            }
        }

        #endregion
    }
}