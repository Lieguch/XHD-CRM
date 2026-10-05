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
    /// 产品分类线端点测试（A 查询 + 树 + B 落库）。
    /// 范式同 CustomerControllerTests：Mock service 桥接真实 repository + SQLite in-memory。
    /// </summary>
    public class ProductCategoryTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Product_categoryRepository _catRepo;
        private readonly ProductRepository _prodRepo;

        public ProductCategoryTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _catRepo = new Product_categoryRepository(_fsql);
            _prodRepo = new ProductRepository(_fsql);
        }

        public void Dispose() => _fsql?.Dispose();

        // ============ 测试数据工厂 ============

        private Task<int> InsertCategory(string id, string parentId, string name)
            => _fsql.Insert(new Product_category
            {
                id = id,
                parentid = parentId,
                category_name = name,
                create_id = "SEED",
                create_time = new DateTime(2024, 1, 1)
            }).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        /// <summary>
        /// Mock IProduct_categoryService 并桥接到真实仓储。
        /// 单参/双参 GridAsync 的 Service 契约返回 XHDData，仓储返回 List，
        /// 统一改用分页重载包一层（与 CustomerControllerTests 同款桥接技巧）。
        /// </summary>
        private static Mock<IProduct_categoryService> CreateCatServiceMock(Product_categoryRepository repo)
        {
            var mock = new Mock<IProduct_categoryService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Product_category, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Product_category, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Product_category, bool>>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<Product_category, bool>> e, int p, int l, string o) => repo.GridAsync(e, p, l, o));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Product_category, bool>>>()))
                .Returns((Expression<Func<Product_category, bool>> e) => repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Product_category, bool>>>(), It.IsAny<string>()))
                .Returns((Expression<Func<Product_category, bool>> e, string o) => repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.AddAsync(It.IsAny<Product_category>()))
                .Returns((Product_category m) => repo.AddAsync(m));
            mock.Setup(s => s.UpdateAsync(It.IsAny<Product_category>()))
                .Returns((Product_category m) => repo.UpdateAsync(m));
            mock.Setup(s => s.DeleteAsync(It.IsAny<string>()))
                .Returns((string id) => repo.DeleteAsync(id));
            mock.Setup(s => s.Tree())
                .Returns(() => BuildTreeJsonFromRepository(repo, null));
            mock.Setup(s => s.Tree(It.IsAny<string>()))
                .Returns((string id) => BuildTreeJsonFromRepository(repo, id));
            return mock;
        }

        /// <summary>
        /// Product_categoryService 是 internal 类，测试无法直接实例化；
        /// 这里从真实仓储取数、按 parentid 递归构建 JArray（算法与服务的 getMenuJson 一致），
        /// 用于在真实层级数据下验证 Controller 树端点的渲染。
        /// </summary>
        private static async Task<JArray> BuildTreeJsonFromRepository(Product_categoryRepository repo, string? excludeId)
        {
            Expression<Func<Product_category, bool>> exp = a => 1 == 1;
            if (!string.IsNullOrWhiteSpace(excludeId))
            {
                exp = exp.And(a => a.parentid != excludeId);
            }

            var list = await repo.GridAsync(exp);
            return BuildChildren(list, "root");
        }

        private static JArray BuildChildren(List<Product_category> list, string parentId)
        {
            var arr = new JArray();
            foreach (var item in list.FindAll(c => c.parentid == parentId))
            {
                var obj = new JObject();
                obj.Add("id", item.id);
                obj.Add("title", item.category_name);
                obj.Add("parentid", item.parentid);
                obj.Add("spread", true);

                var children = BuildChildren(list, item.id);
                if (children.Count > 0)
                {
                    obj.Add("children", children);
                }

                arr.Add(obj);
            }
            return arr;
        }

        /// <summary>
        /// Delete 预筛依赖的 IProductService.GridAsync(exp,page,limit) 桥接到真实仓储。
        /// </summary>
        private static Mock<IProductService> CreateProdServiceMock(ProductRepository repo)
        {
            var mock = new Mock<IProductService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Product, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
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

        private ProductCategoryController CreateController(
            Mock<IProduct_categoryService>? catSvc = null,
            Mock<ISys_logService>? logSvc = null)
        {
            return TestControllerHelper.CreateWithHttpContext<ProductCategoryController>(
                "", "TEST_USER", "Test User",
                new Mock<ILogger<ProductCategoryController>>().Object,
                CreateProdServiceMock(_prodRepo).Object,
                (catSvc ?? CreateCatServiceMock(_catRepo)).Object,
                (logSvc ?? CreateLogMock()).Object,
                CreateFullAccessAuth().Object);
        }

        // =========================================================
        // Grid（A 档：全部）
        // =========================================================

        [Fact]
        public async Task Grid_ReturnsAllCategories()
        {
            await InsertCategory("A", "root", "软件类");
            await InsertCategory("B", "root", "硬件类");
            await InsertCategory("C", "A", "软件子类");

            var ctrl = CreateController();

            var json = await ctrl.Grid();
            var data = JsonConvert.DeserializeObject<XHDData<Product_category>>(JObject.Parse(json).ToString());

            Assert.Equal(3, data.count);
            Assert.Equal(3, data.data.Count);
        }

        // =========================================================
        // Tree / TreeAll（A 档：递归 children）
        // =========================================================

        [Fact]
        public async Task TreeAll_ReturnsRecursiveChildren()
        {
            await InsertCategory("A", "root", "一级类目");
            await InsertCategory("B", "A", "二级类目");
            await InsertCategory("C", "B", "三级类目");
            await InsertCategory("X", "root", "另一支");

            var ctrl = CreateController();

            var json = await ctrl.TreeAll();
            var arr = JArray.Parse(json);

            // 根下挂 A 与 X 两支
            Assert.Equal(2, arr.Count);
            var a = arr.First(t => (string)t["id"]! == "A");
            var x = arr.First(t => (string)t["id"]! == "X");
            Assert.Equal("一级类目", (string)a["title"]!);

            // A 的直接子级是 B
            var level2 = (JArray)a["children"]!;
            Assert.Single(level2);
            Assert.Equal("B", (string)level2[0]["id"]!);

            // B 的直接子级是 C（递归到第三层）
            var level3 = (JArray)level2[0]["children"]!;
            Assert.Single(level3);
            Assert.Equal("C", (string)level3[0]["id"]!);

            // X 是叶子，没有 children 节点
            Assert.Null(x["children"]);
        }

        [Fact]
        public async Task Tree_WithId_PrependsRootNode()
        {
            await InsertCategory("A", "root", "一级类目");
            await InsertCategory("B", "A", "二级类目");

            var ctrl = CreateController();

            var json = await ctrl.Tree("A");
            var arr = JArray.Parse(json);

            // Controller 在结果最前面插入 "无" 根节点
            Assert.Equal("root", (string)arr[0]["id"]!);
            Assert.Equal("无", (string)arr[0]["title"]!);
            Assert.Equal("", (string)arr[0]["parentid"]!);

            // 传入的 id 的下级（B 的 parentid==A）被排除，A 保留但无 children
            var a = arr.First(t => (string)t["id"]! == "A");
            Assert.Equal("一级类目", (string)a["title"]!);
            Assert.Null(a["children"]);
        }

        // =========================================================
        // Save（B 档：新增 + 更新两态）
        // =========================================================

        [Fact]
        public async Task Save_New_PersistsRow()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new Product_category { category_name = "新类目", parentid = "root" });
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var rows = await _fsql.Select<Product_category>().Where(a => a.category_name == "新类目").ToListAsync();
            Assert.Single(rows);
            Assert.False(string.IsNullOrEmpty(rows[0].id));
            Assert.Equal("root", rows[0].parentid);
        }

        [Fact]
        public async Task Save_Update_ChangesCategoryName()
        {
            await InsertCategory("A", "root", "旧类目");
            var logSvc = CreateLogMock();
            var ctrl = CreateController(logSvc: logSvc);

            var json = await ctrl.Save(new Product_category
            {
                id = "A",
                category_name = "新类目",
                parentid = "root",
                create_id = "SEED",
                create_time = new DateTime(2024, 1, 1)
            });
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<Product_category>().Where(a => a.id == "A").FirstAsync();
            Assert.Equal("新类目", row.category_name);
            logSvc.Verify(l => l.UpdateLog(It.IsAny<Sys_log>()), Times.Once);
        }

        [Fact]
        public async Task Save_Update_Nonexistent_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new Product_category { id = "NO_EXIST", category_name = "幽灵类目" });
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("找不到数据", (string)obj["msg"]!);
        }

        // =========================================================
        // Delete（B 档：存在 + 不存在 + 约束保护）
        // =========================================================

        [Fact]
        public async Task Delete_Existing_RemovesRow()
        {
            await InsertCategory("A", "root", "软件类");
            var logSvc = CreateLogMock();
            var ctrl = CreateController(logSvc: logSvc);

            var json = await ctrl.Delete("A");
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<Product_category>().Where(a => a.id == "A").FirstAsync();
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
        public async Task Delete_WithChildren_ReturnsError()
        {
            await InsertCategory("A", "root", "父类");
            await InsertCategory("B", "A", "子类");

            var ctrl = CreateController();

            var json = await ctrl.Delete("A");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("含有下级", (string)obj["msg"]!);

            var stillThere = await _fsql.Select<Product_category>().Where(a => a.id == "A").CountAsync();
            Assert.Equal(1, stillThere);
        }

        [Fact]
        public async Task Delete_WithProduct_ReturnsError()
        {
            await InsertCategory("A", "root", "软件类");
            await _fsql.Insert(new Product
            {
                id = "PRD1",
                product_name = "CRM 软件",
                category_id = "A",
                price = 100m
            }).ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Delete("A");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("含有产品", (string)obj["msg"]!);
        }
    }

    /// <summary>
    /// 产品线端点测试（A 查询 + B 落库）。
    /// </summary>
    public class ProductTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly ProductRepository _prodRepo;
        private readonly Sale_order_detailsRepository _orderDetailRepo;

        public ProductTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _prodRepo = new ProductRepository(_fsql);
            _orderDetailRepo = new Sale_order_detailsRepository(_fsql);
        }

        public void Dispose() => _fsql?.Dispose();

        // ============ 测试数据工厂 ============

        private Task<int> InsertProduct(string id, string name, string categoryId = "", decimal price = 100m)
            => _fsql.Insert(new Product
            {
                id = id,
                product_name = name,
                category_id = categoryId,
                price = price,
                create_id = "SEED",
                create_time = new DateTime(2024, 1, 1)
            }).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        private static Mock<IProductService> CreateProdServiceMock(ProductRepository repo)
        {
            var mock = new Mock<IProductService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Product, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<Product, bool>> e, int p, int l, string o) => repo.GridAsync(e, p, l, o));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Product, bool>>>()))
                .Returns((Expression<Func<Product, bool>> e) => repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string>()))
                .Returns((Expression<Func<Product, bool>> e, string o) => repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.AddAsync(It.IsAny<Product>()))
                .Returns((Product m) => repo.AddAsync(m));
            mock.Setup(s => s.UpdateAsync(It.IsAny<Product>()))
                .Returns((Product m) => repo.UpdateAsync(m));
            mock.Setup(s => s.DeleteAsync(It.IsAny<string>()))
                .Returns((string id) => repo.DeleteAsync(id));
            return mock;
        }

        /// <summary>
        /// Delete 预筛依赖的 ISale_order_detailsService.GridAsync(exp,page,limit) 桥接到真实仓储。
        /// </summary>
        private static Mock<ISale_order_detailsService> CreateOrderDetailServiceMock(Sale_order_detailsRepository repo)
        {
            var mock = new Mock<ISale_order_detailsService>();
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sale_order_details, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Sale_order_details, bool>> e, int p, int l) => repo.GridAsync(e, p, l));
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

        private ProductController CreateController(
            Mock<IProductService>? prodSvc = null,
            Mock<ISys_logService>? logSvc = null,
            string queryString = "",
            string userId = "TEST_USER")
        {
            return TestControllerHelper.CreateWithHttpContext<ProductController>(
                queryString, userId, "Test User",
                new Mock<ILogger<ProductController>>().Object,
                (prodSvc ?? CreateProdServiceMock(_prodRepo)).Object,
                CreateOrderDetailServiceMock(_orderDetailRepo).Object,
                (logSvc ?? CreateLogMock()).Object,
                CreateFullAccessAuth().Object);
        }

        // =========================================================
        // Grid（A 档：分页 + 按类别过滤）
        // =========================================================

        [Fact]
        public async Task Grid_EmptyDB_ReturnsZeroCount()
        {
            var ctrl = CreateController();

            var json = await ctrl.Grid(new PageView<Product> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<Product>>(JObject.Parse(json).ToString());

            Assert.Equal(0, data.count);
        }

        [Fact]
        public async Task Grid_FilterByCategory_ReturnsMatched()
        {
            await InsertProduct("PRD1", "产品A", "CAT1");
            await InsertProduct("PRD2", "产品B", "CAT1");
            await InsertProduct("PRD3", "产品C", "CAT2");

            var ctrl = CreateController(queryString: "?T_type=CAT1");

            var json = await ctrl.Grid(new PageView<Product> { Page = 1, Limit = 30 });
            var data = JsonConvert.DeserializeObject<XHDData<Product>>(JObject.Parse(json).ToString());

            Assert.Equal(2, data.count);
            Assert.All(data.data, p => Assert.Equal("CAT1", p.category_id));
        }

        [Fact]
        public async Task Grid_WithPagination_ReturnsLimitedRows()
        {
            for (int i = 0; i < 5; i++)
            {
                await InsertProduct($"PRD{i}", $"产品{i}");
            }

            var ctrl = CreateController();

            var json = await ctrl.Grid(new PageView<Product> { Page = 1, Limit = 2 });
            var data = JsonConvert.DeserializeObject<XHDData<Product>>(JObject.Parse(json).ToString());

            Assert.Equal(5, data.count);
            Assert.Equal(2, data.data.Count);
        }

        // =========================================================
        // Save（B 档：新增 + 更新两态）
        // =========================================================

        [Fact]
        public async Task Save_New_PersistsRow()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new Product
            {
                product_name = "新产品",
                category_id = "CAT1",
                price = 200m,
                unit = "套"
            });
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var rows = await _fsql.Select<Product>().Where(a => a.product_name == "新产品").ToListAsync();
            Assert.Single(rows);
            Assert.False(string.IsNullOrEmpty(rows[0].id));
            Assert.Equal("CAT1", rows[0].category_id);
            Assert.Equal(200m, rows[0].price);
        }

        [Fact]
        public async Task Save_Update_ChangesProductName()
        {
            await InsertProduct("PRD1", "旧产品", "CAT1");
            var logSvc = CreateLogMock();
            var ctrl = CreateController(logSvc: logSvc);

            var json = await ctrl.Save(new Product
            {
                id = "PRD1",
                product_name = "新产品",
                category_id = "CAT1",
                price = 100m,
                create_id = "SEED",
                create_time = new DateTime(2024, 1, 1)
            });
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<Product>().Where(a => a.id == "PRD1").FirstAsync();
            Assert.Equal("新产品", row.product_name);
            logSvc.Verify(l => l.UpdateLog(It.IsAny<Sys_log>()), Times.Once);
        }

        [Fact]
        public async Task Save_Update_Nonexistent_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new Product { id = "NO_EXIST", product_name = "幽灵产品" });
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("找不到数据", (string)obj["msg"]!);
        }

        // =========================================================
        // Delete（B 档：存在 + 不存在 + 订单保护）
        // =========================================================

        [Fact]
        public async Task Delete_Existing_RemovesRow()
        {
            await InsertProduct("PRD1", "产品A", "CAT1");
            var logSvc = CreateLogMock();
            var ctrl = CreateController(logSvc: logSvc);

            var json = await ctrl.Delete("PRD1");
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<Product>().Where(a => a.id == "PRD1").FirstAsync();
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
        public async Task Delete_ProductWithOrderDetail_ReturnsError()
        {
            await InsertProduct("PRD1", "产品A", "CAT1");
            await _fsql.Insert(new Sale_order_details { id = "OD1", product_id = "PRD1" })
                .ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Delete("PRD1");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("含有订单", (string)obj["msg"]!);

            var stillThere = await _fsql.Select<Product>().Where(a => a.id == "PRD1").CountAsync();
            Assert.Equal(1, stillThere);
        }
    }
}
