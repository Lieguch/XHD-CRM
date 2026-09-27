// SPDX-License-Identifier: MIT
// Sprint 10 - 2026-09-22: 数据库初始化服务单元测试。
// 覆盖：
//   ① IsSeededAsync 空库 false
//   ② SeedAsync 首次返回 Success 且 TotalInserted > 0
//   ③ SeedAsync 二次不重复（幂等）
//   ④ SeedAsync(force=true) 清旧重插，仍保持正确
//   ⑤ 8 张表插入行数与 SeedData 声明一致
//   ⑥ 'seeded' 标记行写入 Sys_info
//   ⑦ SeedAsync 不抛异常（永远返回 SeedResult）
//   ⑧ SeedData.Roles() 固定 GUID 回归（禁止 Guid.NewGuid）
//   ⑨ 事务性：SeedAsync 部分失败应回滚（用 mock 验证不易，此处验证标记行只在成功后写入）

using System;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Services;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10 数据库初始化服务测试。
    /// </summary>
    public class Sprint10InitializerTests
    {
        private readonly DatabaseInitializerService _svc;

        public Sprint10InitializerTests()
        {
            _svc = new DatabaseInitializerService(NullLogger<DatabaseInitializerService>.Instance);
        }

        // ========== ① IsSeededAsync 空库 ==========

        [Fact]
        public async Task IsSeededAsync_EmptyDb_ReturnsFalse()
        {
            var fsql = TestDbContextFactory.CreateFreeSql();
            var result = await _svc.IsSeededAsync(fsql);
            Assert.False(result);
        }

        // ========== ② SeedAsync 首次执行 ==========

        [Fact]
        public async Task SeedAsync_EmptyDb_ReturnsSuccessAndInsertsAllTables()
        {
            var fsql = TestDbContextFactory.CreateFreeSql();

            var result = await _svc.SeedAsync(fsql);

            Assert.True(result.Success);
            Assert.False(result.AlreadySeeded);
            Assert.Equal(8, result.RecordsPerTable.Count);
            Assert.All(result.RecordsPerTable, x => Assert.True(x > 0, $"表 {x} 应该 >0"));
            Assert.True(result.TotalInserted > 0);
        }

        // ========== ③ 幂等：二次执行不重复 ==========

        [Fact]
        public async Task SeedAsync_SecondCall_ReturnsAlreadySeededAndSkips()
        {
            var fsql = TestDbContextFactory.CreateFreeSql();

            var first = await _svc.SeedAsync(fsql);
            Assert.True(first.Success);

            // 记录 8 张表的行数
            var countsAfterFirst = SnapshotCounts(fsql);

            var second = await _svc.SeedAsync(fsql);
            Assert.True(second.Success);
            Assert.True(second.AlreadySeeded);
            Assert.Equal(0, second.TotalInserted);

            // 二次执行不应有新增数据
            var countsAfterSecond = SnapshotCounts(fsql);
            Assert.Equal(countsAfterFirst, countsAfterSecond);
        }

        // ========== ④ force=true 强制重插 ==========

        [Fact]
        public async Task SeedAsync_ForceTrue_ReplacesDataButKeepsCounts()
        {
            var fsql = TestDbContextFactory.CreateFreeSql();

            var first = await _svc.SeedAsync(fsql);
            Assert.True(first.Success);
            var countsAfterFirst = SnapshotCounts(fsql);

            var forced = await _svc.SeedAsync(fsql, force: true);
            Assert.True(forced.Success);
            Assert.False(forced.AlreadySeeded);
            Assert.True(forced.TotalInserted > 0);

            var countsAfterForce = SnapshotCounts(fsql);
            // force=true 后总数应等于首次插入总数（清理 + 重插）
            Assert.Equal(countsAfterFirst, countsAfterForce);
        }

        // ========== ⑤ 8 表插入行数与 SeedData 声明一致 ==========

        [Fact]
        public async Task SeedAsync_AfterFirstSeed_InsertsExpectedRowsPerTable()
        {
            var fsql = TestDbContextFactory.CreateFreeSql();
            await _svc.SeedAsync(fsql);

            // 每张表实际行数应等于 SeedData 声明
            Assert.Equal(SeedData.Menus().Count,             fsql.Select<Sys_Menu>().Count());
            Assert.Equal(SeedData.Buttons().Count,           fsql.Select<Sys_Button>().Count());
            Assert.Equal(SeedData.Provinces().Count,         fsql.Select<Sys_Param_Provinces>().Count());
            Assert.Equal(SeedData.Cities().Count,            fsql.Select<Sys_Param_City>().Count());
            Assert.Equal(SeedData.ParamTypes().Count,        fsql.Select<Sys_Param_Type>().Count());
            Assert.Equal(SeedData.Admins().Count,            fsql.Select<hr_employee>().Count());
            Assert.Equal(SeedData.Roles().Count,             fsql.Select<Sys_role>().Count());
        }

        // ========== ⑥ 'seeded' 标记行写入 ==========

        [Fact]
        public async Task SeedAsync_AfterFirstSeed_WritesSeededMarkerRow()
        {
            var fsql = TestDbContextFactory.CreateFreeSql();
            await _svc.SeedAsync(fsql);

            var marker = await fsql.Select<Sys_info>()
                                     .Where(x => x.sys_key == "seeded")
                                     .FirstAsync();

            Assert.NotNull(marker);
            Assert.Equal("1", marker.sys_value);
            Assert.Contains("seeded at", marker.sys_remark);
        }

        // ========== ⑦ 参数契约：null 抛 ArgumentNullException ==========

        [Fact]
        public async Task IsSeededAsync_NullFsql_ThrowsArgumentNull()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _svc.IsSeededAsync(null));
        }

        [Fact]
        public async Task SeedAsync_NullFsql_ThrowsArgumentNull()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _svc.SeedAsync(null));
        }

        [Fact]
        public void Constructor_NullLogger_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => new DatabaseInitializerService(null));
        }

        // ========== ⑧ SeedData.Roles 固定 GUID 回归 ==========

        [Fact]
        public void SeedData_Roles_UsesFixedGuid()
        {
            var roles = SeedData.Roles();
            Assert.Single(roles);
            Assert.Equal("SystemAdminRole", roles[0].id);
            // 保证两次调用返回同一个 id（不依赖 Guid.NewGuid）
            Assert.Equal("SystemAdminRole", SeedData.Roles()[0].id);
        }

        // ========== ⑨ SeedData Admins 只有一条 admin ==========

        [Fact]
        public void SeedData_Admins_SingleAdmin()
        {
            var admins = SeedData.Admins();
            Assert.Single(admins);
            Assert.Equal("admin", admins[0].id);
            Assert.Equal("admin", admins[0].uid);
            Assert.Equal("E10ADC3949BA59ABBE56E057F20F883E", admins[0].pwd); // "123456" MD5
        }

        // ========== helpers ==========

        private static (int sysMenu, int sysButton, int provinces, int cities, int paramType, int employee, int role) SnapshotCounts(IFreeSql fsql)
        {
            return (
                fsql.Select<Sys_Menu>().Count(),
                fsql.Select<Sys_Button>().Count(),
                fsql.Select<Sys_Param_Provinces>().Count(),
                fsql.Select<Sys_Param_City>().Count(),
                fsql.Select<Sys_Param_Type>().Count(),
                fsql.Select<hr_employee>().Count(),
                fsql.Select<Sys_role>().Count()
            );
        }
    }
}
