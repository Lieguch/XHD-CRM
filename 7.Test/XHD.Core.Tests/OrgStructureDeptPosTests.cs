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
    /// 组织架构线（一）：HrDepartmentController + HrPositionController。
    /// 全部走真实 SQLite 内存库 + 真实 Repository/Service，只 Mock ILogger 与 IDBAuthService。
    /// 覆盖：Grid/Combo/ComboTree 读写态、Save 新增/编辑/权限闸门/自引用/找不到数据、
    /// Del 的下级保护/员工保护/找不到数据/权限闸门/成功落库+日志。
    /// </summary>
    public class OrgStructureDeptPosTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Ihr_departmentService _deptSvc;
        private readonly Ihr_positionService _posSvc;
        private readonly Ihr_employeeService _empSvc;
        private readonly ISys_logService _logSvc;

        public OrgStructureDeptPosTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _deptSvc = new hr_departmentService(new hr_departmentRepository(_fsql));
            _posSvc = new hr_positionService(new hr_positionRepository(_fsql));
            _empSvc = new hr_employeeService(new hr_employeeRepository(_fsql));
            _logSvc = new Sys_logService(new Sys_logRepository(_fsql));
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static hr_department NewDept(string id, string name, string parentid = "root", int order = 1)
        {
            return new hr_department
            {
                id = id,
                dep_name = name,
                parentid = parentid,
                dep_order = order,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        private static hr_position NewPos(string id, string name, string level = "L1", int order = 1)
        {
            return new hr_position
            {
                id = id,
                position_name = name,
                position_level = level,
                position_order = order,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        private static hr_employee NewEmp(string id, string uid, string name,
            string depId = "", string positionId = "")
        {
            return new hr_employee
            {
                id = id,
                uid = uid,
                name = name,
                dep_id = depId,
                position_id = positionId,
                isDelete = 0,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        // ============ Controller 装配辅助 ============

        /// <summary>
        /// 权限 mock：GetDataAuth 放行全公司 + GetAuth 按 grantAuth 开关。
        /// 两者必须同时设置，否则 Save 的 GetAuth 闸门会被 Moq 默认 false 误判。
        /// </summary>
        private static Mock<IDBAuthService> CreateAuthMock(bool grantAuth)
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 5, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(grantAuth);
            return auth;
        }

        private HrDepartmentController CreateDeptController(bool grantAuth = true, string userId = "TEST_USER")
        {
            var auth = CreateAuthMock(grantAuth);
            return TestControllerHelper.CreateWithHttpContext<HrDepartmentController>(
                string.Empty, userId, "Test User",
                new Mock<ILogger<HrDepartmentController>>().Object,
                _deptSvc,
                _empSvc,
                _logSvc,
                auth.Object);
        }

        private HrPositionController CreatePosController(bool grantAuth = true, string userId = "TEST_USER")
        {
            var auth = CreateAuthMock(grantAuth);
            return TestControllerHelper.CreateWithHttpContext<HrPositionController>(
                string.Empty, userId, "Test User",
                new Mock<ILogger<HrPositionController>>().Object,
                _posSvc,
                _empSvc,
                _logSvc,
                auth.Object);
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

        // ============ HrDepartmentController.Grid ============

        [Fact]
        public async Task Dept_Grid_HasData_ReturnsAllDepartments()
        {
            await _fsql.Insert(new List<hr_department>
            {
                NewDept("D_ROOT", "总公司"),
                NewDept("D_CHILD", "研发部", parentid: "D_ROOT", order: 2)
            }).ExecuteAffrowsAsync();

            var json = await CreateDeptController().Grid();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var ids = ((JArray)obj["data"]!).Select(d => (string)d["id"]!).ToHashSet();
            Assert.Contains("D_ROOT", ids);
            Assert.Contains("D_CHILD", ids);
        }

        [Fact]
        public async Task Dept_Grid_EmptyTable_ReturnsEmpty()
        {
            var json = await CreateDeptController().Grid();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        // ============ HrDepartmentController.Combo / ComboTree ============

        [Fact]
        public async Task Dept_Combo_BuildsTwoLevelTree()
        {
            await _fsql.Insert(new List<hr_department>
            {
                NewDept("D_ROOT", "总公司"),
                NewDept("D_CHILD", "研发部", parentid: "D_ROOT")
            }).ExecuteAffrowsAsync();

            var json = await CreateDeptController().Combo();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("D_ROOT", (string)data[0]["id"]!);
            // 下级挂到 children
            var children = (JArray)data[0]["children"]!;
            Assert.Single(children);
            Assert.Equal("D_CHILD", (string)children[0]["id"]!);
        }

        [Fact]
        public async Task Dept_ComboTree_ExcludesSelf_PrependsRootNode()
        {
            await _fsql.Insert(new List<hr_department>
            {
                NewDept("D_ROOT", "总公司"),
                NewDept("D_CHILD", "研发部", parentid: "D_ROOT")
            }).ExecuteAffrowsAsync();

            // 求 D_CHILD 的上级候选：排除自己，且头部补一个"无"
            var json = await CreateDeptController().ComboTree("D_CHILD");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(2, data.Count);
            Assert.Equal("root", (string)data[0]["id"]!);
            Assert.Equal("无", (string)data[0]["title"]!);
            Assert.Equal("D_ROOT", (string)data[1]["id"]!);
            // D_CHILD 被排除
            Assert.DoesNotContain(data, d => (string)d["id"]! == "D_CHILD");
        }

        // ============ HrDepartmentController.Save（新增分支）============

        [Fact]
        public async Task Dept_Save_New_AddsRow_AndReturnsAffectedRows()
        {
            var ctrl = CreateDeptController();

            // 新增分支返回的是 AddAsync 影响行数（"1"），不是 XHDResult JSON
            var json = await ctrl.Save(new hr_department
            {
                dep_name = "财务部",
                parentid = "root",
                dep_order = 5
            });

            Assert.Equal("1", json.Trim());

            var rows = await _fsql.Select<hr_department>()
                .Where(a => a.dep_name == "财务部").ToListAsync();
            Assert.Single(rows);
            Assert.Equal("root", rows[0].parentid);
            // id 由后端 UUID 生成，不能是空
            Assert.False(string.IsNullOrEmpty(rows[0].id));
        }

        [Fact]
        public async Task Dept_Save_New_NoPermission_ReturnsError()
        {
            var ctrl = CreateDeptController(grantAuth: false);

            var json = await ctrl.Save(new hr_department { dep_name = "财务部" });

            AssertError(json, "无权限！");
            Assert.Equal(0, (int)await _fsql.Select<hr_department>().CountAsync());
        }

        // ============ HrDepartmentController.Save（编辑分支）============

        [Fact]
        public async Task Dept_Save_Edit_UpdatesRow_AndWritesModifyLog()
        {
            await _fsql.Insert(NewDept("DEPT1", "旧部门", order: 1)).ExecuteAffrowsAsync();

            var json = await CreateDeptController().Save(new hr_department
            {
                id = "DEPT1",
                dep_name = "新部门",
                parentid = "root",
                dep_order = 2
            });

            AssertSuccess(json);

            var dept = await _fsql.Select<hr_department>()
                .Where(a => a.id == "DEPT1").FirstAsync();
            Assert.Equal("新部门", dept.dep_name);
            Assert.Equal(2, dept.dep_order);

            // 实体有变化时应写 [部门]修改 日志
            var logs = await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "[部门]修改" && a.EventID == "DEPT1").ToListAsync();
            Assert.Single(logs);
            Assert.Equal("新部门", logs[0].EventTitle);
            Assert.Contains("dep_name", logs[0].Log_Content);
        }

        [Fact]
        public async Task Dept_Save_Edit_ParentIsSelf_ReturnsError()
        {
            await _fsql.Insert(NewDept("DEPT1", "部门A")).ExecuteAffrowsAsync();

            var json = await CreateDeptController().Save(new hr_department
            {
                id = "DEPT1",
                dep_name = "部门A",
                parentid = "DEPT1"
            });

            AssertError(json, "上级不能是自己！");

            // 原数据未被改动
            var dept = await _fsql.Select<hr_department>()
                .Where(a => a.id == "DEPT1").FirstAsync();
            Assert.Equal("root", dept.parentid);
        }

        [Fact]
        public async Task Dept_Save_Edit_NotFound_ReturnsError()
        {
            var json = await CreateDeptController().Save(new hr_department
            {
                id = "NOPE",
                dep_name = "不存在",
                parentid = "root"
            });

            AssertError(json, "找不到数据！");
        }

        [Fact]
        public async Task Dept_Save_Edit_NoPermission_ReturnsError()
        {
            await _fsql.Insert(NewDept("DEPT1", "部门A")).ExecuteAffrowsAsync();

            var json = await CreateDeptController(grantAuth: false).Save(new hr_department
            {
                id = "DEPT1",
                dep_name = "改名",
                parentid = "root"
            });

            AssertError(json, "无权限！");

            var dept = await _fsql.Select<hr_department>()
                .Where(a => a.id == "DEPT1").FirstAsync();
            Assert.Equal("部门A", dept.dep_name);
        }

        // ============ HrDepartmentController.Del ============

        [Fact]
        public async Task Dept_Del_HasChild_ReturnsError()
        {
            await _fsql.Insert(new List<hr_department>
            {
                NewDept("D_ROOT", "总公司"),
                NewDept("D_CHILD", "研发部", parentid: "D_ROOT")
            }).ExecuteAffrowsAsync();

            var json = await CreateDeptController().Del("D_ROOT");

            AssertError(json, "此部门下含有下级，不能删除！");
            Assert.Equal(2, (int)await _fsql.Select<hr_department>().CountAsync());
        }

        [Fact]
        public async Task Dept_Del_HasEmployee_ReturnsError()
        {
            await _fsql.Insert(NewDept("D_ROOT", "总公司")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewEmp("E1", "u1", "张三", depId: "D_ROOT")).ExecuteAffrowsAsync();

            var json = await CreateDeptController().Del("D_ROOT");

            AssertError(json, "此部门下有员工，不能删除！");
            Assert.Equal(1, (int)await _fsql.Select<hr_department>().CountAsync());
        }

        [Fact]
        public async Task Dept_Del_HappyPath_DeletesRow_AndWritesDeleteLog()
        {
            await _fsql.Insert(NewDept("D_ROOT", "总公司")).ExecuteAffrowsAsync();

            var json = await CreateDeptController().Del("D_ROOT");

            AssertSuccess(json);
            Assert.Equal(0, (int)await _fsql.Select<hr_department>().CountAsync());

            var logs = await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "[部门]删除" && a.EventID == "D_ROOT").ToListAsync();
            Assert.Single(logs);
            Assert.Equal("总公司", logs[0].EventTitle);
        }

        [Fact]
        public async Task Dept_Del_NotFound_ReturnsError()
        {
            var json = await CreateDeptController().Del("NOPE");

            AssertError(json, "找不到此数据！");
        }

        [Fact]
        public async Task Dept_Del_NoPermission_ReturnsError()
        {
            await _fsql.Insert(NewDept("D_ROOT", "总公司")).ExecuteAffrowsAsync();

            var json = await CreateDeptController(grantAuth: false).Del("D_ROOT");

            AssertError(json, "无权限！");
            Assert.Equal(1, (int)await _fsql.Select<hr_department>().CountAsync());
        }

        // ============ HrPositionController.Grid / Combo ============

        [Fact]
        public async Task Pos_Grid_HasData_ReturnsAllPositions()
        {
            await _fsql.Insert(new List<hr_position>
            {
                NewPos("P1", "销售经理", level: "L2", order: 20),
                NewPos("P2", "销售总监", level: "L3", order: 10)
            }).ExecuteAffrowsAsync();

            var json = await CreatePosController().Grid(TestControllerHelper.BuildPageView<hr_position>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            // 按 position_level 排序：L2 在前
            var data = (JArray)obj["data"]!;
            Assert.Equal("L2", (string)data[0]["position_level"]!);
        }

        [Fact]
        public async Task Pos_Combo_ReturnsIdTextArray()
        {
            await _fsql.Insert(new List<hr_position>
            {
                NewPos("P1", "销售经理", level: "L1"),
                NewPos("P2", "销售总监", level: "L2")
            }).ExecuteAffrowsAsync();

            // Combo 返回的是裸 JArray（非 XHDResult 包装），按 position_level 排序
            var arr = JArray.Parse(await CreatePosController().Combo());

            Assert.Equal(2, arr.Count);
            Assert.Equal("P1", (string)arr[0]["id"]!);
            Assert.Equal("销售经理", (string)arr[0]["text"]!);
            Assert.Equal("P2", (string)arr[1]["id"]!);
            Assert.Equal("销售总监", (string)arr[1]["text"]!);
        }

        // ============ HrPositionController.Save ============

        [Fact]
        public async Task Pos_Save_New_AddsRow()
        {
            var json = await CreatePosController().Save(new hr_position
            {
                position_name = "产品经理",
                position_level = "L2",
                position_order = 15
            });

            AssertSuccess(json);

            var rows = await _fsql.Select<hr_position>()
                .Where(a => a.position_name == "产品经理").ToListAsync();
            Assert.Single(rows);
            Assert.False(string.IsNullOrEmpty(rows[0].id));
        }

        [Fact]
        public async Task Pos_Save_New_NoPermission_ReturnsError()
        {
            var json = await CreatePosController(grantAuth: false)
                .Save(new hr_position { position_name = "产品经理" });

            AssertError(json, "无权限！");
            Assert.Equal(0, (int)await _fsql.Select<hr_position>().CountAsync());
        }

        [Fact]
        public async Task Pos_Save_Edit_UpdatesRow_AndWritesModifyLog()
        {
            await _fsql.Insert(NewPos("P1", "销售经理", level: "L2", order: 20))
                .ExecuteAffrowsAsync();

            var json = await CreatePosController().Save(new hr_position
            {
                id = "P1",
                position_name = "销售专家",
                position_level = "L3",
                position_order = 30
            });

            AssertSuccess(json);

            var pos = await _fsql.Select<hr_position>()
                .Where(a => a.id == "P1").FirstAsync();
            Assert.Equal("销售专家", pos.position_name);
            Assert.Equal("L3", pos.position_level);

            var logs = await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "[职务]修改" && a.EventID == "P1").ToListAsync();
            Assert.Single(logs);
            Assert.Equal("销售专家", logs[0].EventTitle);
        }

        [Fact]
        public async Task Pos_Save_Edit_NotFound_ReturnsError()
        {
            var json = await CreatePosController().Save(new hr_position
            {
                id = "NOPE",
                position_name = "不存在"
            });

            AssertError(json, "找不到数据！");
        }

        [Fact]
        public async Task Pos_Save_Edit_NoPermission_ReturnsError()
        {
            await _fsql.Insert(NewPos("P1", "销售经理")).ExecuteAffrowsAsync();

            var json = await CreatePosController(grantAuth: false).Save(new hr_position
            {
                id = "P1",
                position_name = "改名"
            });

            AssertError(json, "无权限！");

            var pos = await _fsql.Select<hr_position>()
                .Where(a => a.id == "P1").FirstAsync();
            Assert.Equal("销售经理", pos.position_name);
        }

        // ============ HrPositionController.Delete ============

        [Fact]
        public async Task Pos_Del_HasEmployee_ReturnsError()
        {
            await _fsql.Insert(NewPos("P1", "销售经理")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewEmp("E1", "u1", "张三", positionId: "P1")).ExecuteAffrowsAsync();

            var json = await CreatePosController().Delete("P1");

            AssertError(json, "此职务下有员工，不能删除！");
            Assert.Equal(1, (int)await _fsql.Select<hr_position>().CountAsync());
        }

        [Fact]
        public async Task Pos_Del_HappyPath_DeletesRow_AndWritesDeleteLog()
        {
            await _fsql.Insert(NewPos("P1", "销售经理")).ExecuteAffrowsAsync();

            var json = await CreatePosController().Delete("P1");

            AssertSuccess(json);
            Assert.Equal(0, (int)await _fsql.Select<hr_position>().CountAsync());

            var logs = await _fsql.Select<Sys_log>()
                .Where(a => a.EventType == "[职务]删除" && a.EventID == "P1").ToListAsync();
            Assert.Single(logs);
            Assert.Equal("销售经理", logs[0].EventTitle);
        }

        [Fact]
        public async Task Pos_Del_NotFound_ReturnsError()
        {
            var json = await CreatePosController().Delete("NOPE");

            AssertError(json, "找不到此数据！");
        }
    }
}
