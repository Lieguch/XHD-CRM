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
    /// 省份/城市参数主数据线端点测试（A 查询 + B 落库）。
    /// 范式同 CustomerControllerTests：Mock service 桥接真实 repository + SQLite in-memory。
    /// </summary>
    public class ParamsProvinceTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sys_Param_ProvincesRepository _provRepo;
        private readonly Sys_Param_CityRepository _cityRepo;
        private readonly CRM_CustomerRepository _custRepo;

        public ParamsProvinceTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _provRepo = new Sys_Param_ProvincesRepository(_fsql);
            _cityRepo = new Sys_Param_CityRepository(_fsql);
            _custRepo = new CRM_CustomerRepository(_fsql);
        }

        public void Dispose() => _fsql?.Dispose();

        // ============ 测试数据工厂 ============

        private Task<int> InsertProvince(string id, string name, int order = 0, string type = "province")
            => _fsql.Insert(new Sys_Param_Provinces
            {
                id = id,
                Provinces = name,
                Provinces_order = order,
                Provinces_type = type,
                create_id = "SEED",
                create_time = new DateTime(2024, 1, 1)
            }).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        /// <summary>
        /// Mock ISys_Param_ProvincesService 并桥接到真实仓储。
        /// 单参/双参 GridAsync 的 Service 契约返回 XHDData，仓储返回 List，
        /// 统一改用分页重载包一层（与 CustomerControllerTests 同款桥接技巧）。
        /// </summary>
        private static Mock<ISys_Param_ProvincesService> CreateProvServiceMock(Sys_Param_ProvincesRepository repo)
        {
            var mock = new Mock<ISys_Param_ProvincesService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_Provinces, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Sys_Param_Provinces, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_Provinces, bool>>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<Sys_Param_Provinces, bool>> e, int p, int l, string o) => repo.GridAsync(e, p, l, o));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_Provinces, bool>>>()))
                .Returns((Expression<Func<Sys_Param_Provinces, bool>> e) => repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_Provinces, bool>>>(), It.IsAny<string>()))
                .Returns((Expression<Func<Sys_Param_Provinces, bool>> e, string o) => repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.AddAsync(It.IsAny<Sys_Param_Provinces>()))
                .Returns((Sys_Param_Provinces m) => repo.AddAsync(m));
            mock.Setup(s => s.UpdateAsync(It.IsAny<Sys_Param_Provinces>()))
                .Returns((Sys_Param_Provinces m) => repo.UpdateAsync(m));
            mock.Setup(s => s.DeleteAsync(It.IsAny<string>()))
                .Returns((string id) => repo.DeleteAsync(id));
            return mock;
        }

        /// <summary>
        /// 桥接 Delete 预筛依赖的 ICRM_CustomerService.GridAsync(exp,page,limit) 到真实仓储。
        /// </summary>
        private static Mock<ICRM_CustomerService> CreateCustServiceMock(CRM_CustomerRepository repo)
        {
            var mock = new Mock<ICRM_CustomerService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<CRM_Customer, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            return mock;
        }

        /// <summary>
        /// 桥接 Delete 预筛依赖的 ISys_Param_CityService.GridAsync(exp,page,limit) 到真实仓储。
        /// </summary>
        private static Mock<ISys_Param_CityService> CreateCityServiceMock(Sys_Param_CityRepository repo)
        {
            var mock = new Mock<ISys_Param_CityService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_City, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Sys_Param_City, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            return mock;
        }

        /// <summary>
        /// 日志 mock：Save/Delete 落库后写操作日志，返回 1 表示成功。
        /// </summary>
        private static Mock<ISys_logService> CreateLogMock()
        {
            var mock = new Mock<ISys_logService>();
            mock.Setup(l => l.UpdateLog(It.IsAny<Sys_log>())).ReturnsAsync(1);
            mock.Setup(l => l.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);
            return mock;
        }

        /// <summary>
        /// 权限 mock 两件套（同 CustomerControllerTests）：
        /// GetDataAuth 放行全量 + GetAuth 返回 true，避免 ButtonAuth 闸门误判。
        /// </summary>
        private static Mock<IDBAuthService> CreateFullAccessAuth()
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 5, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            return auth;
        }

        private ParamsProvinceController CreateController(
            Mock<ISys_Param_ProvincesService>? provSvc = null,
            Mock<ISys_logService>? logSvc = null,
            string queryString = "",
            string userId = "TEST_USER")
        {
            return TestControllerHelper.CreateWithHttpContext<ParamsProvinceController>(
                queryString, userId, "Test User",
                new Mock<ILogger<ParamsProvinceController>>().Object,
                (provSvc ?? CreateProvServiceMock(_provRepo)).Object,
                CreateCustServiceMock(_custRepo).Object,
                CreateCityServiceMock(_cityRepo).Object,
                (logSvc ?? CreateLogMock()).Object,
                CreateFullAccessAuth().Object);
        }

        // =========================================================
        // Grid（A 档：分页 + 关键字）
        // =========================================================

        [Fact]
        public async Task Grid_EmptyDB_ReturnsZeroCount()
        {
            var ctrl = CreateController();

            var json = await ctrl.Grid(new PageView<Sys_Param_Provinces> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<Sys_Param_Provinces>>(JObject.Parse(json).ToString());

            Assert.Equal(0, data.count);
            Assert.Empty(data.data);
        }

        [Fact]
        public async Task Grid_WithKeywordFilter_ReturnsMatched()
        {
            await InsertProvince("P1", "广东省");
            await InsertProvince("P2", "广西壮族自治区");
            await InsertProvince("P3", "北京市");

            var ctrl = CreateController(queryString: "?T_name=广东");

            var json = await ctrl.Grid(new PageView<Sys_Param_Provinces> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<Sys_Param_Provinces>>(JObject.Parse(json).ToString());

            Assert.Equal(1, data.count);
            Assert.Equal("P1", data.data[0].id);
        }

        [Fact]
        public async Task Grid_WithPagination_ReturnsLimitedRows()
        {
            for (int i = 0; i < 5; i++)
            {
                await InsertProvince($"P{i}", $"省份{i}", order: i);
            }

            var ctrl = CreateController();

            var json = await ctrl.Grid(new PageView<Sys_Param_Provinces> { Page = 1, Limit = 2 });
            var data = JsonConvert.DeserializeObject<XHDData<Sys_Param_Provinces>>(JObject.Parse(json).ToString());

            Assert.Equal(5, data.count);
            Assert.Equal(2, data.data.Count);
        }

        // =========================================================
        // Combo（A 档：下拉）
        // =========================================================

        [Fact]
        public async Task Combo_ReturnsProvincesAsJsonArray()
        {
            await InsertProvince("P1", "广东省", order: 1);
            await InsertProvince("P2", "北京市", order: 2);

            var ctrl = CreateController();

            var json = await ctrl.Combo(new PageView<Sys_Param_Provinces>());
            var arr = JArray.Parse(json);

            Assert.Equal(2, arr.Count);
            Assert.Equal("P1", (string)arr[0]["id"]!);
            Assert.Equal("广东省", (string)arr[0]["title"]!);
            Assert.Equal("广东省", (string)arr[0]["text"]!);
            Assert.Equal("P2", (string)arr[1]["id"]!);
        }

        // =========================================================
        // Save（B 档：新增 + 更新两态）
        // =========================================================

        [Fact]
        public async Task Save_New_AssignsIdAndAuditFields()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new Sys_Param_Provinces { Provinces = "上海市", Provinces_order = 1 });
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var rows = await _fsql.Select<Sys_Param_Provinces>().Where(a => a.Provinces == "上海市").ToListAsync();
            Assert.Single(rows);
            Assert.False(string.IsNullOrEmpty(rows[0].id));
            Assert.Equal("TEST_USER", rows[0].create_id);
            Assert.NotNull(rows[0].create_time);
        }

        [Fact]
        public async Task Save_New_WithoutPermission_ReturnsError()
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var ctrl = TestControllerHelper.CreateWithHttpContext<ParamsProvinceController>(
                "", "TEST_USER", "Test User",
                new Mock<ILogger<ParamsProvinceController>>().Object,
                CreateProvServiceMock(_provRepo).Object,
                CreateCustServiceMock(_custRepo).Object,
                CreateCityServiceMock(_cityRepo).Object,
                CreateLogMock().Object,
                auth.Object);

            var json = await ctrl.Save(new Sys_Param_Provinces { Provinces = "天津市" });
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限", (string)obj["msg"]!);
            var count = await _fsql.Select<Sys_Param_Provinces>().Where(a => a.Provinces == "天津市").CountAsync();
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task Save_Update_ChangesProvincesName()
        {
            await InsertProvince("P1", "广东省", order: 1);
            var logSvc = CreateLogMock();
            var ctrl = CreateController(logSvc: logSvc);

            var json = await ctrl.Save(new Sys_Param_Provinces
            {
                id = "P1",
                Provinces = "广东省（更名）",
                Provinces_order = 1,
                Provinces_type = "province",
                create_id = "SEED",
                create_time = new DateTime(2024, 1, 1)
            });
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<Sys_Param_Provinces>().Where(a => a.id == "P1").FirstAsync();
            Assert.Equal("广东省（更名）", row.Provinces);
            logSvc.Verify(l => l.UpdateLog(It.IsAny<Sys_log>()), Times.Once);
        }

        [Fact]
        public async Task Save_Update_Nonexistent_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new Sys_Param_Provinces { id = "NO_EXIST", Provinces = "火星省" });
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("找不到数据", (string)obj["msg"]!);
        }

        // =========================================================
        // Delete（B 档：存在 + 不存在两态）
        // =========================================================

        [Fact]
        public async Task Delete_Existing_RemovesRow()
        {
            await InsertProvince("P1", "广东省");
            var logSvc = CreateLogMock();
            var ctrl = CreateController(logSvc: logSvc);

            var json = await ctrl.Delete("P1");
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<Sys_Param_Provinces>().Where(a => a.id == "P1").FirstAsync();
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
        public async Task Delete_ProvinceWithCity_ReturnsError()
        {
            await InsertProvince("P1", "广东省");
            await _fsql.Insert(new Sys_Param_City
            {
                id = "C1",
                City = "广州市",
                City_order = 1,
                Provinces_id = "P1"
            }).ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Delete("P1");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("有城市", (string)obj["msg"]!);

            var stillThere = await _fsql.Select<Sys_Param_Provinces>().Where(a => a.id == "P1").CountAsync();
            Assert.Equal(1, stillThere);
        }

        [Fact]
        public async Task Delete_ProvinceWithCustomer_ReturnsError()
        {
            await InsertProvince("P1", "广东省");
            await _fsql.Insert(new CRM_Customer { id = "CUS1", cus_name = "广东客户", Provinces_id = "P1" })
                .ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Delete("P1");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("有客户", (string)obj["msg"]!);
        }
    }

    /// <summary>
    /// 城市参数线端点测试（A 查询 + B 落库）。
    /// </summary>
    public class ParamsCityTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sys_Param_CityRepository _cityRepo;
        private readonly CRM_CustomerRepository _custRepo;

        public ParamsCityTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _cityRepo = new Sys_Param_CityRepository(_fsql);
            _custRepo = new CRM_CustomerRepository(_fsql);
        }

        public void Dispose() => _fsql?.Dispose();

        // ============ 测试数据工厂 ============

        private Task<int> InsertCity(string id, string name, string provinceId, int order = 0)
            => _fsql.Insert(new Sys_Param_City
            {
                id = id,
                City = name,
                City_order = order,
                Provinces_id = provinceId,
                create_id = "SEED",
                create_time = new DateTime(2024, 1, 1)
            }).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        private static Mock<ISys_Param_CityService> CreateCityServiceMock(Sys_Param_CityRepository repo)
        {
            var mock = new Mock<ISys_Param_CityService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_City, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Sys_Param_City, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_City, bool>>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<Sys_Param_City, bool>> e, int p, int l, string o) => repo.GridAsync(e, p, l, o));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_City, bool>>>()))
                .Returns((Expression<Func<Sys_Param_City, bool>> e) => repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sys_Param_City, bool>>>(), It.IsAny<string>()))
                .Returns((Expression<Func<Sys_Param_City, bool>> e, string o) => repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.AddAsync(It.IsAny<Sys_Param_City>()))
                .Returns((Sys_Param_City m) => repo.AddAsync(m));
            mock.Setup(s => s.UpdateAsync(It.IsAny<Sys_Param_City>()))
                .Returns((Sys_Param_City m) => repo.UpdateAsync(m));
            mock.Setup(s => s.DeleteAsync(It.IsAny<string>()))
                .Returns((string id) => repo.DeleteAsync(id));
            return mock;
        }

        private static Mock<ICRM_CustomerService> CreateCustServiceMock(CRM_CustomerRepository repo)
        {
            var mock = new Mock<ICRM_CustomerService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<CRM_Customer, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            return mock;
        }

        private static Mock<ISys_logService> CreateLogMock()
        {
            var mock = new Mock<ISys_logService>();
            mock.Setup(l => l.UpdateLog(It.IsAny<Sys_log>())).ReturnsAsync(1);
            mock.Setup(l => l.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);
            return mock;
        }

        private static Mock<IDBAuthService> CreateFullAccessAuth()
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 5, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            return auth;
        }

        private ParamsCityController CreateController(
            Mock<ISys_Param_CityService>? citySvc = null,
            Mock<ISys_logService>? logSvc = null,
            string queryString = "",
            string userId = "TEST_USER")
        {
            return TestControllerHelper.CreateWithHttpContext<ParamsCityController>(
                queryString, userId, "Test User",
                new Mock<ILogger<ParamsCityController>>().Object,
                (citySvc ?? CreateCityServiceMock(_cityRepo)).Object,
                CreateCustServiceMock(_custRepo).Object,
                (logSvc ?? CreateLogMock()).Object,
                CreateFullAccessAuth().Object);
        }

        // =========================================================
        // Grid（A 档：分页 + 按省过滤）
        // =========================================================

        [Fact]
        public async Task Grid_EmptyDB_ReturnsZeroCount()
        {
            var ctrl = CreateController();

            var json = await ctrl.Grid(new PageView<Sys_Param_City> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<Sys_Param_City>>(JObject.Parse(json).ToString());

            Assert.Equal(0, data.count);
        }

        [Fact]
        public async Task Grid_FilterByProvince_ReturnsMatched()
        {
            await InsertCity("C1", "广州市", "P1", 1);
            await InsertCity("C2", "深圳市", "P1", 2);
            await InsertCity("C3", "朝阳区", "P2", 1);

            var ctrl = CreateController(queryString: "?T_type=P1");

            var json = await ctrl.Grid(new PageView<Sys_Param_City> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<Sys_Param_City>>(JObject.Parse(json).ToString());

            Assert.Equal(2, data.count);
            Assert.All(data.data, c => Assert.Equal("P1", c.Provinces_id));
        }

        [Fact]
        public async Task Grid_WithPagination_ReturnsLimitedRows()
        {
            for (int i = 0; i < 4; i++)
            {
                await InsertCity($"C{i}", $"城市{i}", "P1", i);
            }

            var ctrl = CreateController();

            var json = await ctrl.Grid(new PageView<Sys_Param_City> { Page = 1, Limit = 2 });
            var data = JsonConvert.DeserializeObject<XHDData<Sys_Param_City>>(JObject.Parse(json).ToString());

            Assert.Equal(4, data.count);
            Assert.Equal(2, data.data.Count);
        }

        // =========================================================
        // Combo（A 档：按省取城市）
        // =========================================================

        [Fact]
        public async Task Combo_ByProvince_ReturnsCities()
        {
            await InsertCity("C1", "广州市", "P1", 1);
            await InsertCity("C2", "深圳市", "P1", 2);
            await InsertCity("C3", "朝阳区", "P2", 1);

            var ctrl = CreateController();

            var json = await ctrl.Combo("P1");
            var arr = JArray.Parse(json);

            Assert.Equal(2, arr.Count);
            Assert.Equal("C1", (string)arr[0]["id"]!);
            Assert.Equal("广州市", (string)arr[0]["title"]!);
            Assert.Equal("广州市", (string)arr[0]["text"]!);
        }

        // =========================================================
        // Save（B 档：新增 + 更新两态）
        // =========================================================

        [Fact]
        public async Task Save_New_PersistsRow()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new Sys_Param_City { City = "佛山市", Provinces_id = "P1", City_order = 3 });
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var rows = await _fsql.Select<Sys_Param_City>().Where(a => a.City == "佛山市").ToListAsync();
            Assert.Single(rows);
            Assert.False(string.IsNullOrEmpty(rows[0].id));
            Assert.Equal("P1", rows[0].Provinces_id);
        }

        [Fact]
        public async Task Save_Update_ChangesCityName()
        {
            await InsertCity("C1", "广州市", "P1", 1);
            var logSvc = CreateLogMock();
            var ctrl = CreateController(logSvc: logSvc);

            var json = await ctrl.Save(new Sys_Param_City
            {
                id = "C1",
                City = "广州市（更名）",
                City_order = 1,
                Provinces_id = "P1",
                create_id = "SEED",
                create_time = new DateTime(2024, 1, 1)
            });
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<Sys_Param_City>().Where(a => a.id == "C1").FirstAsync();
            Assert.Equal("广州市（更名）", row.City);
            logSvc.Verify(l => l.UpdateLog(It.IsAny<Sys_log>()), Times.Once);
        }

        [Fact]
        public async Task Save_Update_Nonexistent_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new Sys_Param_City { id = "NO_EXIST", City = "不存在的城市" });
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("找不到数据", (string)obj["msg"]!);
        }

        // =========================================================
        // Delete（B 档：存在 + 不存在两态）
        // =========================================================

        [Fact]
        public async Task Delete_Existing_RemovesRow()
        {
            await InsertCity("C1", "广州市", "P1", 1);
            var logSvc = CreateLogMock();
            var ctrl = CreateController(logSvc: logSvc);

            var json = await ctrl.Delete("C1");
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<Sys_Param_City>().Where(a => a.id == "C1").FirstAsync();
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
        public async Task Delete_CityWithCustomer_ReturnsError()
        {
            await InsertCity("C1", "广州市", "P1", 1);
            await _fsql.Insert(new CRM_Customer { id = "CUS1", cus_name = "广州客户", City_id = "C1" })
                .ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Delete("C1");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("有客户", (string)obj["msg"]!);

            var stillThere = await _fsql.Select<Sys_Param_City>().Where(a => a.id == "C1").CountAsync();
            Assert.Equal(1, stillThere);
        }
    }
}
