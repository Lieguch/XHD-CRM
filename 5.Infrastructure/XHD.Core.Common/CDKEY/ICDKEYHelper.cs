using System;
using System.Threading.Tasks;

namespace XHD.Core.Common.CDKEY
{
    /// <summary>
    /// CDKEY 生成/校验抽象接口（Sprint 9 新增，#144 ECBC_CDKEY.Generate / #145 ECBC_CDKEY.Verify）。
    /// A 侧 <c>Common/ECBC_CDKEY.cs</c> 类为**空壳**（<c>internal class ECBC_CDKEY { }</c>），
    /// 真实逻辑在 <c>SoftReg</c>（<c>getMNum</c>/<c>getRNum</c>，依赖 WMI/Registry 硬件信息）。
    /// B 侧不接入硬件绑定，改为基于 machineCode 的**确定性 MD5 派生注册码**，便于纯函数式测试。
    /// 约定：Generate(machineCode) 与 Verify(machineCode, cdkey) 对称；
    ///       同 machineCode → 同 cdkey（幂等）；不同 machineCode → 不同 cdkey。
    /// </summary>
    public interface ICDKEYHelper
    {
        /// <summary>
        /// 基于机器码生成 CDKEY。
        /// </summary>
        /// <param name="machineCode">机器码（客户端上报，如 GUID/序列号），不允许 null/空白</param>
        /// <returns>形如 <c>XHDRC-XXXXX-XXXXX-XXXXX</c> 的注册码；machineCode 为 null 时抛 <see cref="ArgumentException"/></returns>
        Task<string> GenerateAsync(string machineCode);

        /// <summary>
        /// 校验 CDKEY 是否匹配机器码。
        /// </summary>
        /// <param name="machineCode">机器码</param>
        /// <param name="cdkey">待校验的注册码</param>
        /// <returns>true 匹配；false 不匹配（含 null/空白/格式错误）</returns>
        Task<bool> VerifyAsync(string machineCode, string cdkey);

        /// <summary>
        /// 生成机器码（模拟 A 侧 SoftReg.getMNum：拼接硬盘卷标号 + MAC 后取前 24 位）。
        /// B 侧不查硬件，改用传入的 seed 字符串哈希后拼装，便于纯函数测试。
        /// </summary>
        /// <param name="seed">种子字符串（真实部署中来自硬件指纹）</param>
        /// <returns>形如 <c>XMK-XX-XXXXX-XXXXX-XXXXX-XXXXX</c> 的机器码</returns>
        Task<string> GetMachineCodeAsync(string seed);
    }
}
