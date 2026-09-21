using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XHD.Core.Common.CDKEY
{
    /// <summary>
    /// CDKEY 生成/校验默认实现（Sprint 9 新增，#144/#145）。
    /// 算法：MD5(machineCode + Salt) → 32 hex → 前 15 位分组 → <c>XHDRC-XXXXX-XXXXX-XXXXX</c>。
    /// 幂等、无状态、无 IO；测试使用同一 seed 可稳定复现。
    /// </summary>
    public class CDKEYHelper : ICDKEYHelper
    {
        private const string Salt = "XHD@2026!";
        private const string CdkeyPrefix = "XHDRC";
        private const string MachinePrefix = "XMK";
        private readonly ILogger<CDKEYHelper> _logger;

        public CDKEYHelper(ILogger<CDKEYHelper> logger = null)
        {
            _logger = logger;
        }

        public Task<string> GenerateAsync(string machineCode)
        {
            if (string.IsNullOrWhiteSpace(machineCode))
            {
                throw new ArgumentException("machineCode 不能为 null 或空白", nameof(machineCode));
            }
            string code = BuildCdkey(machineCode);
            return Task.FromResult(code);
        }

        public Task<bool> VerifyAsync(string machineCode, string cdkey)
        {
            if (string.IsNullOrWhiteSpace(machineCode) || string.IsNullOrWhiteSpace(cdkey))
            {
                return Task.FromResult(false);
            }
            string expected = BuildCdkey(machineCode);
            return Task.FromResult(string.Equals(expected, cdkey, StringComparison.Ordinal));
        }

        public Task<string> GetMachineCodeAsync(string seed)
        {
            string s = seed ?? string.Empty;
            // MD5(seed) 生成 32 hex；取前 2 + 后续 15 hex 拼装
            // 输出格式：XMK-XX-XXXXX-XXXXX-XXXXX（1 prefix + 2 头 + 3 组 5 = 2 + 15 = 17 位 body）
            string hex = HashMd5(s);
            int bodyLen = 2 + 5 + 5 + 5;
            if (hex.Length < bodyLen)
            {
                hex = hex.PadRight(bodyLen, '0');
            }
            string body = hex.Substring(0, bodyLen);
            string machineCode = $"{MachinePrefix}-{body.Substring(0, 2)}-{body.Substring(2, 5)}-{body.Substring(7, 5)}-{body.Substring(12, 5)}";
            return Task.FromResult(machineCode);
        }

        /// <summary>
        /// 生成 <c>XHDRC-XXXXX-XXXXX-XXXXX</c> 格式注册码。
        /// </summary>
        private static string BuildCdkey(string machineCode)
        {
            string hash = HashMd5(machineCode + Salt);
            // 取 15 位 hex 分 3 组，每组 5 位
            string body = hash.Substring(0, 15).ToUpperInvariant();
            return $"{CdkeyPrefix}-{body.Substring(0, 5)}-{body.Substring(5, 5)}-{body.Substring(10, 5)}";
        }

        private static string HashMd5(string input)
        {
            using var md5 = MD5.Create();
            byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
