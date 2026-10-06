using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FreeSql;
using Moq;
using Newtonsoft.Json;
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
    /// Sprint 10.38 第 10 轮 R10-B 真集成测试（SQLite in-memory + 真实 Service/Repository）。
    ///
    /// 覆盖两条链路：
    /// 1) ismark 批量标记（对应 A 侧 Server.CRM_Customer.UpdateBFmark / DAL.CRM_Customer.UpdateBFmark）：
    ///    Entity.ismark -> Repository.UpdateMarkAsync -> Service.UpdateMark -> CustomerController.UpdateMark。
    ///    B 侧此前完全没有 ismark 字段，本测试证明全链路打通：标记落库、取消标记、值校验、
    ///    去重、数据权限预筛（empList）。
    /// 2) #45 keyword 跨字段搜索（对应 A 侧 Server.CRM_Customer.seachgrid:555）：
    ///    A 侧 keyword = address OR DesCripe OR Remarks；B 侧此前只查 cus_name。
    ///    修复后 keyword 命中 cus_name / cus_add / DesCripe / Remarks 任一字段。
    /// </summary>
    public class CustomerMarkSearchTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly ICRM_CustomerService _custSvc;
        private readonly DBAuthService _authSvc;
        private readonly Sys_logService _logSvc;

        public CustomerMarkSearchTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            var repo = new CRM_CustomerRepository(_fsql);
            _custSvc = new CRM_CustomerService(repo);
            _authSvc = new DBAuthService(new DBAuthRepository(_fsql));
            _logSvc = new Sys_logService(new Sys_logRepository(_fsql));
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ Controller 装配 ============

        /// <summary>
        /// 用 admin 身份 + 真实 DBAuthService 装配：GetDataAuth("admin") 硬编码返回 authtype=5（全部），
        /// UpdateMark 的数据权限预筛直接放行，可专注验证标记主链路。
        /// </summary>
        private CustomerController CreateController(string queryString = "")
        {
            return CreateController(queryString, "admin", "管理员", _authSvc);
        }

        /// <summary>
        /// 可自定义身份与权限服务的装配：用于验证非 admin 的 empList 数据权限预筛。
        /// </summary>
        private CustomerController CreateController(
            string queryString, string userId, string userName, IDBAuthService authSvc)
        {
            return TestControllerHelper.CreateWithHttpContext<CustomerController>(
                queryString, userId, userName,
                _custSvc,
                new Mock<ICRM_ContactService>().Object,
                new Mock<ICRM_followService>().Object,
                new Mock<ISale_orderService>().Object,
                new Mock<ISale_contractService>().Object,
                authSvc,
                new Mock<ISys_ParamService>().Object,
                new Mock<ISys_Param_ProvincesService>().Object,
                _logSvc,
                new Mock<ISys_infoService>().Object,
                _fsql);
        }

        /// <summary>
        /// 受限数据权限（authtype=2 本部门，empList 白名单）的 Mock IDBAuthService。
        /// </summary>
        private static IDBAuthService CreateRestrictedAuth(List<string> empList, int authtype = 2)
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = authtype, empList = empList });
            return auth.Object;
        }

        private static (int code, string msg, int data) ParseResult(string json)
        {
            var jo = JObject.Parse(json);
            var dataToken = jo["data"];
            int data = dataToken != null && dataToken.Type != JTokenType.Null
                ? dataToken.Value<int>()
                : -1;
            return (jo.Value<int>("code"), jo.Value<string>("msg") ?? string.Empty, data);
        }

        private static List<CRM_Customer> ParseGridData(string json)
        {
            var result = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());
            return result?.data ?? new List<CRM_Customer>();
        }

        private async Task SeedCusAsync(
            string id, string name,
            string add = "", string desc = "", string remarks = "",
            int state = 3, int? ismark = null, string empId = "E1")
        {
            await _fsql.Insert(new CRM_Customer
            {
                id = id,
                cus_name = name,
                cus_add = add,
                DesCripe = desc,
                Remarks = remarks,
                state = state,
                ismark = ismark,
                emp_id = empId,
                isDelete = 0,
                isPrivate = 0,
                create_time = DateTime.Now
            }).ExecuteAffrowsAsync();
        }

        private async Task<int?> GetMarkAsync(string id)
        {
            var c = await _fsql.Select<CRM_Customer>().Where(a => a.id == id).FirstAsync();
            return c?.ismark;
        }

        // =============================================================
        // == ismark 批量标记（UpdateMark 端点 -> Repository 落库） ==
        // =============================================================

        // ============ 1. 正常标记两个客户：落库 ismark=1 ============

        [Fact]
        public async Task UpdateMark_TwoValidIds_SetsIsMarkOne_AndPersists()
        {
            await SeedCusAsync("C1", "客户一", state: 3);
            await SeedCusAsync("C2", "客户二", state: 3);

            var payload = JObject.Parse(@"{""ids"":[""C1"",""C2""],""mark"":1}");
            var (code, msg, data) = ParseResult(await CreateController().UpdateMark(payload));

            Assert.Equal(0, code);
            Assert.Equal("数据成功标记!", msg);
            Assert.Equal(2, data);

            Assert.Equal(1, await GetMarkAsync("C1"));
            Assert.Equal(1, await GetMarkAsync("C2"));
        }

        // ============ 2. 空 ids：拦截，不落库 ============

        [Fact]
        public async Task UpdateMark_EmptyIds_ReturnsError()
        {
            await SeedCusAsync("C1", "客户一", state: 3);

            var payload = JObject.Parse(@"{""ids"":[],""mark"":1}");
            var (code, msg, _) = ParseResult(await CreateController().UpdateMark(payload));

            Assert.Equal(1, code);
            Assert.Null(await GetMarkAsync("C1"));
        }

        // ============ 3. mark 值非法（2）：拦截，不落库 ============

        [Fact]
        public async Task UpdateMark_MarkValueOutOfRange_ReturnsError()
        {
            await SeedCusAsync("C1", "客户一", state: 3);

            // A 侧 ismark 只有 0/1 两种业务值，B 侧端点显式收窄为 0|1
            var payload = JObject.Parse(@"{""ids"":[""C1""],""mark"":2}");
            var (code, msg, _) = ParseResult(await CreateController().UpdateMark(payload));

            Assert.Equal(1, code);
            Assert.Equal("标记值非法", msg);
            Assert.Null(await GetMarkAsync("C1"));
        }

        // ============ 4. 取消标记：ismark 1 -> 0 ============

        [Fact]
        public async Task UpdateMark_Unmark_SetsIsMarkZero()
        {
            await SeedCusAsync("C1", "客户一", state: 3, ismark: 1);

            var payload = JObject.Parse(@"{""ids"":[""C1""],""mark"":0}");
            var (code, _, _) = ParseResult(await CreateController().UpdateMark(payload));

            Assert.Equal(0, code);
            Assert.Equal(0, await GetMarkAsync("C1"));
        }

        // ============ 5. 重复标记：幂等成功（已处于目标态仍返回 code=0） ============

        [Fact]
        public async Task UpdateMark_RepeatedMark_IsIdempotent()
        {
            await SeedCusAsync("C1", "客户一", state: 3);

            var payload = JObject.Parse(@"{""ids"":[""C1""],""mark"":1}");

            var first = ParseResult(await CreateController().UpdateMark(payload));
            Assert.Equal(0, first.code);
            Assert.Equal(1, await GetMarkAsync("C1"));

            // 第二次标记同一客户：不报错，值仍为 1
            var second = ParseResult(await CreateController().UpdateMark(payload));
            Assert.Equal(0, second.code);
            Assert.Equal(1, await GetMarkAsync("C1"));
        }

        // ============ 6. 重复 id 去重：不因重复条数误拒 ============

        [Fact]
        public async Task UpdateMark_DuplicateIds_DoesNotFalseReject()
        {
            await SeedCusAsync("C1", "客户一", state: 3);
            await SeedCusAsync("C2", "客户二", state: 3);

            // 前端可能重复提交同一 id；端点先 Distinct 再比对权限预筛条数
            var payload = JObject.Parse(@"{""ids"":[""C1"",""C1"",""C2""],""mark"":1}");
            var (code, _, data) = ParseResult(await CreateController().UpdateMark(payload));

            Assert.Equal(0, code);
            Assert.Equal(2, data);
            Assert.Equal(1, await GetMarkAsync("C1"));
            Assert.Equal(1, await GetMarkAsync("C2"));
        }

        // ============ 7. 受限用户标记他人名下客户：拦截且不落库 ============

        [Fact]
        public async Task UpdateMark_RestrictedUser_OtherEmpCustomer_Blocked()
        {
            // C1 归属 E2，受限用户 U1 的 empList 只含 E1
            await SeedCusAsync("C1", "客户一", state: 3, empId: "E2");

            var auth = CreateRestrictedAuth(new List<string> { "E1" }, authtype: 2);
            var ctrl = CreateController(string.Empty, "U1", "受限用户", auth);

            var payload = JObject.Parse(@"{""ids"":[""C1""],""mark"":1}");
            var (code, msg, _) = ParseResult(await ctrl.UpdateMark(payload));

            Assert.Equal(1, code);
            Assert.Equal("包含无权限操作的客户", msg);
            Assert.Null(await GetMarkAsync("C1"));
        }

        // ============ 8. 受限用户标记自己名下客户：放行 ============

        [Fact]
        public async Task UpdateMark_RestrictedUser_OwnCustomer_Allowed()
        {
            await SeedCusAsync("C1", "客户一", state: 3, empId: "E1");

            var auth = CreateRestrictedAuth(new List<string> { "E1" }, authtype: 2);
            var ctrl = CreateController(string.Empty, "U1", "受限用户", auth);

            var payload = JObject.Parse(@"{""ids"":[""C1""],""mark"":1}");
            var (code, _, _) = ParseResult(await ctrl.UpdateMark(payload));

            Assert.Equal(0, code);
            Assert.Equal(1, await GetMarkAsync("C1"));
        }

        // ===========================================
        // == #45 keyword 跨字段搜索（Intentiongrid） ==
        // ===========================================

        // ============ 9. keyword 命中客户名（原有能力保持） ============

        [Fact]
        public async Task Intentiongrid_Keyword_MatchesCustomerName()
        {
            await SeedCusAsync("C1", "阿尔法科技有限公司", state: 3);
            await SeedCusAsync("C2", "贝塔商贸", state: 3);

            var qs = "?keyword=" + Uri.EscapeDataString("阿尔法");
            var rows = ParseGridData(await CreateController(qs).Intentiongrid(new PageView<CRM_Customer>()));

            Assert.Single(rows);
            Assert.Equal("C1", rows[0].id);
        }

        // ============ 10. keyword 命中 地址 / 描述 / 备注（#45 核心回归） ============

        [Fact]
        public async Task Intentiongrid_Keyword_MatchesAddress_Description_Remarks()
        {
            // 修复前 keyword 只查 cus_name，以下 3 条对 keyword="靶点" 全部漏掉；
            // 对齐 A 侧 seachgrid（address OR DesCripe OR Remarks）后应全部命中。
            await SeedCusAsync("C1", "无名公司甲", add: "靶点大街1号院", state: 3);
            await SeedCusAsync("C2", "无名公司乙", desc: "这是一条含靶点的描述", state: 3);
            await SeedCusAsync("C3", "无名公司丙", remarks: "备注里也有靶点", state: 3);
            await SeedCusAsync("C4", "完全不相关的客户", state: 3);

            var qs = "?keyword=" + Uri.EscapeDataString("靶点");
            var rows = ParseGridData(await CreateController(qs).Intentiongrid(new PageView<CRM_Customer>()));

            var ids = rows.Select(r => r.id).OrderBy(x => x).ToList();
            Assert.Equal(new[] { "C1", "C2", "C3" }, ids);
        }

        // ============ 11. keyword 无匹配：空集 ============

        [Fact]
        public async Task Intentiongrid_Keyword_NoMatch_ReturnsEmpty()
        {
            await SeedCusAsync("C1", "客户一", state: 3);

            var qs = "?keyword=" + Uri.EscapeDataString("根本不存在的关键词xyz");
            var rows = ParseGridData(await CreateController(qs).Intentiongrid(new PageView<CRM_Customer>()));

            Assert.Empty(rows);
        }

        // ============ 12. Repository 层：UpdateMarkAsync 空守卫 ============

        [Fact]
        public async Task UpdateMarkAsync_EmptyOrNullList_ReturnsFalse()
        {
            var repo = new CRM_CustomerRepository(_fsql);

            Assert.False(await repo.UpdateMarkAsync(new List<string>(), 1));
            Assert.False(await repo.UpdateMarkAsync(null!, 1));
        }
    }
}
