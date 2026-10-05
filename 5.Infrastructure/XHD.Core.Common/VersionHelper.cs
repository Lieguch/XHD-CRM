using System;
using System.Globalization;

namespace XHD.Core.Common
{
    /// <summary>
    /// 版本号比较纯函数（零 IO，可单测）。
    /// 对应 A 版 View/System/sysinfo/Sys_version.aspx:59-81 checkup() 的客户端比较口径：
    /// 去 'v' 前缀、按 '.' 拆 4 段、第 4 段 &lt; 10000 时 ×10、加权
    /// a[3] + a[2]*1e5 + a[1]*1e7 + a[0]*1e9，本地权重 &lt; 远程权重 = 有更新。
    /// A 版远程走 SOAP（server.xhdcrm.com，已失效），B 版改 HTTP GET + 可配置 Url，
    /// 比较算法保持与 A 版一致，保证老用户升级时的版本判定结果可对齐。
    /// </summary>
    public static class VersionHelper
    {
        /// <summary>
        /// 计算版本号的加权值。
        /// </summary>
        /// <param name="version">版本号，形如 v3.0.20250920.0</param>
        /// <returns>加权值；输入无法解析为 4 段数字版本时返回 <c>null</c>。
        /// 不使用哨兵常量（如 long.MinValue），避免把「解析失败」隐式当成某个具体版本，
        /// 调用方通过 <c>null</c> 自行决定降级语义（见 <see cref="HasUpdate(string, string)"/>）。</returns>
        public static long? VersionWeight(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            // A 版：server_version.toLowerCase().replace('v','')，sys_version 同理
            var normalized = version.Trim().ToLowerInvariant().Replace("v", string.Empty);

            var segments = normalized.Split('.');
            if (segments.Length < 4)
            {
                // A 版隐式取 a[0]~a[3]，不足 4 段时 JS 得到 undefined*1=NaN，这里显式判为不可解析
                return null;
            }

            var numbers = new long[4];
            for (var i = 0; i < 4; i++)
            {
                if (!long.TryParse(segments[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]))
                {
                    return null;
                }
            }

            // A 版：if (a[3] * 1 < 10000) a[3] = a[3] * 10;
            if (numbers[3] < 10000)
            {
                numbers[3] *= 10;
            }

            // A 版：a[3]*1 + a[2]*100000 + a[1]*10000000 + a[0]*1000000000
            return numbers[3]
                + numbers[2] * 100000L
                + numbers[1] * 10000000L
                + numbers[0] * 1000000000L;
        }

        /// <summary>
        /// 判断本地版本是否落后于远程版本（本地权重 &lt; 远程权重）。
        /// </summary>
        /// <param name="current">当前（本地）版本号</param>
        /// <param name="latest">最新（远程）版本号</param>
        /// <returns>有更新返回 true。解析失败时按「宁可不提示也不误报」降级：
        /// 远程版本无法解析 → 返回 false（服务器数据异常时不打扰用户，也不假装是最新）；
        /// 当前版本无法解析 → 返回 true（本地数据异常时保守提示用户升级）。</returns>
        public static bool HasUpdate(string current, string latest)
        {
            var currentWeight = VersionWeight(current);
            var latestWeight = VersionWeight(latest);

            if (latestWeight == null)
            {
                // 远程版本不可解析 → 无法比较，不打扰用户
                return false;
            }

            if (currentWeight == null)
            {
                // 当前版本不可解析 → 保守视为落后，引导用户处理
                return true;
            }

            return currentWeight < latestWeight;
        }
    }
}
