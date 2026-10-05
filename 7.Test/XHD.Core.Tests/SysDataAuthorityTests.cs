using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using XHD.Core.Common;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.Services;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.43 缺口 E：Sys_data_authority（数据权限-指定部门）仓储/服务真实集成测试。
    /// 不使用任何 Mock：new Sys_data_authorityService(new Sys_data_authorityRepository(fsql)) + SQLite in-memory。
    /// 对齐 A 版 Server/Sys_data_authority.cs 的 get/save 契约（delete-then-insert）。
    /// </summary>
    public class SysDataAuthorityTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sys_data_authorityRepository _repository;
        private readonly Sys_data_authorityService _service;

        public SysDataAuthorityTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _repository = new Sys_data_authorityRepository(_fsql);
            _service = new Sys_data_authorityService(_repository);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 查询 ============

        [Fact]
        public async Task GetDepIds_RoleWithSelectedDepts_ReturnsDepIds()
        {
            await SeedAuthorityRowsAsync("R1", "D1", "D2");

            var depIds = await _service.GetDepIdsByRoleIdAsync("R1");

            Assert.Equal(2, depIds.Count);
            Assert.Contains("D1", depIds);
            Assert.Contains("D2", depIds);
        }

        [Fact]
        public async Task GetDepIds_RoleWithoutRows_ReturnsEmpty()
        {
            await SeedAuthorityRowsAsync("R_OTHER", "D1");

            var depIds = await _service.GetDepIdsByRoleIdAsync("R1");

            Assert.NotNull(depIds);
            Assert.Empty(depIds);
        }

        [Fact]
        public async Task GetDepIds_EmptyRoleId_ReturnsEmpty()
        {
            var depIds = await _service.GetDepIdsByRoleIdAsync("");
            Assert.Empty(depIds);
        }

        [Fact]
        public async Task GetDepIds_OnlyReturnsOwnRoleRows()
        {
            // 两角色各勾不同部门，互不干扰
            await SeedAuthorityRowsAsync("R1", "D1");
            await SeedAuthorityRowsAsync("R2", "D2");

            var r1 = await _service.GetDepIdsByRoleIdAsync("R1");
            var r2 = await _service.GetDepIdsByRoleIdAsync("R2");

            Assert.Equal(new List<string> { "D1" }, r1);
            Assert.Equal(new List<string> { "D2" }, r2);
        }

        // ============ 保存（先删后插） ============

        [Fact]
        public async Task Save_InsertsAllDepIds()
        {
            int inserted = await _service.SaveAsync("R1", new[] { "D1", "D2", "D3" }, "OP");

            Assert.Equal(3, inserted);
            var depIds = await _service.GetDepIdsByRoleIdAsync("R1");
            Assert.Equal(3, depIds.Count);
        }

        [Fact]
        public async Task Save_ReplacesPreviousSelection()
        {
            await _service.SaveAsync("R1", new[] { "D1", "D2" }, "OP");

            // 再保存完全不同的集合：旧的必须被清掉
            int inserted = await _service.SaveAsync("R1", new[] { "D3" }, "OP");

            Assert.Equal(1, inserted);
            var depIds = await _service.GetDepIdsByRoleIdAsync("R1");
            Assert.Equal(new List<string> { "D3" }, depIds);
        }

        [Fact]
        public async Task Save_EmptyDepIds_ClearsAll()
        {
            await _service.SaveAsync("R1", new[] { "D1", "D2" }, "OP");

            int inserted = await _service.SaveAsync("R1", Enumerable.Empty<string>(), "OP");

            Assert.Equal(0, inserted);
            var depIds = await _service.GetDepIdsByRoleIdAsync("R1");
            Assert.Empty(depIds);
        }

        [Fact]
        public async Task Save_DedupesAndTrims()
        {
            // A 版按逗号分割不去重；B 侧去空去重，避免同一部门多行
            int inserted = await _service.SaveAsync("R1", new[] { " D1 ", "", "D1", "  ", "D2" }, "OP");

            Assert.Equal(2, inserted);
            var depIds = await _service.GetDepIdsByRoleIdAsync("R1");
            Assert.Equal(2, depIds.Count);
            Assert.Contains("D1", depIds);
            Assert.Contains("D2", depIds);
        }

        [Fact]
        public async Task Save_WritesCreateIdAndTime()
        {
            await _service.SaveAsync("R1", new[] { "D1" }, "OPERATOR");

            var rows = await _fsql.Select<Sys_data_authority>()
                .Where(a => a.Role_id == "R1")
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal("OPERATOR", rows[0].create_id);
            Assert.NotNull(rows[0].create_time);
        }

        [Fact]
        public async Task Save_DoesNotAffectOtherRoles()
        {
            await _service.SaveAsync("R1", new[] { "D1" }, "OP");
            await _service.SaveAsync("R2", new[] { "D2" }, "OP");

            await _service.SaveAsync("R1", new[] { "D3" }, "OP");

            var r2 = await _service.GetDepIdsByRoleIdAsync("R2");
            Assert.Equal(new List<string> { "D2" }, r2);
        }

        [Fact]
        public async Task Save_EmptyRoleId_ReturnsZero()
        {
            int inserted = await _service.SaveAsync("", new[] { "D1" }, "OP");
            Assert.Equal(0, inserted);
        }

        // ============ 辅助 ============

        private async Task SeedAuthorityRowsAsync(string roleId, params string[] depIds)
        {
            foreach (var depId in depIds)
            {
                await _fsql.Insert(new Sys_data_authority
                {
                    id = Guid.NewGuid().ToString(),
                    Role_id = roleId,
                    dep_id = depId,
                    create_id = "SEED",
                    create_time = DateTime.Now
                }).ExecuteAffrowsAsync();
            }
        }
    }
}
