using System.Collections.Generic;
using System.Threading.Tasks;

namespace XHD.Core.Common.RSA
{
    /// <summary>
    /// RSA 加解密/密钥生成/保存抽象接口（Sprint 9 新增，#148 RSACryption）。
    /// A 侧 <c>Common/DEncrypt/RSACryption.cs</c> 有 17 个方法；B 侧 <c>5.Infrastructure/.../DEncrypt/RSACryption.cs</c>
    /// 已 1:1 迁移同名方法，但需要 Controller 端点暴露。
    /// 本接口只暴露 Web 端最常用的 4 个方法（生成密钥对 / 加密 / 解密 / 保存密钥）；
    /// 签名/验签/Hash 等低频方法由 B 侧 <c>RSACryption</c> 类直接访问，不在此抽象层。
    /// </summary>
    public interface IRSACryptionHelper
    {
        /// <summary>
        /// 生成 RSA 密钥对（私钥 + 公钥）。
        /// </summary>
        /// <param name="keySize">密钥长度（1024/2048/4096），默认 2048</param>
        /// <returns>密钥对</returns>
        Task<RsaKeyPair> GenerateKeyPairAsync(int keySize = 2048);

        /// <summary>
        /// 使用公钥 XML 加密明文。
        /// </summary>
        /// <param name="xmlPublicKey">公钥 XML</param>
        /// <param name="plainText">明文</param>
        /// <returns>Base64 密文；公钥为空或格式错误时返回空字符串</returns>
        Task<string> EncryptAsync(string xmlPublicKey, string plainText);

        /// <summary>
        /// 使用私钥 XML 解密密文。
        /// </summary>
        /// <param name="xmlPrivateKey">私钥 XML</param>
        /// <param name="cipherBase64">Base64 密文</param>
        /// <returns>明文；私钥为空/密文格式错误/解密失败时返回空字符串</returns>
        Task<string> DecryptAsync(string xmlPrivateKey, string cipherBase64);

        /// <summary>
        /// 保存密钥对到 JSON 文件（用于持久化）。
        /// </summary>
        /// <param name="keyPair">密钥对</param>
        /// <param name="filePath">目标文件绝对路径</param>
        /// <returns>写入的字节数</returns>
        Task<int> SaveKeyAsync(RsaKeyPair keyPair, string filePath);
    }

    /// <summary>
    /// RSA 密钥对数据模型。
    /// </summary>
    public class RsaKeyPair
    {
        /// <summary>
        /// 密钥长度（bit）
        /// </summary>
        public int KeySize { get; set; } = 2048;

        /// <summary>
        /// 私钥 XML（含私钥 + 公钥）
        /// </summary>
        public string PrivateKey { get; set; } = string.Empty;

        /// <summary>
        /// 公钥 XML
        /// </summary>
        public string PublicKey { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"RSA({KeySize}) private={(string.IsNullOrEmpty(PrivateKey) ? "empty" : $"{PrivateKey.Length} chars")} public={(string.IsNullOrEmpty(PublicKey) ? "empty" : $"{PublicKey.Length} chars")}";
        }
    }
}
