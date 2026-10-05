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
    /// Sprint 10.38 Phase 3：DBAuthService（所有 Controller 数据权限闸门的共用底座）真实集成测试。
    /// 不使用任何 Mock，直接 new DBAuthService(new DBAuthRepository(fsql)) + SQLite in-memory，
    /// 覆盖 admin 短路、GetAuthType（0/1/2/3/4/5 六分支）、GetDataAuth（含指定部门、部门递归树）、GetAuth 真值三态。
    /// </summary>
    public class DBAuthServiceTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly DBAuthRepository _repository;
        private readonly DBAuthService _service;

        public DBAuthServiceTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _repository = new DBAuthRepository(_fsql);
            _service = new DBAuthService(_repository);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static hr_employee NewEmp(string id, string depId = "", string roleId = "") =>
            new hr_employee { id = id, name = $"员工-{id}", dep_id = depId, role_id = roleId };

        private static hr_department NewDept(string id, string parentId = "") =>
            new hr_department { id = id, dep_name = $"部门-{id}", parentid = parentId };

        private static Sys_role NewRole(string id, int? dataAuth) =>
            new Sys_role { id = id, RoleName = $"角色-{id}", DataAuth = dataAuth };

        private async Task InsertEmpAsync(string id, string depId = "", string roleId = "") =>
            await _fsql.Insert(NewEmp(id, depId, roleId)).ExecuteAffrowsAsync();

        private async Task InsertDeptAsync(string id, string parentId = "") =>
            await _fsql.Insert(NewDept(id, parentId)).ExecuteAffrowsAsync();

        private async Task InsertRoleAsync(string id, int? dataAuth) =>
            await _fsql.Insert(NewRole(id, dataAuth)).ExecuteAffrowsAsync();

        private async Task InsertAuthorityAsync(string id, string authId, string roleId) =>
            await _fsql.Insert(new Sys_authority
            {
                id = id,
                Auth_id = authId,
                Role_id = roleId,
                Auth_type = 1
            }).ExecuteAffrowsAsync();

        // 缺口 E：角色勾选指定部门（Sys_data_authority）
        private async Task InsertDataAuthorityAsync(string roleId, params string[] depIds)
        {
            foreach (var depId in depIds)
            {
                await _fsql.Insert(new Sys_data_authority
                {
                    id = Guid.NewGuid().ToString(),
                    Role_id = roleId,
                    dep_id = depId,
                    create_id = "TEST",
                    create_time = DateTime.Now
                }).ExecuteAffrowsAsync();
            }
        }

        // 装配一棵部门树：A(root) → B → C，以及无关部门 D
        private async Task SeedDeptTreeAsync()
        {
            await InsertDeptAsync("A");
            await InsertDeptAsync("B", "A");
            await InsertDeptAsync("C", "B");
            await InsertDeptAsync("D");
        }

        // ============ 1. admin 短路（含大小写） ============

        [Fact]
        public async Task GetAuthType_Admin_UpperCase_Returns5()
        {
            // 不插入任何员工/角色数据，证明 admin 完全不查表
            int authType = await _service.GetAuthType("ADMIN");
            Assert.Equal(5, authType);
        }

        [Fact]
        public async Task GetAuthType_Admin_LowerCase_Returns5()
        {
            int authType = await _service.GetAuthType("admin");
            Assert.Equal(5, authType);
        }

        [Fact]
        public async Task GetAuthType_Admin_MixedCase_Returns5()
        {
            int authType = await _service.GetAuthType("AdMiN");
            Assert.Equal(5, authType);
        }

        [Fact]
        public async Task GetAuth_Admin_AnyAuthId_ReturnsTrue()
        {
            // admin 短路：即使库里没有任何权限记录也返回 true
            Assert.True(await _service.GetAuth("admin", "customer_list"));
            Assert.True(await _service.GetAuth("ADMIN", "customer_delete"));
            Assert.True(await _service.GetAuth("Admin", "any_nonexistent_auth"));
        }

        [Fact]
        public async Task GetDataAuth_Admin_ReturnsAuthType5AndEmptyList()
        {
            // 造一些数据，证明 admin 走短路、不看库
            await InsertDeptAsync("D1");
            await InsertEmpAsync("E1", "D1");
            await InsertEmpAsync("E2", "D1");

            XHDRoleData auth = await _service.GetDataAuth("admin");

            Assert.Equal(5, auth.authtype);
            Assert.NotNull(auth.empList);
            Assert.Empty(auth.empList);
        }

        // ============ 2. 无角色员工 / 未知员工 ============

        [Fact]
        public async Task GetAuthType_EmployeeWithoutRole_Returns0()
        {
            await InsertEmpAsync("E1");
            Assert.Equal(0, await _service.GetAuthType("E1"));
        }

        [Fact]
        public async Task GetAuthType_RoleWithExplicitZeroDataAuth_Returns0()
        {
            await InsertRoleAsync("R1", 0);
            await InsertEmpAsync("E1", roleId: "R1");

            Assert.Equal(0, await _service.GetAuthType("E1"));
        }

        [Fact]
        public async Task GetAuthType_UnknownEmployee_Returns0()
        {
            Assert.Equal(0, await _service.GetAuthType("NO_SUCH_EMP"));
        }

        [Fact]
        public async Task GetAuthType_RoleWithNullDataAuth_Returns0()
        {
            await InsertRoleAsync("R1", null);
            await InsertEmpAsync("E1", roleId: "R1");

            Assert.Equal(0, await _service.GetAuthType("E1"));
        }

        [Fact]
        public async Task GetAuthType_RoleNotInDb_Returns0()
        {
            await InsertEmpAsync("E1", roleId: "GHOST_ROLE");

            Assert.Equal(0, await _service.GetAuthType("E1"));
        }

        [Fact]
        public async Task GetDataAuth_NoRoleEmployee_ReturnsAuthType0AndEmptyList()
        {
            await InsertEmpAsync("E1");
            // 干扰数据：别的员工有权限，不能影响本员工
            await InsertRoleAsync("R_OTHER", 4);
            await InsertEmpAsync("E2", roleId: "R_OTHER");

            XHDRoleData auth = await _service.GetDataAuth("E1");

            Assert.Equal(0, auth.authtype);
            Assert.NotNull(auth.empList);
            Assert.Empty(auth.empList);
        }

        [Fact]
        public async Task GetDataAuth_UnknownEmployee_ReturnsAuthType0()
        {
            XHDRoleData auth = await _service.GetDataAuth("NO_SUCH_EMP");

            Assert.Equal(0, auth.authtype);
            Assert.Empty(auth.empList);
        }

        // ============ 3. authtype=1 本人 ============

        [Fact]
        public async Task GetDataAuth_AuthType1_ReturnsOnlySelf()
        {
            await InsertRoleAsync("R1", 1);
            await InsertEmpAsync("E1", roleId: "R1");
            // 同部门、别的员工，必须不被纳入
            await InsertEmpAsync("E2", depId: "D1", roleId: "R1");

            XHDRoleData auth = await _service.GetDataAuth("E1");

            Assert.Equal(1, auth.authtype);
            Assert.Equal(new List<string> { "E1" }, auth.empList);
        }

        [Fact]
        public async Task GetAuthType_AuthType1_Returns1()
        {
            await InsertRoleAsync("R1", 1);
            await InsertEmpAsync("E1", roleId: "R1");

            Assert.Equal(1, await _service.GetAuthType("E1"));
        }

        // ============ 4. authtype=2 本部 ============

        [Fact]
        public async Task GetDataAuth_AuthType2_ReturnsSameDeptOnly()
        {
            await InsertDeptAsync("D1");
            await InsertDeptAsync("D2");
            await InsertRoleAsync("R1", 2);
            await InsertEmpAsync("E1", "D1", "R1");
            await InsertEmpAsync("E2", "D1", "R1");
            await InsertEmpAsync("E3", "D2", "R1");

            XHDRoleData auth = await _service.GetDataAuth("E1");

            Assert.Equal(2, auth.authtype);
            Assert.Equal(2, auth.empList.Count);
            Assert.DoesNotContain("E3", auth.empList);
            Assert.True(auth.empList.OrderBy(x => x).SequenceEqual(new[] { "E1", "E2" }));
        }

        [Fact]
        public async Task GetDataAuth_AuthType2_EmployeeWithoutDept_ReturnsEmptyList()
        {
            await InsertRoleAsync("R1", 2);
            await InsertEmpAsync("E1", roleId: "R1");

            XHDRoleData auth = await _service.GetDataAuth("E1");

            Assert.Equal(2, auth.authtype);
            Assert.Empty(auth.empList);
        }

        // ============ 5. authtype=3 本部及下级（递归） ============

        [Fact]
        public async Task GetDataAuth_AuthType3_RecursesChildDepartments()
        {
            await SeedDeptTreeAsync();
            await InsertRoleAsync("R1", 3);
            await InsertEmpAsync("EA", "A", "R1");
            await InsertEmpAsync("EB", "B", "R1");
            await InsertEmpAsync("EC", "C", "R1");
            await InsertEmpAsync("ED", "D", "R1"); // 无关部门，必须排除

            XHDRoleData auth = await _service.GetDataAuth("EA");

            Assert.Equal(3, auth.authtype);
            Assert.Equal(3, auth.empList.Count);
            Assert.DoesNotContain("ED", auth.empList);
            Assert.True(auth.empList.OrderBy(x => x).SequenceEqual(new[] { "EA", "EB", "EC" }));
        }

        [Fact]
        public async Task GetDataAuth_AuthType3_FromLeafDept_OnlyOwnBranch()
        {
            await SeedDeptTreeAsync();
            await InsertRoleAsync("R1", 3);
            await InsertEmpAsync("EA", "A", "R1");
            await InsertEmpAsync("EB", "B", "R1");
            await InsertEmpAsync("EC", "C", "R1");

            // 从叶子部门 C 出发：只能向下（无子部门），不能向上回溯到 A/B
            XHDRoleData auth = await _service.GetDataAuth("EC");

            Assert.Equal(3, auth.authtype);
            Assert.Equal(new List<string> { "EC" }, auth.empList);
        }

        [Fact]
        public async Task GetDataAuth_AuthType3_DeepTree_RecursesAllLevels()
        {
            // 更深的树 A→B→C→E，验证递归不止一层
            await InsertDeptAsync("A");
            await InsertDeptAsync("B", "A");
            await InsertDeptAsync("C", "B");
            await InsertDeptAsync("E", "C");
            await InsertRoleAsync("R1", 3);
            await InsertEmpAsync("EA", "A", "R1");
            await InsertEmpAsync("EE", "E", "R1");

            XHDRoleData auth = await _service.GetDataAuth("EA");

            Assert.Equal(3, auth.authtype);
            Assert.Equal(2, auth.empList.Count);
            Assert.Contains("EE", auth.empList);
        }

        [Fact]
        public async Task GetDataAuth_AuthType3_NoDuplicateEmployeeIds()
        {
            // 不变量：本部 + 子部门合并后 empList 不应有重复 id
            await SeedDeptTreeAsync();
            await InsertRoleAsync("R1", 3);
            await InsertEmpAsync("EA", "A", "R1");
            await InsertEmpAsync("EB", "B", "R1");
            await InsertEmpAsync("EC", "C", "R1");

            XHDRoleData auth = await _service.GetDataAuth("EA");

            Assert.Equal(3, auth.authtype);
            Assert.Equal(auth.empList.Count, auth.empList.Distinct().Count());
        }

        [Fact]
        public async Task GetDataAuth_AuthType3_ChildDeptIsEmpty_NoError()
        {
            await SeedDeptTreeAsync();
            await InsertRoleAsync("R1", 3);
            await InsertEmpAsync("EA", "A", "R1");
            // B/C 部门没有员工

            XHDRoleData auth = await _service.GetDataAuth("EA");

            Assert.Equal(3, auth.authtype);
            Assert.Equal(new List<string> { "EA" }, auth.empList);
        }

        // ============ 6. authtype=4 指定部门（Sys_data_authority 勾选部门） ============

        [Fact]
        public async Task GetDataAuth_AuthType4_NoDepartmentsSelected_EmptyList()
        {
            // 未在 Sys_data_authority 勾选任何部门 → 可见员工为空集
            await InsertDeptAsync("D1");
            await InsertDeptAsync("D2");
            await InsertRoleAsync("R1", 4);
            await InsertEmpAsync("E1", "D1", "R1");
            await InsertEmpAsync("E2", "D1", "R1");
            await InsertEmpAsync("E3", "D2", "R1");

            XHDRoleData auth = await _service.GetDataAuth("E1");

            Assert.Equal(4, auth.authtype);
            Assert.NotNull(auth.empList);
            Assert.Empty(auth.empList);
        }

        [Fact]
        public async Task GetDataAuth_AuthType4_ReturnsEmployeesInSelectedDepartments()
        {
            // 勾选 D1 → 只见 D1 员工，D2 员工不可见
            await InsertDeptAsync("D1");
            await InsertDeptAsync("D2");
            await InsertRoleAsync("R1", 4);
            await InsertEmpAsync("E1", "D1", "R1");
            await InsertEmpAsync("E2", "D1", "R1");
            await InsertEmpAsync("E3", "D2", "R1");
            await InsertDataAuthorityAsync("R1", "D1");

            XHDRoleData auth = await _service.GetDataAuth("E1");

            Assert.Equal(4, auth.authtype);
            Assert.Equal(2, auth.empList.Count);
            Assert.Contains("E1", auth.empList);
            Assert.Contains("E2", auth.empList);
            Assert.DoesNotContain("E3", auth.empList);
        }

        [Fact]
        public async Task GetDataAuth_AuthType4_OtherRoleDepartmentsNotVisible()
        {
            // A 版 get_depAp_emp_ids 只取本角色（B 版单角色）的 Sys_data_authority 行
            await InsertDeptAsync("D1");
            await InsertDeptAsync("D2");
            await InsertRoleAsync("R1", 4);
            await InsertRoleAsync("R2", 4);
            await InsertEmpAsync("E1", "D1", "R1");
            await InsertEmpAsync("E2", "D2", "R2");
            // R2 勾的是 D2，对 R1 的员工不可见
            await InsertDataAuthorityAsync("R2", "D2");
            await InsertDataAuthorityAsync("R1", "D1");

            XHDRoleData auth = await _service.GetDataAuth("E1");

            Assert.Equal(4, auth.authtype);
            Assert.Equal(new List<string> { "E1" }, auth.empList);
        }

        [Fact]
        public async Task GetAuthType_AuthType4_Returns4()
        {
            await InsertRoleAsync("R1", 4);
            await InsertEmpAsync("E1", roleId: "R1");

            Assert.Equal(4, await _service.GetAuthType("E1"));
        }

        // ============ 7. authtype=5 全部（不变量：empList 为空） ============

        [Fact]
        public async Task GetDataAuth_AuthType5_EmpListIsEmpty_Invariant()
        {
            // ★ 关键不变量：authtype=5 与 authtype=0 的 empList 都是空列表，
            // 判权限必须看 authtype，不能看 empList 是否为空
            await InsertDeptAsync("D1");
            await InsertDeptAsync("D2");
            await InsertRoleAsync("R1", 5);
            await InsertEmpAsync("E1", "D1", "R1");
            await InsertEmpAsync("E2", "D1", "R1");
            await InsertEmpAsync("E3", "D2", "R1");

            XHDRoleData auth = await _service.GetDataAuth("E1");

            Assert.Equal(5, auth.authtype);
            Assert.NotNull(auth.empList);
            Assert.Empty(auth.empList);
        }

        [Fact]
        public async Task GetDataAuth_AuthType5_MustNotBeMistakenForNoPermission()
        {
            // 同样是空 empList，authtype=5（全部）与 authtype=0（无权限）语义完全相反
            await InsertRoleAsync("R_FULL", 5);
            await InsertRoleAsync("R_NONE", 0);
            await InsertEmpAsync("E_FULL", roleId: "R_FULL");
            await InsertEmpAsync("E_NONE", roleId: "R_NONE");

            XHDRoleData full = await _service.GetDataAuth("E_FULL");
            XHDRoleData none = await _service.GetDataAuth("E_NONE");

            Assert.Equal(5, full.authtype);
            Assert.Equal(0, none.authtype);
            Assert.Empty(full.empList);
            Assert.Empty(none.empList);
            Assert.NotEqual(full.authtype, none.authtype);
        }

        [Fact]
        public async Task GetAuthType_AuthType5_Returns5()
        {
            await InsertRoleAsync("R1", 5);
            await InsertEmpAsync("E1", roleId: "R1");

            Assert.Equal(5, await _service.GetAuthType("E1"));
        }

        // ============ 8. 多角色取 Max ============

        [Fact]
        public async Task GetAuthType_MultipleRolesInSystem_ReturnsEmployeeRoleDataAuth()
        {
            // hr_employee.role_id 为单一外键，库内存在多个角色时取员工所挂角色的 DataAuth
            await InsertRoleAsync("R_LOW", 1);
            await InsertRoleAsync("R_HIGH", 3);
            await InsertEmpAsync("E1", roleId: "R_HIGH");

            Assert.Equal(3, await _service.GetAuthType("E1"));
        }

        [Fact]
        public async Task GetAuthType_RoleDataAuth2_Returns2()
        {
            await InsertRoleAsync("R1", 2);
            await InsertEmpAsync("E1", roleId: "R1");

            Assert.Equal(2, await _service.GetAuthType("E1"));
        }

        // ============ 8. GetAuth 真值三态 ============

        [Fact]
        public async Task GetAuth_HasAuthority_ReturnsTrue()
        {
            await InsertRoleAsync("R1", 2);
            await InsertEmpAsync("E1", roleId: "R1");
            await InsertAuthorityAsync("A1", "customer_list", "R1");

            Assert.True(await _service.GetAuth("E1", "customer_list"));
        }

        [Fact]
        public async Task GetAuth_NoAuthorityRecord_ReturnsFalse()
        {
            await InsertRoleAsync("R1", 2);
            await InsertEmpAsync("E1", roleId: "R1");
            await InsertAuthorityAsync("A1", "customer_list", "R1");

            // 角色没有这个权限
            Assert.False(await _service.GetAuth("E1", "customer_delete"));
        }

        [Fact]
        public async Task GetAuth_AuthorityBelongsToOtherRole_ReturnsFalse()
        {
            await InsertRoleAsync("R1", 2);
            await InsertRoleAsync("R2", 2);
            await InsertEmpAsync("E1", roleId: "R1");
            await InsertAuthorityAsync("A1", "customer_list", "R2");

            Assert.False(await _service.GetAuth("E1", "customer_list"));
        }

        [Fact]
        public async Task GetAuth_EmployeeWithoutRole_ReturnsFalse()
        {
            await InsertEmpAsync("E1");
            await InsertAuthorityAsync("A1", "customer_list", "R1");

            Assert.False(await _service.GetAuth("E1", "customer_list"));
        }

        [Fact]
        public async Task GetAuth_UnknownEmployee_ReturnsFalse()
        {
            Assert.False(await _service.GetAuth("NO_SUCH_EMP", "customer_list"));
        }

        [Fact]
        public async Task GetAuth_Admin_ShortCircuitsBeforeDbQuery()
        {
            // admin 短路在 service 层（emp_id.ToLower().Equals("admin")），
            // 即便角色/权限表完全空也必须 true
            Assert.True(await _service.GetAuth("admin", "anything"));
        }
    }
}
