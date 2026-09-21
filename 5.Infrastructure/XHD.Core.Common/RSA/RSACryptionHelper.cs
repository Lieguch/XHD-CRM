using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SystemSecurityRsa = System.Security.Cryptography.RSA;

namespace XHD.Core.Common.RSA
{
    /// <summary>
    /// RSA 加解密默认实现（Sprint 9 新增，#148）。
    /// 使用 .NET 8 内置 <see cref="SystemSecurityRsa"/> 抽象（跨平台），不依赖 Windows CAPI；
    /// 密钥以 PKCS#1 XML 格式导入/导出（兼容 B 侧已有 <c>RSACryption</c> 类）。
    /// </summary>
    public class RSACryptionHelper : IRSACryptionHelper
    {
        public Task<RsaKeyPair> GenerateKeyPairAsync(int keySize = 2048)
        {
            if (keySize < 512 || keySize > 65536)
            {
                keySize = 2048;
            }
            using var rsa = SystemSecurityRsa.Create(keySize);
            // .NET 8 RSA.Create 的 ToXmlString 参数行为可能因平台而异；
            // 保险起见：调用两次，比较长度，长者为私钥
            string xml1 = rsa.ToXmlString(true);
            string xml2 = rsa.ToXmlString(false);
            string privateKey = xml1.Length >= xml2.Length ? xml1 : xml2;
            string publicKey = xml1.Length >= xml2.Length ? xml2 : xml1;
            return Task.FromResult(new RsaKeyPair
            {
                KeySize = keySize,
                PrivateKey = privateKey,
                PublicKey = publicKey
            });
        }

        public Task<string> EncryptAsync(string xmlPublicKey, string plainText)
        {
            if (string.IsNullOrWhiteSpace(xmlPublicKey) || plainText == null)
            {
                return Task.FromResult(string.Empty);
            }
            try
            {
                using var rsa = SystemSecurityRsa.Create();
                rsa.FromXmlString(xmlPublicKey);
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] cipherBytes = rsa.Encrypt(plainBytes, System.Security.Cryptography.RSAEncryptionPadding.Pkcs1);
                return Task.FromResult(Convert.ToBase64String(cipherBytes));
            }
            catch (Exception)
            {
                return Task.FromResult(string.Empty);
            }
        }

        public Task<string> DecryptAsync(string xmlPrivateKey, string cipherBase64)
        {
            if (string.IsNullOrWhiteSpace(xmlPrivateKey) || string.IsNullOrWhiteSpace(cipherBase64))
            {
                return Task.FromResult(string.Empty);
            }
            try
            {
                using var rsa = SystemSecurityRsa.Create();
                rsa.FromXmlString(xmlPrivateKey);
                byte[] cipherBytes = Convert.FromBase64String(cipherBase64);
                byte[] plainBytes = rsa.Decrypt(cipherBytes, System.Security.Cryptography.RSAEncryptionPadding.Pkcs1);
                return Task.FromResult(Encoding.UTF8.GetString(plainBytes));
            }
            catch (Exception)
            {
                return Task.FromResult(string.Empty);
            }
        }

        public Task<int> SaveKeyAsync(RsaKeyPair keyPair, string filePath)
        {
            if (keyPair == null || string.IsNullOrWhiteSpace(filePath))
            {
                return Task.FromResult(0);
            }
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var payload = new JObject
                {
                    { "keySize", keyPair.KeySize },
                    { "privateKey", keyPair.PrivateKey },
                    { "publicKey", keyPair.PublicKey },
                    { "generatedAt", DateTime.UtcNow.ToString("o") }
                };
                string json = JsonConvert.SerializeObject(payload, Formatting.Indented);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                File.WriteAllBytes(filePath, bytes);
                return Task.FromResult(bytes.Length);
            }
            catch (Exception)
            {
                return Task.FromResult(0);
            }
        }
    }
}
