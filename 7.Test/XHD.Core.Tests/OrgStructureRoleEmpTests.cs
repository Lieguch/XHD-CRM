using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.Services;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// 组织架构线（二）：SysRoleController + SysRoleEmpController + DataAuthConfigController。
    /// 全部走真实 SQLite 内存库 + 真实 Repository/Service，只 Mock ILogger。
    /// 覆盖：角色 Grid/Combo/Save/Del、角色-员工中间表 Add/Remove 真实落库与删除、
    /// Emplist/Get 的 NOT IN/IN 语义（排除 admin）、数据权限 0-4 层级校验与审计日志。
    /// </summary>
    public class OrgStructureRoleEmpTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly ISys_roleService _roleSvc;
        private readonly ISys_role_empService _roleEmpSvc;
        private readonly Ihr_employeeService _empSvc;
        private readonly ISys_logService _logSvc;

        public OrgStructureRoleEmpTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _roleSvc = new Sys_roleService(new Sys_roleRepository(_fsql));
            _roleEmpSvc = new Sys_role_empService(
                new Sys_role_empRepository(_fsql),
                new Sys_roleRepository(_fsql),
                new hr_employeeRepository(_fsql));
            _empSvc = new hr_employeeService(new hr_employeeRepository(_fsql));
            _logSvc = new Sys_logService(new Sys_logRepository(_fsql));
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static Sys_role NewRole(string id, string name = "管理员", int? dataAuth = null, int sort = 10)
        {
            return new Sys_role
            {
                id = id,
                RoleName = name,
                RoleSort = sort,
                DataAuth = dataAuth,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        private static hr_employee NewEmp(string id, string uid, string name)
        {
            // isDelete=0：GetEmpIdsNotInRoleAsync 只取未删除员工，null 会被过滤掉
            return new hr_employee
            {
                id = id,
                uid = uid,
                name = name,
                isDelete = 0,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        private static Sys_role_emp NewRoleEmp(string roleId, string empId)
        {
            return new Sys_role_emp
            {
                id = Guid.NewGuid().ToString(),
                role_id = roleId,
                emp_id = empId
            };
        }

        // ============ Controller 装配辅助 ============

        private SysRoleController CreateRoleController(string userId = "TEST_USER")
        {
            return TestControllerHelper.CreateWithHttpContext<SysRoleController>(
                string.Empty, userId, "Test User",
                new Mock<ILogger<SysRoleController>>().Object,
                _empSvc,
                _roleSvc,
                _logSvc);
        }

        private SysRoleEmpController CreateRoleEmpController(string userId = "TEST_USER")
        {
            return TestControllerHelper.CreateWithHttpContext<SysRoleEmpController>(
                string.Empty, userId, "Test User",
                new Mock<ILogger<SysRoleEmpController>>().Object,
                _roleEmpSvc,
                _logSvc);
        }

        private DataAuthConfigController CreateDataAuthController(string userId = "TEST_USER")
        {
            return TestControllerHelper.CreateWithHttpContext<DataAuthConfigController>(
                string.Empty, userId, "Test User",
                new Mock<ILogger<DataAuthConfigController>>().Object,
                _roleSvc,
                _logSvc);
        }

        private static void AssertSuccess(string json)
        {
            Assert.Equal(0, (int)JObject.Parse(json)["code"]!);
        }

        private static void AssertError(string json, string expectedMsg)
        {
            var obj = JObject.Parse(json);
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal(expectedMsg, (string)obj["msg"]!);
        }

        // ============ SysRoleController.Grid ============

        [Fact]
        public async Task Role_Grid_HasData_ReturnsAllRoles()
        {
            await _fsql.Insert(new List<Sys_role>
            {
                NewRole("R1", "管理员", sort: 10),
                NewRole("R2", "员工", sort: 20)
            }).ExecuteAffrowsAsync();

            var json = await CreateRoleController().Grid(TestControllerHelper.BuildPageView<Sys_role>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            // 按 RoleSort 排序
            var data = (JArray)obj["data"]!;
            Assert.Equal("R1", (string)data[0]["id"]!);
            Assert.Equal("R2", (string)data[1]["id"]!);
        }

        // ============ SysRoleController.Save ============

        [Fact]
        public async Task Role_Save_New_AddsRow()
        {
            var json = await CreateRoleController().Save(new Sys_role
            {
                RoleName = "客服",
                RoleSort = 30
            });

            AssertSuccess(json);

            var rows = await _fsql.Select<Sys_role>()
                .Where(a => a.RoleName == "客服").ToListAsync();
            Assert.Single(rows);
            Assert.False(string.IsNullOrEmpty(rows[0].id));
        }

        [Fact]
        public async Task Role_Save_Edit_UpdatesRow_AndWritesModifyLog()
        {
            await _fsql.Insert(NewRole("R1", "旧角色", sort: 10)).ExecuteAffrowsAsync();

            var json = await CreateRoleController().Save(new Sys_role
            {
                id = "R1",
                RoleName = "新角色",
                RoleSort = 15
            });

            AssertSuccess(json);

            var role = await _fsql.Select<Sys_role>()
                .Where(a => a.id == "R1").FirstAsync();
            Assert.Equal("新角色", role.RoleName);
            Assert.Equal(15, role.RoleSort);

            var logs = await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "[角色]修改" && a.EventID == "R1").ToListAsync();
            Assert.Single(logs);
            Assert.Equal("新角色", logs[0].EventTitle);
        }

        [Fact]
        public async Task Role_Save_Edit_NotFound_ReturnsError()
        {
            var json = await CreateRoleController().Save(new Sys_role
            {
                id = "NOPE",
                RoleName = "不存在"
            });

            AssertError(json, "找不到数据！");
        }

        // ============ SysRoleController.Del ============

        [Fact]
        public async Task Role_Del_HasEmployee_ReturnsError()
        {
            await _fsql.Insert(NewRole("R1", "管理员")).ExecuteAffrowsAsync();
            var inRole = NewEmp("E1", "u1", "张三");
            inRole.role_id = "R1";
            await _fsql.Insert(inRole).ExecuteAffrowsAsync();

            var json = await CreateRoleController().Del("R1");

            AssertError(json, "此角色下有员工，不能删除！");
            Assert.Equal(1, (int)await _fsql.Select<Sys_role>().CountAsync());
        }

        [Fact]
        public async Task Role_Del_HappyPath_DeletesRow_AndWritesDeleteLog()
        {
            await _fsql.Insert(NewRole("R1", "管理员")).ExecuteAffrowsAsync();

            var json = await CreateRoleController().Del("R1");

            AssertSuccess(json);
            Assert.Equal(0, (int)await _fsql.Select<Sys_role>().CountAsync());

            var logs = await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "[角色]删除" && a.EventID == "R1").ToListAsync();
            Assert.Single(logs);
            Assert.Equal("管理员", logs[0].EventTitle);
        }

        [Fact]
        public async Task Role_Del_NotFound_ReturnsError()
        {
            var json = await CreateRoleController().Del("NOPE");

            AssertError(json, "找不到此数据！");
        }

        // ============ SysRoleEmpController.Add ============

        [Fact]
        public async Task RoleEmp_Add_EmptyRoleId_ReturnsError()
        {
            var json = await CreateRoleEmpController().Add("", "E1");

            AssertError(json, "角色ID无效");
            Assert.Equal(0, (int)await _fsql.Select<Sys_role_emp>().CountAsync());
        }

        [Fact]
        public async Task RoleEmp_Add_EmptyEmpIds_ReturnsError()
        {
            var json = await CreateRoleEmpController().Add("R1", "");

            AssertError(json, "员工ID不能为空");
            Assert.Equal(0, (int)await _fsql.Select<Sys_role_emp>().CountAsync());
        }

        [Fact]
        public async Task RoleEmp_Add_OnlyCommas_ReturnsError()
        {
            // TrimEnd(',') 后 Split 去空 → 列表为空
            var json = await CreateRoleEmpController().Add("R1", ",,");

            AssertError(json, "员工ID列表为空");
            Assert.Equal(0, (int)await _fsql.Select<Sys_role_emp>().CountAsync());
        }

        [Fact]
        public async Task RoleEmp_Add_ExpiredUser_ReturnsError()
        {
            var ctrl = CreateRoleEmpController(userId: "");

            var json = await ctrl.Add("R1", "E1");

            AssertError(json, "登录状态已过期");
            Assert.Equal(0, (int)await _fsql.Select<Sys_role_emp>().CountAsync());
        }

        [Fact]
        public async Task RoleEmp_Add_HappyPath_PersistsRows_AndWritesLog()
        {
            await _fsql.Insert(NewRole("R1", "管理员")).ExecuteAffrowsAsync();
            await _fsql.Insert(new List<hr_employee>
            {
                NewEmp("E1", "u1", "张三"),
                NewEmp("E2", "u2", "李四")
            }).ExecuteAffrowsAsync();

            // 末尾带逗号也要正确解析
            var json = await CreateRoleEmpController().Add("R1", "E1,E2,");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal("添加成功", (string)obj["msg"]!);

            // 中间表真实落库
            var rows = await _fsql.Select<Sys_role_emp>()
                .Where(a => a.role_id == "R1").ToListAsync();
            Assert.Equal(2, rows.Count);
            var empIds = rows.Select(r => r.emp_id).ToHashSet();
            Assert.Contains("E1", empIds);
            Assert.Contains("E2", empIds);

            // 写权限人员调整日志
            var logs = await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "权限人员调整" && a.EventID == "R1").ToListAsync();
            Assert.Single(logs);
        }

        [Fact]
        public async Task RoleEmp_Add_WithWhitespace_TrimsIds()
        {
            await _fsql.Insert(new List<hr_employee>
            {
                NewEmp("E1", "u1", "张三"),
                NewEmp("E2", "u2", "李四")
            }).ExecuteAffrowsAsync();

            var json = await CreateRoleEmpController().Add("R1", " E1 , E2 ");

            AssertSuccess(json);

            var empIds = (await _fsql.Select<Sys_role_emp>()
                .Where(a => a.role_id == "R1").ToListAsync())
                .Select(r => r.emp_id).ToHashSet();
            Assert.Equal(2, empIds.Count);
            Assert.Contains("E1", empIds);
            Assert.Contains("E2", empIds);
        }

        // ============ SysRoleEmpController.Remove ============

        [Fact]
        public async Task RoleEmp_Remove_HappyPath_DeletesRows()
        {
            await _fsql.Insert(new List<Sys_role_emp>
            {
                NewRoleEmp("R1", "E1"),
                NewRoleEmp("R1", "E2"),
                NewRoleEmp("R1", "E3")
            }).ExecuteAffrowsAsync();

            var json = await CreateRoleEmpController().Remove("R1", "E1,E2");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal("移除成功", (string)obj["msg"]!);

            // 真实删除：只剩 E3
            var remain = await _fsql.Select<Sys_role_emp>()
                .Where(a => a.role_id == "R1").ToListAsync();
            Assert.Single(remain);
            Assert.Equal("E3", remain[0].emp_id);

            // 移除也写权限人员调整日志
            var logs = await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "权限人员调整" && a.EventID == "R1").CountAsync();
            Assert.Equal(1, (int)logs);
        }

        // ============ SysRoleEmpController.Emplist ============

        /// <summary>
        /// ⚠ 本测试只固化**当前真实行为**，不按方法名/注释猜语义。
        /// Sys_role_empService.GetEmployeesNotInRoleAsync 把 GetEmpIdsNotInRoleAsync
        /// （不在角色下的员工）当作 excludeSet，再 !excludeSet.Contains(id) —— 双重否定后
        /// Emplist 实际返回的是**该角色的成员**，与 Controller 注释/A 版 NOT IN 语义相反。
        /// 这是产品层缺陷（不在本测试修复范围），已上报；若 Service 修复，本测试需同步翻转。
        /// </summary>
        [Fact]
        public async Task RoleEmp_Emplist_CurrentSemantics_ReturnsRoleMembers()
        {
            await _fsql.Insert(new List<hr_employee>
            {
                NewEmp("E1", "u1", "张三"),
                NewEmp("E2", "u2", "李四"),
                NewEmp("E3", "u3", "王五"),
                NewEmp("E_ADMIN", "admin", "超管")
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new List<Sys_role_emp>
            {
                NewRoleEmp("R1", "E1"),
                NewRoleEmp("R1", "E2")
            }).ExecuteAffrowsAsync();

            var json = await CreateRoleEmpController().Emplist("R1", null);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            // 当前实现：返回角色成员 E1/E2（admin 恒排除）
            Assert.Equal(2, (int)obj["count"]!);
            var ids = ((JArray)obj["data"]!).Select(d => (string)d["id"]!).ToHashSet();
            Assert.Contains("E1", ids);
            Assert.Contains("E2", ids);
            Assert.DoesNotContain("E3", ids);
            Assert.DoesNotContain("E_ADMIN", ids);
        }

        [Fact]
        public async Task RoleEmp_Emplist_EmptyRoleId_ReturnsAllNonAdmin()
        {
            await _fsql.Insert(new List<hr_employee>
            {
                NewEmp("E1", "u1", "张三"),
                NewEmp("E2", "u2", "李四"),
                NewEmp("E_ADMIN", "admin", "超管")
            }).ExecuteAffrowsAsync();

            // role_id 为空 → 语义上无"该角色"，返回全员（除 admin）作为候选
            var json = await CreateRoleEmpController().Emplist(null, null);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var ids = ((JArray)obj["data"]!).Select(d => (string)d["id"]!).ToHashSet();
            Assert.Contains("E1", ids);
            Assert.Contains("E2", ids);
            Assert.DoesNotContain("E_ADMIN", ids);
        }

        // ============ SysRoleEmpController.Get（IN 语义）============

        [Fact]
        public async Task RoleEmp_Get_ReturnsOnlyRoleMembers_ExcludesAdmin()
        {
            await _fsql.Insert(new List<hr_employee>
            {
                NewEmp("E1", "u1", "张三"),
                NewEmp("E2", "u2", "李四"),
                NewEmp("E3", "u3", "王五"),
                NewEmp("E_ADMIN", "admin", "超管")
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new List<Sys_role_emp>
            {
                NewRoleEmp("R1", "E1"),
                NewRoleEmp("R1", "E2")
            }).ExecuteAffrowsAsync();

            var json = await CreateRoleEmpController().Get("R1", null);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var ids = ((JArray)obj["data"]!).Select(d => (string)d["id"]!).ToHashSet();
            Assert.Contains("E1", ids);
            Assert.Contains("E2", ids);
            Assert.DoesNotContain("E3", ids);
            Assert.DoesNotContain("E_ADMIN", ids);
        }

        // ============ DataAuthConfigController.Grid ============

        [Fact]
        public async Task DataAuth_Grid_ReturnsRolesWithAuthLevel()
        {
            await _fsql.Insert(new List<Sys_role>
            {
                NewRole("R1", "管理员", dataAuth: 2, sort: 10),
                NewRole("R2", "员工", dataAuth: null, sort: 20)
            }).ExecuteAffrowsAsync();

            var json = await CreateDataAuthController().Grid();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(2, data.Count);
            Assert.Equal("R1", (string)data[0]["id"]!);
            Assert.Equal(2, (int)data[0]["DataAuth"]!);
            // DataAuth 为 null 时输出 0
            Assert.Equal(0, (int)data[1]["DataAuth"]!);
        }

        // ============ DataAuthConfigController.Save ============

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task DataAuth_Save_ValidLevel_UpdatesRole(int level)
        {
            await _fsql.Insert(NewRole("R1", "管理员", dataAuth: 1)).ExecuteAffrowsAsync();

            var json = await CreateDataAuthController().Save("R1", level);

            AssertSuccess(json);

            var role = await _fsql.Select<Sys_role>()
                .Where(a => a.id == "R1").FirstAsync();
            Assert.Equal(level, role.DataAuth);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        public async Task DataAuth_Save_OutOfRangeLevel_ReturnsError(int level)
        {
            await _fsql.Insert(NewRole("R1", "管理员", dataAuth: 1)).ExecuteAffrowsAsync();

            var json = await CreateDataAuthController().Save("R1", level);

            AssertError(json, "参数错误：DataAuth 必须是 0-4 之间的整数");

            // 未被改动
            var role = await _fsql.Select<Sys_role>()
                .Where(a => a.id == "R1").FirstAsync();
            Assert.Equal(1, role.DataAuth);
        }

        [Fact]
        public async Task DataAuth_Save_NullLevel_ReturnsError()
        {
            await _fsql.Insert(NewRole("R1", "管理员", dataAuth: 1)).ExecuteAffrowsAsync();

            var json = await CreateDataAuthController().Save("R1", null);

            AssertError(json, "参数错误：DataAuth 必须是 0-4 之间的整数");
        }

        [Fact]
        public async Task DataAuth_Save_EmptyRoleId_ReturnsError()
        {
            var json = await CreateDataAuthController().Save("", 3);

            AssertError(json, "参数错误：role_id 不能为空");
        }

        [Fact]
        public async Task DataAuth_Save_RoleNotFound_ReturnsError()
        {
            var json = await CreateDataAuthController().Save("NOPE", 3);

            AssertError(json, "找不到此角色！");
        }

        [Fact]
        public async Task DataAuth_Save_LogWrittenOnlyWhenAuthChanged()
        {
            await _fsql.Insert(NewRole("R1", "管理员", dataAuth: 1)).ExecuteAffrowsAsync();
            var ctrl = CreateDataAuthController();

            // 层级未变 → 不写日志
            AssertSuccess(await ctrl.Save("R1", 1));
            Assert.Equal(0, (int)await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "[数据权限]修改").CountAsync());

            // 层级变化 → 写审计日志
            AssertSuccess(await ctrl.Save("R1", 4));

            var logs = await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "[数据权限]修改" && a.EventID == "R1").ToListAsync();
            Assert.Single(logs);
            Assert.Contains("数据权限：1→4", logs[0].Log_Content);

            var role = await _fsql.Select<Sys_role>()
                .Where(a => a.id == "R1").FirstAsync();
            Assert.Equal(4, role.DataAuth);
        }
    }
}
