using FreeSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// 系统参数线端点测试（A 查询 + 唯一性校验 + B 落库）。
    /// 范式同 CustomerControllerTests：Mock service 桥接真实 repository + SQLite in-memory。
    /// </summary>
    public class SysParamTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sys_ParamRepository _paramRepo;
        private readonly CRM_CustomerRepository _custRepo;
        private readonly CRM_followRepository _followRepo;
        private readonly Sale_orderRepository _orderRepo;
        private readonly Finance_ReceiveRepository _receiveRepo;
        private readonly Finance_InvoiceRepository _invoiceRepo;
        private readonly Message_newsRepository _newsRepo;

        public SysParamTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _paramRepo = new Sys_ParamRepository(_fsql);
            _custRepo = new CRM_CustomerRepository(_fsql);
            _followRepo = new CRM_followRepository(_fsql);
            _orderRepo = new Sale_orderRepository(_fsql);
            _receiveRepo = new Finance_ReceiveRepository(_fsql);
            _invoiceRepo = new Finance_InvoiceRepository(_fsql);
            _newsRepo = new Message_newsRepository(_fsql);
        }

        public void Dispose() => _fsql?.Dispose();

        // ============ 测试数据工厂 ============

        private Task<int> InsertParam(string id, string name, string type, int order = 0)
            => _fsql.Insert(new Sys_Param
            {
                id = id,
                params_name = name,
                params_type = type,
                params_order = order,
                create_id = "SEED",
                create_time = new DateTime(2024, 1, 1)
            }).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        /// <summary>
        /// Mock ISys_ParamService 并桥接到真实仓储。
        /// 单参/双参 GridAsync 的 Service 契约返回 XHDData，仓储返回 List，
        /// 统一改用分页重载包一层（与 CustomerControllerTests 同款桥接技巧）。
        /// </summary>
        private static Mock<ISys_ParamService> CreateParamServiceMock(Sys_ParamRepository repo)
        {
            var mock = new Mock<ISys_ParamService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Sys_Param, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param, bool>>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<Sys_Param, bool>> e, int p, int l, string o) => repo.GridAsync(e, p, l, o));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param, bool>>>()))
                .Returns((Expression<Func<Sys_Param, bool>> e) => repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param, bool>>>(), It.IsAny<string>()))
                .Returns((Expression<Func<Sys_Param, bool>> e, string o) => repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.AddAsync(It.IsAny<Sys_Param>()))
                .Returns((Sys_Param m) => repo.AddAsync(m));
            mock.Setup(s => s.UpdateAsync(It.IsAny<Sys_Param>()))
                .Returns((Sys_Param m) => repo.UpdateAsync(m));
            mock.Setup(s => s.DeleteAsync(It.IsAny<string>()))
                .Returns((string id) => repo.DeleteAsync(id));
            mock.Setup(s => s.ValidateNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string name, string parentId, string excludeId) => repo.ValidateNameAsync(name, parentId, excludeId));
            return mock;
        }

        /// <summary>
        /// Delete 按 params_type 分支预筛的依赖服务统一桥接到真实仓储，
        /// 避免 loose mock 返回 null 导致 count 访问空引用。
        /// </summary>
        private static Mock<ICRM_CustomerService> CreateCustServiceMock(CRM_CustomerRepository repo)
        {
            var mock = new Mock<ICRM_CustomerService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<CRM_Customer, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            return mock;
        }

        private static Mock<ICRM_followService> CreateFollowServiceMock(CRM_followRepository repo)
        {
            var mock = new Mock<ICRM_followService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_follow, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<CRM_follow, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            return mock;
        }

        private static Mock<ISale_orderService> CreateOrderServiceMock(Sale_orderRepository repo)
        {
            var mock = new Mock<ISale_orderService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sale_order, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Sale_order, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            return mock;
        }

        private static Mock<IFinance_ReceiveService> CreateReceiveServiceMock(Finance_ReceiveRepository repo)
        {
            var mock = new Mock<IFinance_ReceiveService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Finance_Receive, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Finance_Receive, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            return mock;
        }

        private static Mock<IFinance_InvoiceService> CreateInvoiceServiceMock(Finance_InvoiceRepository repo)
        {
            var mock = new Mock<IFinance_InvoiceService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Finance_Invoice, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Finance_Invoice, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            return mock;
        }

        private static Mock<IMessage_newsService> CreateNewsServiceMock(Message_newsRepository repo)
        {
            var mock = new Mock<IMessage_newsService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Message_news, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Message_news, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            return mock;
        }

        private static Mock<ISys_logService> CreateLogMock()
        {
            var mock = new Mock<ISys_logService>();
            mock.Setup(l => l.UpdateLog(It.IsAny<Sys_log>())).ReturnsAsync(1);
            mock.Setup(l => l.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);
            return mock;
        }

        private SysParamController CreateController(
            Mock<ISys_ParamService>? paramSvc = null,
            Mock<ISys_logService>? logSvc = null,
            string queryString = "",
            string userId = "TEST_USER")
        {
            return TestControllerHelper.CreateWithHttpContext<SysParamController>(
                queryString, userId, "Test User",
                new Mock<ILogger<SysParamController>>().Object,
                (paramSvc ?? CreateParamServiceMock(_paramRepo)).Object,
                CreateCustServiceMock(_custRepo).Object,
                CreateFollowServiceMock(_followRepo).Object,
                CreateOrderServiceMock(_orderRepo).Object,
                CreateReceiveServiceMock(_receiveRepo).Object,
                CreateInvoiceServiceMock(_invoiceRepo).Object,
                CreateNewsServiceMock(_newsRepo).Object,
                (logSvc ?? CreateLogMock()).Object);
        }

        // =========================================================
        // Grid（A 档：分页 + 按类型过滤）
        // =========================================================

        [Fact]
        public async Task Grid_EmptyDB_ReturnsZeroCount()
        {
            var ctrl = CreateController();

            var json = await ctrl.Grid(new PageView<Sys_Param> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<Sys_Param>>(JObject.Parse(json).ToString());

            Assert.Equal(0, data.count);
        }

        [Fact]
        public async Task Grid_FilterByType_ReturnsMatched()
        {
            await InsertParam("P1", "转介绍", "cus_source", 1);
            await InsertParam("P2", "陌拜", "cus_source", 2);
            await InsertParam("P3", "重要客户", "cus_level", 1);

            var ctrl = CreateController(queryString: "?T_type=cus_source");

            var json = await ctrl.Grid(new PageView<Sys_Param> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<Sys_Param>>(JObject.Parse(json).ToString());

            Assert.Equal(2, data.count);
            Assert.All(data.data, p => Assert.Equal("cus_source", p.params_type));
        }

        [Fact]
        public async Task Grid_WithPagination_ReturnsLimitedRows()
        {
            for (int i = 0; i < 4; i++)
            {
                await InsertParam($"P{i}", $"参数{i}", "cus_source", i);
            }

            var ctrl = CreateController();

            var json = await ctrl.Grid(new PageView<Sys_Param> { Page = 1, Limit = 2 });
            var data = JsonConvert.DeserializeObject<XHDData<Sys_Param>>(JObject.Parse(json).ToString());

            Assert.Equal(4, data.count);
            Assert.Equal(2, data.data.Count);
        }

        // =========================================================
        // Combo（A 档：按类型）
        // =========================================================

        [Fact]
        public async Task Combo_ByType_ReturnsParams()
        {
            await InsertParam("P1", "转介绍", "cus_source", 1);
            await InsertParam("P2", "陌拜", "cus_source", 2);
            await InsertParam("P3", "重要客户", "cus_level", 1);

            var ctrl = CreateController();

            var json = await ctrl.Combo("cus_source");
            var data = JsonConvert.DeserializeObject<XHDData<Sys_Param>>(JObject.Parse(json).ToString());

            Assert.Equal(2, data.count);
            Assert.All(data.data, p => Assert.Equal("cus_source", p.params_type));
        }

        // =========================================================
        // Validate（A 档：唯一性校验，存在/不存在两态）
        // =========================================================

        [Fact]
        public async Task Validate_EmptyName_ReturnsFalse()
        {
            var ctrl = CreateController();

            var json = await ctrl.Validate("", "cus_source", "");

            Assert.Equal("false", json);
        }

        [Fact]
        public async Task Validate_UniqueName_ReturnsTrue()
        {
            await InsertParam("P1", "转介绍", "cus_source");

            var ctrl = CreateController();

            var json = await ctrl.Validate("陌拜", "cus_source", "");

            Assert.Equal("true", json);
        }

        [Fact]
        public async Task Validate_DuplicateName_ReturnsFalse()
        {
            await InsertParam("P1", "转介绍", "cus_source");

            var ctrl = CreateController();

            var json = await ctrl.Validate("转介绍", "cus_source", "");

            Assert.Equal("false", json);
        }

        [Fact]
        public async Task Validate_SameNameDifferentType_ReturnsTrue()
        {
            // 同名挂在别的参数类型下不算重名（唯一性按 params_name + params_type 组合判定）
            await InsertParam("P1", "转介绍", "cus_source");

            var ctrl = CreateController();

            var json = await ctrl.Validate("转介绍", "cus_level", "");

            Assert.Equal("true", json);
        }

        [Fact]
        public async Task Validate_ExcludeSelf_ReturnsTrue()
        {
            await InsertParam("P1", "转介绍", "cus_source");

            var ctrl = CreateController();

            // 编辑场景：排除自身 id 后应判为唯一
            var json = await ctrl.Validate("转介绍", "cus_source", "P1");

            Assert.Equal("true", json);
        }

        // =========================================================
        // Save（B 档：新增 + 更新两态）
        // =========================================================

        [Fact]
        public async Task Save_New_PersistsRow()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new Sys_Param { params_name = "会展渠道", params_type = "cus_source", params_order = 9 });
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var rows = await _fsql.Select<Sys_Param>().Where(a => a.params_name == "会展渠道").ToListAsync();
            Assert.Single(rows);
            Assert.False(string.IsNullOrEmpty(rows[0].id));
            Assert.Equal("cus_source", rows[0].params_type);
        }

        [Fact]
        public async Task Save_Update_ChangesParamName()
        {
            await InsertParam("P1", "转介绍", "cus_source", 1);
            var logSvc = CreateLogMock();
            var ctrl = CreateController(logSvc: logSvc);

            var json = await ctrl.Save(new Sys_Param
            {
                id = "P1",
                params_name = "老客户转介绍",
                params_type = "cus_source",
                params_order = 1,
                create_id = "SEED",
                create_time = new DateTime(2024, 1, 1)
            });
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<Sys_Param>().Where(a => a.id == "P1").FirstAsync();
            Assert.Equal("老客户转介绍", row.params_name);
            logSvc.Verify(l => l.UpdateLog(It.IsAny<Sys_log>()), Times.Once);
        }

        [Fact]
        public async Task Save_Update_Nonexistent_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new Sys_Param { id = "NO_EXIST", params_name = "幽灵参数" });
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("找不到数据", (string)obj["msg"]!);
        }

        // =========================================================
        // Delete（B 档：存在 + 不存在 + 业务引用保护）
        // =========================================================

        [Fact]
        public async Task Delete_Existing_RemovesRow()
        {
            await InsertParam("P1", "会展渠道", "cus_source");
            var logSvc = CreateLogMock();
            var ctrl = CreateController(logSvc: logSvc);

            var json = await ctrl.Delete("P1");
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<Sys_Param>().Where(a => a.id == "P1").FirstAsync();
            Assert.Null(row);
            logSvc.Verify(l => l.DeleteLog(It.IsAny<Sys_log>()), Times.Once);
        }

        [Fact]
        public async Task Delete_Nonexistent_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Delete("NO_EXIST");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("找不到此数据", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Delete_SourceParamLinkedCustomer_ReturnsError()
        {
            await InsertParam("P1", "转介绍", "cus_source");
            await _fsql.Insert(new CRM_Customer { id = "CUS1", cus_name = "客户", cus_source_id = "P1" })
                .ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Delete("P1");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("有客户", (string)obj["msg"]!);

            var stillThere = await _fsql.Select<Sys_Param>().Where(a => a.id == "P1").CountAsync();
            Assert.Equal(1, stillThere);
        }

        [Fact]
        public async Task Delete_PayTypeParamLinkedReceive_ReturnsError()
        {
            await InsertParam("P1", "微信支付", "pay_type");
            await _fsql.Insert(new Finance_Receive { id = "R1", Pay_type_id = "P1" })
                .ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Delete("P1");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("有收款", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Delete_UnknownTypeParam_RemovesRow()
        {
            // params_type 未命中任何引用分支，直接走删除
            await InsertParam("P9", "自定义参数", "custom_unmapped");

            var ctrl = CreateController();

            var json = await ctrl.Delete("P9");
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<Sys_Param>().Where(a => a.id == "P9").FirstAsync();
            Assert.Null(row);
        }
    }

    /// <summary>
    /// 参数类型线端点测试（A 档：树）。
    /// </summary>
    public class SysParamTypeTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sys_Param_TypeRepository _typeRepo;

        public SysParamTypeTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _typeRepo = new Sys_Param_TypeRepository(_fsql);
        }

        public void Dispose() => _fsql?.Dispose();

        private static Mock<ISys_Param_TypeService> CreateTypeServiceMock(Sys_Param_TypeRepository repo)
        {
            var mock = new Mock<ISys_Param_TypeService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_Type, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Sys_Param_Type, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_Type, bool>>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<Sys_Param_Type, bool>> e, int p, int l, string o) => repo.GridAsync(e, p, l, o));
            // Service 双参重载返回 XHDData，仓储返回 List，改用分页重载包一层
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_Type, bool>>>(), It.IsAny<string>()))
                .Returns((Expression<Func<Sys_Param_Type, bool>> e, string o) => repo.GridAsync(e, 1, 100000));
            return mock;
        }

        [Fact]
        public async Task Tree_ReturnsAllTypesAsJsonArray()
        {
            await _fsql.Insert(new Sys_Param_Type { id = "T1", params_name = "客户来源", params_order = 1 }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_Param_Type { id = "T2", params_name = "客户等级", params_order = 2 }).ExecuteAffrowsAsync();

            var ctrl = TestControllerHelper.CreateWithHttpContext<SysParamTypeController>(
                "", "TEST_USER", "Test User",
                new Mock<ILogger<SysParamTypeController>>().Object,
                CreateTypeServiceMock(_typeRepo).Object);

            var json = await ctrl.Tree();
            var arr = JArray.Parse(json);

            Assert.Equal(2, arr.Count);
            Assert.Equal("T1", (string)arr[0]["id"]!);
            Assert.Equal("客户来源", (string)arr[0]["title"]!);
            Assert.Equal("T2", (string)arr[1]["id"]!);
        }
    }

    /// <summary>
    /// 按钮线端点测试（A 档：查全部，带全公司权限闸门）。
    /// </summary>
    public class SysButtonTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sys_ButtonRepository _btnRepo;

        public SysButtonTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _btnRepo = new Sys_ButtonRepository(_fsql);
        }

        public void Dispose() => _fsql?.Dispose();

        private static Mock<ISys_ButtonService> CreateBtnServiceMock(Sys_ButtonRepository repo)
        {
            var mock = new Mock<ISys_ButtonService>();
            // Service 单参重载返回 XHDData，仓储返回 List，改用分页重载包一层
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Button, bool>>>()))
                .Returns((Expression<Func<Sys_Button, bool>> e) => repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Button, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Sys_Button, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            return mock;
        }

        private static Mock<IDBAuthService> CreateAuth(int authtype)
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = authtype, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            return auth;
        }

        private SysButtonController CreateController(int authtype = 4, string userId = "TEST_USER")
        {
            return TestControllerHelper.CreateWithHttpContext<SysButtonController>(
                "", userId, "Test User",
                new Mock<ILogger<SysButtonController>>().Object,
                CreateBtnServiceMock(_btnRepo).Object,
                _btnRepo,
                CreateAuth(authtype).Object);
        }

        [Fact]
        public async Task Grid_WithFullAccess_ReturnsAllButtons()
        {
            await _fsql.Insert(new Sys_Button { id = "B1", Btn_name = "新增", Btn_type = "add", Menu_id = "M1" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_Button { id = "B2", Btn_name = "删除", Btn_type = "del", Menu_id = "M1" }).ExecuteAffrowsAsync();

            var ctrl = CreateController(authtype: 4);

            var json = await ctrl.Grid();
            var data = JsonConvert.DeserializeObject<XHDData<Sys_Button>>(JObject.Parse(json).ToString());

            Assert.Equal(0, data.code);
            Assert.Equal(2, data.count);
            Assert.Equal(2, data.data.Count);
        }

        [Fact]
        public async Task Grid_NonFullAccess_ReturnsError()
        {
            await _fsql.Insert(new Sys_Button { id = "B1", Btn_name = "新增" }).ExecuteAffrowsAsync();

            var ctrl = CreateController(authtype: 2);

            var json = await ctrl.Grid();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无操作权限", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Grid_AnonymousUser_ReturnsError()
        {
            var ctrl = CreateController(userId: "");

            var json = await ctrl.Grid();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("登录状态已过期", (string)obj["msg"]!);
        }
    }
}
