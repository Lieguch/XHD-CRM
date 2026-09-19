using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
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
    /// #01-#07 客户池 + 认领/放弃 单元测试。
    /// 覆盖：3 个 Grid + Claimlist + AbanDon 的仓库层与端点层。
    /// </summary>
    public class CustomerControllerTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly CRM_CustomerRepository _custRepo;

        public CustomerControllerTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _custRepo = new CRM_CustomerRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static CRM_Customer NewCustomer(
            string id,
            int state,
            string cusName = "客户",
            string empId = "",
            string industryId = "",
            string keyword = "",
            DateTime? lastFollow = null,
            int isDelete = 0)
        {
            return new CRM_Customer
            {
                id = id,
                state = state,
                cus_name = cusName,
                emp_id = empId,
                cus_industry_id = industryId,
                cus_add = keyword,
                DesCripe = keyword,
                Remarks = keyword,
                lastfollow = lastFollow,
                create_time = lastFollow ?? new DateTime(2024, 6, 15, 10, 30, 0),
                create_id = empId,
                isDelete = isDelete,
                isPrivate = 1,
                sn = $"CU-{id}"
            };
        }

        private Task<int> InsertAsync(CRM_Customer c)
            => _fsql.Insert(c).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        /// <summary>
        /// 构造 CustomerController：service 使用 Moq（默认走 Repository 行为），
        /// HttpContext 通过 DefaultHttpContext 组装。
        /// </summary>
        private static CustomerController CreateController(
            ICRM_CustomerService service,
            Mock<IDBAuthService> authMock,
            Mock<ISys_logService>? logMock = null,
            string queryString = "",
            string userId = "TEST_USER")
        {
            var contactSvc = new Mock<ICRM_ContactService>().Object;
            var followSvc = new Mock<ICRM_followService>().Object;
            var orderSvc = new Mock<ISale_orderService>().Object;
            var contractSvc = new Mock<ISale_contractService>().Object;
            var paramSvc = new Mock<ISys_ParamService>().Object;
            var provSvc = new Mock<ISys_Param_ProvincesService>().Object;
            var infoSvc = new Mock<ISys_infoService>().Object;
            logMock ??= new Mock<ISys_logService>();

            var ctrl = new CustomerController(
                service, contactSvc, followSvc, orderSvc, contractSvc,
                authMock.Object, paramSvc, provSvc,
                logMock.Object, infoSvc);

            var httpCtx = new DefaultHttpContext();
            if (!string.IsNullOrEmpty(queryString))
            {
                var qs = queryString.StartsWith("?") ? queryString.Substring(1) : queryString;
                httpCtx.Request.QueryString = new QueryString($"?{qs}");
            }
            httpCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, userId),
                new Claim(ClaimTypes.Name, "Test User")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return ctrl;
        }

        /// <summary>
        /// 创建 Mock Service，通过 when 表达式桥接到真实 Repository，
        /// 使得 Controller 端点调用能打到 SQLite 内存库。
        /// </summary>
        private static Mock<ICRM_CustomerService> CreateServiceMock(
            CRM_CustomerRepository repo)
        {
            var mock = new Mock<ICRM_CustomerService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer, bool>>>(),
                                       It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<CRM_Customer, bool>> e, int p, int l)
                    => repo.GridAsync(e, p, l));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer, bool>>>(),
                                       It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<CRM_Customer, bool>> e, int p, int l, string o)
                    => repo.GridAsync(e, p, l, o));
            mock.Setup(s => s.Claimlist(It.IsAny<List<string>>(), It.IsAny<string>()))
                .Returns((List<string> ids, string emp) => repo.ClaimlistAsync(ids, 0, emp));
            mock.Setup(s => s.AbanDon(It.IsAny<List<string>>()))
                .Returns((List<string> ids) => repo.AbanDonAsync(ids, 1));
            return mock;
        }

        /// <summary>
        /// 让 Mock IDBAuthService 返回全公司权限（authtype=4）。
        /// </summary>
        private static Mock<IDBAuthService> CreateFullAccessAuth()
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 4, empList = new List<string>() });
            return auth;
        }

        // =========================================================
        // #01 Poolgrid（公共池，state=1）
        // =========================================================

        [Fact]
        public async Task Poolgrid_WhenOnlyState0_ReturnsEmpty()
        {
            await InsertAsync(NewCustomer("C1", 0, "私有客户A"));
            await InsertAsync(NewCustomer("C2", 0, "私有客户B"));

            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth());

            var json = await ctrl.Poolgrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(0, data.count);
            Assert.Empty(data.data);
        }

        [Fact]
        public async Task Poolgrid_WhenState1RecordsExist_ReturnsAll()
        {
            var now = new DateTime(2024, 6, 15, 10, 0, 0);
            for (int i = 0; i < 5; i++)
            {
                await InsertAsync(NewCustomer($"P{i}", 1, $"公共客户{i}",
                    lastFollow: now.AddMinutes(-i)));
            }
            // 额外插入 state=0 应被过滤
            await InsertAsync(NewCustomer("X0", 0, "私有客户"));

            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth());

            var json = await ctrl.Poolgrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(5, data.count);
            Assert.Equal(5, data.data.Count);
            Assert.All(data.data, c => Assert.Equal(1, c.state));
        }

        [Fact]
        public async Task Poolgrid_WithIndustryFilter_ReturnsMatchingSubset()
        {
            await InsertAsync(NewCustomer("P1", 1, "客户A", industryId: "IND001"));
            await InsertAsync(NewCustomer("P2", 1, "客户B", industryId: "IND001"));
            await InsertAsync(NewCustomer("P3", 1, "客户C", industryId: "IND002"));

            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth(),
                queryString: "cus_industry_id=IND001");

            var json = await ctrl.Poolgrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(2, data.count);
            Assert.All(data.data, c => Assert.Equal("IND001", c.cus_industry_id));
        }

        [Fact]
        public async Task Poolgrid_WithKeywordFilter_MatchesCustomerName()
        {
            await InsertAsync(NewCustomer("P1", 1, "小黄豆科技"));
            await InsertAsync(NewCustomer("P2", 1, "豆花餐饮"));
            await InsertAsync(NewCustomer("P3", 1, "黄豆豆腐铺"));

            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth(), queryString: "keyword=黄豆");

            var json = await ctrl.Poolgrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(2, data.count);
            Assert.Contains(data.data, c => c.id == "P1");
            Assert.Contains(data.data, c => c.id == "P3");
            Assert.DoesNotContain(data.data, c => c.id == "P2");
        }

        [Fact]
        public async Task Poolgrid_WithPagination_ReturnsLimitedCount()
        {
            for (int i = 0; i < 10; i++)
            {
                await InsertAsync(NewCustomer($"P{i:D2}", 1, $"客户{i}"));
            }

            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth());

            var json = await ctrl.Poolgrid(new PageView<CRM_Customer> { Page = 1, Limit = 3 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(10, data.count);
            Assert.Equal(3, data.data.Count);
        }

        // =========================================================
        // #02 Intentiongrid（意向，state=3）
        // =========================================================

        [Fact]
        public async Task Intentiongrid_OnlyState3_Contains()
        {
            await InsertAsync(NewCustomer("I1", 3, "意向1"));
            await InsertAsync(NewCustomer("I2", 3, "意向2"));
            await InsertAsync(NewCustomer("P1", 1, "公共"));
            await InsertAsync(NewCustomer("H1", 2, "高意向"));
            await InsertAsync(NewCustomer("N1", 0, "正常"));

            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth());

            var json = await ctrl.Intentiongrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(2, data.count);
            Assert.All(data.data, c => Assert.Equal(3, c.state));
        }

        [Fact]
        public async Task Intentiongrid_EmptyDB_ReturnsEmpty()
        {
            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth());

            var json = await ctrl.Intentiongrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(0, data.count);
        }

        [Fact]
        public async Task Intentiongrid_WithKeyword_ReturnsMatched()
        {
            await InsertAsync(NewCustomer("I1", 3, "意向客户A"));
            await InsertAsync(NewCustomer("I2", 3, "意向客户B"));
            await InsertAsync(NewCustomer("I3", 3, "普通客户"));

            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth(), queryString: "keyword=意向");

            var json = await ctrl.Intentiongrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(2, data.count);
            Assert.All(data.data, c => Assert.Contains("意向", c.cus_name));
        }

        [Fact]
        public async Task Intentiongrid_WithIndustryFilter_AppliesCorrectly()
        {
            await InsertAsync(NewCustomer("I1", 3, "意向1", industryId: "IND_A"));
            await InsertAsync(NewCustomer("I2", 3, "意向2", industryId: "IND_A"));
            await InsertAsync(NewCustomer("I3", 3, "意向3", industryId: "IND_B"));

            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth(), queryString: "cus_industry_id=IND_A");

            var json = await ctrl.Intentiongrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(2, data.count);
            Assert.All(data.data, c => Assert.Equal("IND_A", c.cus_industry_id));
        }

        // =========================================================
        // #03 HighIntentiongrid（高意向，state=2）
        // =========================================================

        [Fact]
        public async Task HighIntentiongrid_OnlyState2_Contains()
        {
            await InsertAsync(NewCustomer("H1", 2, "高意向1"));
            await InsertAsync(NewCustomer("H2", 2, "高意向2"));
            await InsertAsync(NewCustomer("H3", 2, "高意向3"));
            await InsertAsync(NewCustomer("P1", 1, "公共"));
            await InsertAsync(NewCustomer("I1", 3, "意向"));

            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth());

            var json = await ctrl.HighIntentiongrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(3, data.count);
            Assert.All(data.data, c => Assert.Equal(2, c.state));
        }

        [Fact]
        public async Task HighIntentiongrid_EmptyDB_ReturnsEmpty()
        {
            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth());

            var json = await ctrl.HighIntentiongrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(0, data.count);
        }

        [Fact]
        public async Task HighIntentiongrid_WithKeywordFilter_Applies()
        {
            await InsertAsync(NewCustomer("H1", 2, "高意向甲"));
            await InsertAsync(NewCustomer("H2", 2, "高意向乙"));
            await InsertAsync(NewCustomer("H3", 2, "普通客户"));

            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth(), queryString: "keyword=高意向");

            var json = await ctrl.HighIntentiongrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(2, data.count);
        }

        [Fact]
        public async Task HighIntentiongrid_WithIndustryId_FilterSubset()
        {
            await InsertAsync(NewCustomer("H1", 2, "高意向1", industryId: "IND_H1"));
            await InsertAsync(NewCustomer("H2", 2, "高意向2", industryId: "IND_H2"));
            await InsertAsync(NewCustomer("H3", 2, "高意向3", industryId: "IND_H2"));

            var svc = CreateServiceMock(_custRepo);
            var ctrl = CreateController(svc.Object, CreateFullAccessAuth(), queryString: "cus_industry_id=IND_H2");

            var json = await ctrl.HighIntentiongrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(json).ToString());

            Assert.Equal(2, data.count);
        }

        // =========================================================
        // #04 Claimlist Repository 层测试
        // =========================================================

        [Fact]
        public async Task Claimlist_WithTwoIds_UpdatesStateAndEmpId()
        {
            await InsertAsync(NewCustomer("C1", 1, "客户1"));
            await InsertAsync(NewCustomer("C2", 1, "客户2"));
            await InsertAsync(NewCustomer("C3", 1, "客户3"));

            var ok = await _custRepo.ClaimlistAsync(new List<string> { "C1", "C2" }, 0, "E001");

            Assert.True(ok);
            var c1 = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").FirstAsync();
            var c2 = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C2").FirstAsync();
            var c3 = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C3").FirstAsync();

            Assert.Equal(0, c1.state);
            Assert.Equal("E001", c1.emp_id);
            Assert.Equal(0, c2.state);
            Assert.Equal("E001", c2.emp_id);
            Assert.Equal(1, c3.state);
        }

        [Fact]
        public async Task Claimlist_WithEmptyList_ReturnsFalse()
        {
            var ok = await _custRepo.ClaimlistAsync(new List<string>(), 0, "E001");
            Assert.False(ok);
        }

        [Fact]
        public async Task Claimlist_WithNullList_ReturnsFalse()
        {
            var ok = await _custRepo.ClaimlistAsync(null!, 0, "E001");
            Assert.False(ok);
        }

        [Fact]
        public async Task Claimlist_WithNonexistentId_ReturnsFalse()
        {
            await InsertAsync(NewCustomer("C1", 1, "客户1"));

            var ok = await _custRepo.ClaimlistAsync(new List<string> { "NOT_EXIST" }, 0, "E001");

            Assert.False(ok);
        }

        [Fact]
        public async Task Claimlist_WithSpecialCharEmpId_NoSqlInjection()
        {
            await InsertAsync(NewCustomer("C1", 1, "客户1"));
            var evilEmpId = "E001' OR '1'='1";

            var ok = await _custRepo.ClaimlistAsync(new List<string> { "C1" }, 0, evilEmpId);

            Assert.True(ok);
            var c1 = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").FirstAsync();
            Assert.Equal(evilEmpId, c1.emp_id);
        }

        [Fact]
        public async Task Claimlist_WithFiveHundredIds_UpdatesAll()
        {
            for (int i = 0; i < 500; i++)
            {
                await InsertAsync(NewCustomer($"C{i:D3}", 1, $"客户{i}"));
            }
            var ids = Enumerable.Range(0, 500).Select(i => $"C{i:D3}").ToList();

            var start = DateTime.Now;
            var ok = await _custRepo.ClaimlistAsync(ids, 0, "E001");
            var elapsed = DateTime.Now - start;

            Assert.True(ok);
            Assert.True(elapsed.TotalSeconds < 15);

            var stillState1 = await _fsql.Select<CRM_Customer>()
                .Where(a => ids.Contains(a.id) && a.state == 1).CountAsync();
            Assert.Equal(0, stillState1);
        }

        // =========================================================
        // #05 AbanDon Repository 层测试
        // =========================================================

        [Fact]
        public async Task AbanDon_WithSingleId_UpdatesStateTo1()
        {
            await InsertAsync(NewCustomer("C1", 0, "客户1", empId: "E001"));

            var ok = await _custRepo.AbanDonAsync(new List<string> { "C1" }, 1);

            Assert.True(ok);
            var c1 = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").FirstAsync();
            Assert.Equal(1, c1.state);
            Assert.Equal("E001", c1.emp_id);
        }

        [Fact]
        public async Task AbanDon_WithEmptyList_ReturnsFalse()
        {
            var ok = await _custRepo.AbanDonAsync(new List<string>(), 1);
            Assert.False(ok);
        }

        [Fact]
        public async Task AbanDon_WithNullList_ReturnsFalse()
        {
            var ok = await _custRepo.AbanDonAsync(null!, 1);
            Assert.False(ok);
        }

        [Fact]
        public async Task AbanDon_LinkedToClaimlist_EndsWithState0()
        {
            await InsertAsync(NewCustomer("C1", 0, "客户1", empId: "E001"));

            await _custRepo.AbanDonAsync(new List<string> { "C1" }, 1);
            var mid = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").FirstAsync();
            Assert.Equal(1, mid.state);
            Assert.Equal("E001", mid.emp_id);

            var ok = await _custRepo.ClaimlistAsync(new List<string> { "C1" }, 0, "E002");

            Assert.True(ok);
            var final = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").FirstAsync();
            Assert.Equal(0, final.state);
            Assert.Equal("E002", final.emp_id);
        }

        // =========================================================
        // #06 CustomerController.Claimlist 端点测试
        // =========================================================

        [Fact]
        public async Task Controller_Claimlist_WithEmptyIds_ReturnsError()
        {
            var svc = new Mock<ICRM_CustomerService>();
            svc.Setup(s => s.Claimlist(It.IsAny<List<string>>(), It.IsAny<string>()))
               .ReturnsAsync(true);
            var auth = CreateFullAccessAuth();
            var ctrl = CreateController(svc.Object, auth);

            var json = await ctrl.Claimlist(new JArray());

            var obj = JObject.Parse(json);
            Assert.Equal(1, (int)obj["code"]!);
            Assert.Contains("认领失败", (string)obj["msg"]!);
            // JObject["data"] 对 JSON null 返回 JValue(Type=Null)，不是 C# null
            Assert.Null(obj["data"]?.Value<object>());

            svc.Verify(s => s.Claimlist(It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Controller_Claimlist_WithValidIds_UpdatesDatabase()
        {
            await InsertAsync(NewCustomer("C1", 1, "客户1"));
            await InsertAsync(NewCustomer("C2", 1, "客户2"));

            var svc = CreateServiceMock(_custRepo);
            var auth = CreateFullAccessAuth();
            var ctrl = CreateController(svc.Object, auth, userId: "E001");

            var ids = new JArray { "C1", "C2" };
            var json = await ctrl.Claimlist(ids);

            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal("认领成功", (string)obj["msg"]!);
            Assert.Equal(2, (int)obj["data"]!);

            var c1 = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").FirstAsync();
            var c2 = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C2").FirstAsync();
            Assert.Equal(0, c1.state);
            Assert.Equal("E001", c1.emp_id);
            Assert.Equal(0, c2.state);
            Assert.Equal("E001", c2.emp_id);
        }

        [Fact]
        public async Task Controller_Claimlist_WithNullIds_ReturnsError()
        {
            var svc = new Mock<ICRM_CustomerService>();
            var auth = CreateFullAccessAuth();
            var ctrl = CreateController(svc.Object, auth);

            var json = await ctrl.Claimlist(null!);

            var obj = JObject.Parse(json);
            Assert.Equal(1, (int)obj["code"]!);
            svc.Verify(s => s.Claimlist(It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Controller_Claimlist_WithAnonymousUser_ReturnsError()
        {
            var svc = new Mock<ICRM_CustomerService>();
            var auth = CreateFullAccessAuth();
            var ctrl = CreateController(svc.Object, auth, userId: "");

            var json = await ctrl.Claimlist(new JArray { "C1" });

            var obj = JObject.Parse(json);
            Assert.Equal(1, (int)obj["code"]!);
            Assert.Contains("登录状态", (string)obj["msg"]!);
            svc.Verify(s => s.Claimlist(It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Controller_Claimlist_WithNonexistentIds_ReturnsError()
        {
            var svc = CreateServiceMock(_custRepo);
            var auth = CreateFullAccessAuth();
            var ctrl = CreateController(svc.Object, auth, userId: "E001");

            var json = await ctrl.Claimlist(new JArray { "C1", "C2" });

            var obj = JObject.Parse(json);
            Assert.Equal(1, (int)obj["code"]!);
        }

        // =========================================================
        // #07 CustomerController.AbanDon 端点测试
        // =========================================================

        [Fact]
        public async Task Controller_AbanDon_WithEmptyIds_ReturnsError()
        {
            var svc = new Mock<ICRM_CustomerService>();
            var auth = CreateFullAccessAuth();
            var ctrl = CreateController(svc.Object, auth);

            var json = await ctrl.AbanDon(new JArray());

            var obj = JObject.Parse(json);
            Assert.Equal(1, (int)obj["code"]!);
            Assert.Contains("放弃失败", (string)obj["msg"]!);
            svc.Verify(s => s.AbanDon(It.IsAny<List<string>>()), Times.Never);
        }

        [Fact]
        public async Task Controller_AbanDon_WithValidIds_UpdatesState()
        {
            await InsertAsync(NewCustomer("C1", 0, "客户1", empId: "E001"));

            var svc = CreateServiceMock(_custRepo);
            var auth = CreateFullAccessAuth();
            var ctrl = CreateController(svc.Object, auth, userId: "E001");

            var json = await ctrl.AbanDon(new JArray { "C1" });

            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal("放弃成功", (string)obj["msg"]!);

            var c1 = await _fsql.Select<CRM_Customer>().Where(a => a.id == "C1").FirstAsync();
            Assert.Equal(1, c1.state);
            Assert.Equal("E001", c1.emp_id);
        }

        [Fact]
        public async Task Controller_AbanDon_ThenPoolgrid_CanFindCustomer()
        {
            await InsertAsync(NewCustomer("C1", 0, "客户1", empId: "E001"));

            var svc = CreateServiceMock(_custRepo);
            var auth = CreateFullAccessAuth();
            var ctrl = CreateController(svc.Object, auth, userId: "E001");

            var abandonJson = await ctrl.AbanDon(new JArray { "C1" });
            var abandonObj = JObject.Parse(abandonJson);
            Assert.Equal(0, (int)abandonObj["code"]!);

            var poolJson = await ctrl.Poolgrid(new PageView<CRM_Customer> { Page = 1, Limit = 30 });
            var poolData = JsonConvert.DeserializeObject<XHDData<CRM_Customer>>(JObject.Parse(poolJson).ToString());

            Assert.Equal(1, poolData.count);
            Assert.Contains(poolData.data, c => c.id == "C1");
        }

        [Fact]
        public async Task Controller_AbanDon_WithNullIds_ReturnsError()
        {
            var svc = new Mock<ICRM_CustomerService>();
            var auth = CreateFullAccessAuth();
            var ctrl = CreateController(svc.Object, auth);

            var json = await ctrl.AbanDon(null!);

            var obj = JObject.Parse(json);
            Assert.Equal(1, (int)obj["code"]!);
        }

        [Fact]
        public async Task Controller_AbanDon_WithAnonymousUser_ReturnsError()
        {
            var svc = new Mock<ICRM_CustomerService>();
            var auth = CreateFullAccessAuth();
            var ctrl = CreateController(svc.Object, auth, userId: "");

            var json = await ctrl.AbanDon(new JArray { "C1" });

            var obj = JObject.Parse(json);
            Assert.Equal(1, (int)obj["code"]!);
            Assert.Contains("登录状态", (string)obj["msg"]!);
        }
    }
}
