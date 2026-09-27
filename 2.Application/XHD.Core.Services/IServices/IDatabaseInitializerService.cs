// SPDX-License-Identifier: MIT
// Sprint 10 - 2026-09-22: 数据库初始化服务接口。
// 根因修复：把 SysConfigController 里塞的 8 个 config* 方法下沉到 Application 层，
//           集中事务 + 幂等 + ILogger。Controller 只保留 HTTP 配置和调用。

using FreeSql;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XHD.Core.IServices
{
    /// <summary>
    /// 种子数据插入结果。
    /// </summary>
    public class SeedResult
    {
        /// <summary>整体是否成功（含"已初始化"和"新建完成"）</summary>
        public bool Success { get; set; }

        /// <summary>true = 首次初始化；false = 已初始化，跳过</summary>
        public bool AlreadySeeded { get; set; }

        /// <summary>本次实际插入的行总数（已初始化时为 0）</summary>
        public int TotalInserted { get; set; }

        /// <summary>每张表插入行数（与调用顺序一致）</summary>
        public IReadOnlyList<int> RecordsPerTable { get; set; } = new List<int>();

        /// <summary>给前端展示的消息</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>失败时携带的异常</summary>
        public string Error { get; set; }

        public static SeedResult AlreadySeeded() => new SeedResult
        {
            Success = true,
            AlreadySeeded = true,
            TotalInserted = 0,
            Message = "系统已初始化，无需重复执行。"
        };

        public static SeedResult Success(int total, IReadOnlyList<int> perTable) => new SeedResult
        {
            Success = true,
            AlreadySeeded = false,
            TotalInserted = total,
            RecordsPerTable = perTable,
            Message = $"初始化完成，共插入 {total} 行。"
        };

        public static SeedResult Failed(string error) => new SeedResult
        {
            Success = false,
            AlreadySeeded = false,
            TotalInserted = 0,
            Error = error,
            Message = "初始化失败：" + error
        };
    }

    /// <summary>
    /// 数据库初始化服务：一次性、事务、幂等地把系统必需种子数据写入数据库。
    /// </summary>
    /// <remarks>
    /// 设计约束：
    /// <list type="bullet">
    /// <item>整个 SeedAsync 必须在单个事务内完成，任何一步失败全部回滚。</item>
    /// <item>通过 <see cref="Sys_info"/> 表 <c>sys_key = "seeded"</c> 做数据库级幂等标记。</item>
    /// <item>所有种子数据的 GUID 均为固定值，禁止 <c>Guid.NewGuid()</c>。</item>
    /// <item>本接口不注入 IFreeSql，而是通过方法参数接收，方便首次配置时注入自定义连接。</item>
    /// </list>
    /// </remarks>
    public interface IDatabaseInitializerService
    {
        /// <summary>
        /// 判断数据库是否已经初始化过。
        /// </summary>
        /// <param name="fsql">目标数据库的 FreeSql 实例</param>
        /// <returns>true = 已存在种子标记</returns>
        Task<bool> IsSeededAsync(IFreeSql fsql);

        /// <summary>
        /// 执行初始化。若已初始化且 <paramref name="force"/> = false，直接返回
        /// <see cref="SeedResult.AlreadySeeded"/>；若已初始化且 force = true，
        /// 会先清理旧数据再重新插入。
        /// </summary>
        /// <param name="fsql">目标数据库的 FreeSql 实例</param>
        /// <param name="force">true = 强制重新初始化（先删旧数据）</param>
        /// <returns>结果对象，永不抛异常</returns>
        Task<SeedResult> SeedAsync(IFreeSql fsql, bool force = false);
    }
}
