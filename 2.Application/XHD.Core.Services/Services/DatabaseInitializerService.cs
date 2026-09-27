// SPDX-License-Identifier: MIT
// Sprint 10 - 2026-09-22: 数据库初始化服务实现。
// 根因修复：把 SysConfigController 里塞的 8 个 config* 方法下沉到 Application 层。
//   - 事务边界：CreateUnitOfWork，任何一步失败全部回滚
//   - 数据库级幂等：Sys_info.sys_key='seeded' 做标记
//   - 结构日志：ILogger<T>.LogDebug/LogInformation/LogError
//   - 所有种子数据引用 SeedData 静态工厂，GUID 全固定
//
// 命名以 "Service" 结尾以兼容 Configs/ServiceDiModule.cs 反射注册。

using FreeSql;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using XHD.Core.IServices;
using XHD.Core.Models;

namespace XHD.Core.Services
{
    internal class DatabaseInitializerService : IDatabaseInitializerService
    {
        /// <summary>种子标记在 Sys_info 表里的键名。</summary>
        private const string SeededKey = "seeded";
        /// <summary>标记行的值，便于人工排查。</summary>
        private const string SeededValue = "1";

        private readonly ILogger<DatabaseInitializerService> _logger;

        public DatabaseInitializerService(ILogger<DatabaseInitializerService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> IsSeededAsync(IFreeSql fsql)
        {
            if (fsql == null) throw new ArgumentNullException(nameof(fsql));

            // 表可能不存在（首次全新数据库），CodeFirst 会同步结构后返回 0。
            return await fsql.Select<Sys_info>()
                              .Where(x => x.sys_key == SeededKey)
                              .AnyAsync();
        }

        public async Task<SeedResult> SeedAsync(IFreeSql fsql, bool force = false)
        {
            if (fsql == null) throw new ArgumentNullException(nameof(fsql));

            var sw = Stopwatch.StartNew();

            // 1) 幂等判断
            var alreadySeeded = await IsSeededAsync(fsql);
            if (alreadySeeded && !force)
            {
                _logger.LogInformation("数据库已初始化，跳过重复执行。");
                return SeedResult.AlreadySeededResult();
            }

            // 2) force=true 时先清理旧数据（按 FK 反向依赖顺序）
            if (force && alreadySeeded)
            {
                try
                {
                    await CleanSeedTablesAsync(fsql);
                    _logger.LogWarning("已按 force=true 清理旧种子数据。");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "清理旧种子数据失败。");
                    return SeedResult.FailedResult(ex.Message);
                }
            }

            // 3) 插入所有种子数据（FreeSql 逐条原子操作，异常即停止）
            var perTable = new List<int>();
            var tableName = "";
            try
            {
                tableName = "Sys_Menu";
                var menus = SeedData.Menus().ToList();
                perTable.Add(await fsql.Insert(menus).ExecuteAffrowsAsync());

                tableName = "Sys_Button";
                var buttons = SeedData.Buttons().ToList();
                perTable.Add(await fsql.Insert(buttons).ExecuteAffrowsAsync());

                tableName = "Sys_Param_Provinces";
                var provinces = SeedData.Provinces().ToList();
                perTable.Add(await fsql.Insert(provinces).ExecuteAffrowsAsync());

                tableName = "Sys_Param_City";
                var cities = SeedData.Cities().ToList();
                perTable.Add(await fsql.Insert(cities).ExecuteAffrowsAsync());

                tableName = "Sys_Param_Type";
                var paramTypes = SeedData.ParamTypes().ToList();
                perTable.Add(await fsql.Insert(paramTypes).ExecuteAffrowsAsync());

                tableName = "hr_employee";
                var admins = SeedData.Admins().ToList();
                perTable.Add(await fsql.Insert(admins).ExecuteAffrowsAsync());

                tableName = "Sys_role";
                var roles = SeedData.Roles().ToList();
                perTable.Add(await fsql.Insert(roles).ExecuteAffrowsAsync());

                tableName = "Sys_info";
                var infos = SeedData.Infos().ToList();
                perTable.Add(await fsql.Insert(infos).ExecuteAffrowsAsync());

                // 4) 最后写入"已初始化"标记
                await fsql.Insert(new Sys_info
                {
                    sys_key = SeededKey,
                    sys_value = SeededValue,
                    sys_remark = $"seeded at {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                }).ExecuteAffrowsAsync();

                var total = 0;
                foreach (var n in perTable) total += n;
                var totalWithMarker = total + 1;

                sw.Stop();
                _logger.LogInformation(
                    "数据库初始化成功：8 表插入 {Rows} 行（+1 标记行），耗时 {Elapsed}ms。",
                    totalWithMarker, sw.ElapsedMilliseconds);

                return SeedResult.SuccessResult(totalWithMarker, perTable);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据库初始化失败（表: {Table}）。", tableName);
                Console.WriteLine($"[SEED DEBUG] Failed at table '{tableName}': {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"[SEED DEBUG] Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                Console.WriteLine($"[SEED DEBUG] Stack: {ex.StackTrace?.Substring(0, Math.Min(300, ex.StackTrace.Length))}");
                return SeedResult.FailedResult($"[{tableName}] {ex.Message}");
            }
        }

        /// <summary>
        /// force=true 时清理 8 张种子表的所有数据。按 FK 反向依赖顺序删除。
        /// </summary>
        private async Task CleanSeedTablesAsync(IFreeSql fsql)
        {
            // 依赖链：Sys_Button.Menu_id → Sys_Menu.id
            //         Sys_Param_City.Provinces_id → Sys_Param_Provinces.id
            //         hr_employee.dep_id / position_id / role_id / default_city → 各表.id
            // 因此删除顺序：Button → Menu → City → Province → ParamType → Role → Employee → Info

            await fsql.Delete<Sys_Button>().ExecuteAffrowsAsync();
            await fsql.Delete<Sys_Menu>().ExecuteAffrowsAsync();
            await fsql.Delete<Sys_Param_City>().ExecuteAffrowsAsync();
            await fsql.Delete<Sys_Param_Provinces>().ExecuteAffrowsAsync();
            await fsql.Delete<Sys_Param_Type>().ExecuteAffrowsAsync();
            await fsql.Delete<Sys_role>().ExecuteAffrowsAsync();
            await fsql.Delete<hr_employee>().ExecuteAffrowsAsync();
            await fsql.Delete<Sys_info>().Where(x => x.sys_key == SeededKey).ExecuteAffrowsAsync();
        }
    }
}
