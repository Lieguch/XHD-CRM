using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
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
    /// 组织架构线（三）：SysAuthController + SysMenuController。
    /// 全部走真实 SQLite 内存库 + 真实 Repository/Service，只 Mock ILogger。
    /// 覆盖：权限树 Grid（菜单+按钮+auth_on 标记）、save 的表单参数解析与
    /// Auth_type（GUID→1 / 非 GUID→2）、菜单 Grid/Tree/Save（按钮先清后加）/Delete 级联。
    /// </summary>
    public class OrgStructureAuthMenuTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly ISys_MenuService _menuSvc;
        private readonly ISys_ButtonService _btnSvc;
        private readonly ISys_authorityService _authSvc;

        public OrgStructureAuthMenuTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _menuSvc = new Sys_MenuService(new Sys_MenuRepository(_fsql));
            _btnSvc = new Sys_ButtonService(new Sys_ButtonRepository(_fsql));
            _authSvc = new Sys_authorityService(new Sys_authorityRepository(_fsql));
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static Sys_Menu NewMenu(string id, string name, string parentid = "root", int order = 1)
        {
            return new Sys_Menu
            {
                id = id,
                Menu_name = name,
                Menu_url = $"/{id}",
                Menu_icon = "fa-cog",
                Menu_order = order,
                parentid = parentid
            };
        }

        private static Sys_Button NewBtn(string id, string menuId, string name, int order)
        {
            return new Sys_Button
            {
                id = id,
                Menu_id = menuId,
                Btn_name = name,
                Btn_order = order,
                Btn_handler = name,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        private static Sys_authority NewAuth(string id, string roleId, string authId, int type = 1)
        {
            return new Sys_authority
            {
                id = id,
                Role_id = roleId,
                Auth_id = authId,
                Auth_type = type,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        // ============ Controller 装配辅助 ============

        private SysAuthController CreateAuthController(string queryString = "")
        {
            return TestControllerHelper.CreateWithHttpContext<SysAuthController>(
                queryString, "TEST_USER", "Test User",
                new Mock<ILogger<SysAuthController>>().Object,
                _menuSvc,
                _btnSvc,
                _authSvc);
        }

        private SysMenuController CreateMenuController()
        {
            return TestControllerHelper.CreateWithHttpContext<SysMenuController>(
                string.Empty, "TEST_USER", "Test User",
                new Mock<ILogger<SysMenuController>>().Object,
                _menuSvc,
                _btnSvc);
        }

        /// <summary>
        /// 给 Controller 挂一个表单（save 从 Request.Form 取 role_id/auth_id/auth_on；
        /// SysMenuController.Save 从 Request.Form 取 btn_* 开关）。
        /// </summary>
        private static void SetForm(Controller ctrl, IDictionary<string, string> form)
        {
            ctrl.HttpContext.Request.Form = new FormCollection(
                form.ToDictionary(kv => kv.Key, kv => new StringValues(kv.Value)));
        }

        private static void AssertSuccess(string json)
        {
            Assert.Equal(0, (int)JObject.Parse(json)["code"]!);
        }

        // ============ SysAuthController.Grid ============

        [Fact]
        public async Task Auth_Grid_BuildsTree_WithButtonsAndAuthFlags()
        {
            await _fsql.Insert(new List<Sys_Menu>
            {
                NewMenu("M1", "客户管理", order: 1),
                NewMenu("M2", "客户列表", parentid: "M1", order: 2)
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new List<Sys_Button>
            {
                NewBtn("B1", "M1", "新增", 10),
                NewBtn("B2", "M2", "删除", 20)
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new List<Sys_authority>
            {
                NewAuth("A1", "R1", "M1"),   // M1 菜单已授权
                NewAuth("A2", "R1", "B1")    // B1 按钮已授权
            }).ExecuteAffrowsAsync();

            var json = await CreateAuthController("?role_id=R1").Grid();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(2, data.Count);

            // M1：已授权，按钮 B1 已授权
            var m1 = data[0];
            Assert.Equal("M1", (string)m1["id"]!);
            Assert.Equal(1, (int)m1["auth_on"]!);
            var m1Btns = (JArray)m1["btn"]!;
            Assert.Single(m1Btns);
            Assert.Equal("B1", (string)m1Btns[0]["id"]!);
            Assert.Equal(1, (int)m1Btns[0]["auth_on"]!);

            // M2：未授权，按钮 B2 未授权
            var m2 = data[1];
            Assert.Equal("M2", (string)m2["id"]!);
            Assert.Equal(0, (int)m2["auth_on"]!);
            var m2Btns = (JArray)m2["btn"]!;
            Assert.Single(m2Btns);
            Assert.Equal(0, (int)m2Btns[0]["auth_on"]!);
        }

        [Fact]
        public async Task Auth_Grid_NoAuthorityRows_AllFlagsOff()
        {
            await _fsql.Insert(new List<Sys_Menu>
            {
                NewMenu("M1", "客户管理", order: 1)
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new List<Sys_Button> { NewBtn("B1", "M1", "新增", 10) }).ExecuteAffrowsAsync();
            // 该角色无任何授权记录
            await _fsql.Insert(new List<Sys_authority>
            {
                NewAuth("AX", "OTHER_ROLE", "M1")
            }).ExecuteAffrowsAsync();

            var json = await CreateAuthController("?role_id=R1").Grid();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal(0, (int)data[0]["auth_on"]!);
            Assert.Equal(0, (int)((JArray)data[0]["btn"]!)[0]["auth_on"]!);
        }

        // ============ SysAuthController.save ============

        [Fact]
        public async Task Auth_Save_AuthOn_WithGuid_SetsAuthType1()
        {
            var authId = Guid.NewGuid().ToString();
            var ctrl = CreateAuthController();
            SetForm(ctrl, new Dictionary<string, string>
            {
                { "role_id", "R1" },
                { "auth_id", authId },
                { "auth_on", "1" }
            });

            AssertSuccess(await ctrl.save());

            var rows = await _fsql.Select<Sys_authority>()
                .Where(a => a.Role_id == "R1").ToListAsync();
            Assert.Single(rows);
            Assert.Equal(authId, rows[0].Auth_id);
            Assert.Equal(1, rows[0].Auth_type);
        }

        [Fact]
        public async Task Auth_Save_AuthOn_WithNonGuid_SetsAuthType2()
        {
            // 按钮权限点形如 "菜单|按钮"，非 GUID → Auth_type=2
            var ctrl = CreateAuthController();
            SetForm(ctrl, new Dictionary<string, string>
            {
                { "role_id", "R1" },
                { "auth_id", "M1|add" },
                { "auth_on", "1" }
            });

            AssertSuccess(await ctrl.save());

            var rows = await _fsql.Select<Sys_authority>()
                .Where(a => a.Role_id == "R1").ToListAsync();
            Assert.Single(rows);
            Assert.Equal("M1|add", rows[0].Auth_id);
            Assert.Equal(2, rows[0].Auth_type);
        }

        [Fact]
        public async Task Auth_Save_AuthOn_ReplacesExisting_Row()
        {
            // 角色已拥有该权限点 → save 先按 (role_id, auth_id) 删除再新增，不重复
            await _fsql.Insert(NewAuth("A_OLD", "R1", "M1|add", type: 2)).ExecuteAffrowsAsync();

            var ctrl = CreateAuthController();
            SetForm(ctrl, new Dictionary<string, string>
            {
                { "role_id", "R1" },
                { "auth_id", "M1|add" },
                { "auth_on", "1" }
            });

            AssertSuccess(await ctrl.save());

            var rows = await _fsql.Select<Sys_authority>()
                .Where(a => a.Role_id == "R1" && a.Auth_id == "M1|add").ToListAsync();
            Assert.Single(rows);
        }

        [Fact]
        public async Task Auth_Save_AuthOff_DeletesRow()
        {
            await _fsql.Insert(new List<Sys_authority>
            {
                NewAuth("A1", "R1", "M1|add", type: 2),
                NewAuth("A2", "R1", "M1|edit", type: 2)
            }).ExecuteAffrowsAsync();

            var ctrl = CreateAuthController();
            SetForm(ctrl, new Dictionary<string, string>
            {
                { "role_id", "R1" },
                { "auth_id", "M1|add" },
                { "auth_on", "0" }
            });

            AssertSuccess(await ctrl.save());

            var rows = await _fsql.Select<Sys_authority>()
                .Where(a => a.Role_id == "R1").ToListAsync();
            Assert.Single(rows);
            Assert.Equal("M1|edit", rows[0].Auth_id);
        }

        // ============ SysMenuController.Grid / Tree ============

        [Fact]
        public async Task Menu_Grid_BuildsTreeWithButtons()
        {
            await _fsql.Insert(new List<Sys_Menu>
            {
                NewMenu("M1", "客户管理", order: 1),
                NewMenu("M2", "客户列表", parentid: "M1", order: 2)
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new List<Sys_Button>
            {
                NewBtn("B1", "M1", "新增", 10),
                NewBtn("B2", "M2", "删除", 20)
            }).ExecuteAffrowsAsync();

            var json = await CreateMenuController().Grid();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(2, data.Count);
            Assert.Equal("M1", (string)data[0]["id"]!);
            Assert.Equal("客户管理", (string)data[0]["title"]!);
            // 按钮按菜单归组
            Assert.Single((JArray)data[0]["btn"]!);
            Assert.Single((JArray)data[1]["btn"]!);
        }

        [Fact]
        public async Task Menu_Tree_ReturnsHierarchy()
        {
            await _fsql.Insert(new List<Sys_Menu>
            {
                NewMenu("M1", "客户管理", order: 1),
                NewMenu("M2", "客户列表", parentid: "M1", order: 2)
            }).ExecuteAffrowsAsync();

            // Tree 返回裸 JArray（服务层直出）
            var arr = JArray.Parse(await CreateMenuController().Tree());

            Assert.Single(arr);
            var top = arr[0];
            Assert.Equal("M1", (string)top["id"]!);
            Assert.Equal(1, (int)top["isMenu"]!);
            var children = (JArray)top["children"]!;
            Assert.Single(children);
            Assert.Equal("M2", (string)children[0]["id"]!);
        }

        // ============ SysMenuController.Save ============

        [Fact]
        public async Task Menu_Save_New_AddsMenu_WithoutButtons()
        {
            var ctrl = CreateMenuController();
            SetForm(ctrl, new Dictionary<string, string>());

            var json = await ctrl.Save(new Sys_Menu
            {
                id = "M_NEW",
                Menu_name = "新菜单",
                parentid = "root",
                Menu_order = 5
            });

            AssertSuccess(json);

            var menu = await _fsql.Select<Sys_Menu>()
                .Where(a => a.id == "M_NEW").FirstAsync();
            Assert.Equal("新菜单", menu.Menu_name);

            // 未勾选任何按钮 → 不产生按钮记录
            Assert.Equal(0, (int)await _fsql.Select<Sys_Button>()
                .Where(a => a.Menu_id == "M_NEW").CountAsync());
        }

        [Fact]
        public async Task Menu_Save_WithButtons_InsertsButtonRows()
        {
            var ctrl = CreateMenuController();
            SetForm(ctrl, new Dictionary<string, string>
            {
                { "btn_add", "on" },
                { "btn_edit", "on" }
            });

            var json = await ctrl.Save(new Sys_Menu
            {
                id = "M_NEW",
                Menu_name = "新菜单",
                parentid = "root",
                Menu_order = 5
            });

            AssertSuccess(json);

            var btns = await _fsql.Select<Sys_Button>()
                .Where(a => a.Menu_id == "M_NEW")
                .OrderBy(a => a.Btn_order).ToListAsync();
            Assert.Equal(2, btns.Count);
            Assert.Equal("M_NEW|add", btns[0].id);
            Assert.Equal("新增", btns[0].Btn_name);
            Assert.Equal("M_NEW|edit", btns[1].id);
            Assert.Equal("修改", btns[1].Btn_name);
        }

        [Fact]
        public async Task Menu_Save_ExistingMenu_ReplacesButtons()
        {
            await _fsql.Insert(new List<Sys_Menu> { NewMenu("M1", "客户管理", order: 1) })
                .ExecuteAffrowsAsync();
            await _fsql.Insert(new List<Sys_Button>
            {
                NewBtn("M1|add", "M1", "新增", 10),
                NewBtn("M1|edit", "M1", "修改", 20),
                NewBtn("M1|del", "M1", "删除", 30)
            }).ExecuteAffrowsAsync();

            var ctrl = CreateMenuController();
            // 只保留"删除"按钮：先清理该菜单全部按钮，再按表单新增
            SetForm(ctrl, new Dictionary<string, string> { { "btn_del", "on" } });

            var json = await ctrl.Save(new Sys_Menu
            {
                id = "M1",
                Menu_name = "客户管理V2",
                parentid = "root",
                Menu_order = 2
            });

            AssertSuccess(json);

            var menu = await _fsql.Select<Sys_Menu>()
                .Where(a => a.id == "M1").FirstAsync();
            Assert.Equal("客户管理V2", menu.Menu_name);

            var btns = await _fsql.Select<Sys_Button>()
                .Where(a => a.Menu_id == "M1").ToListAsync();
            Assert.Single(btns);
            Assert.Equal("M1|del", btns[0].id);
            Assert.Equal(30, btns[0].Btn_order);
        }

        // ============ SysMenuController.Delete ============

        [Fact]
        public async Task Menu_Delete_RemovesMenuAndButtons()
        {
            await _fsql.Insert(new List<Sys_Menu> { NewMenu("M1", "客户管理", order: 1) })
                .ExecuteAffrowsAsync();
            await _fsql.Insert(new List<Sys_Button>
            {
                NewBtn("M1|add", "M1", "新增", 10),
                NewBtn("M1|edit", "M1", "修改", 20)
            }).ExecuteAffrowsAsync();
            // 其他菜单的按钮不能被误删
            await _fsql.Insert(new List<Sys_Button> { NewBtn("M2|add", "M2", "新增", 10) })
                .ExecuteAffrowsAsync();

            var json = await CreateMenuController().Delete("M1");

            AssertSuccess(json);

            Assert.Equal(0, (int)await _fsql.Select<Sys_Menu>()
                .Where(a => a.id == "M1").CountAsync());
            Assert.Equal(0, (int)await _fsql.Select<Sys_Button>()
                .Where(a => a.Menu_id == "M1").CountAsync());
            Assert.Equal(1, (int)await _fsql.Select<Sys_Button>()
                .Where(a => a.Menu_id == "M2").CountAsync());
        }
    }
}
