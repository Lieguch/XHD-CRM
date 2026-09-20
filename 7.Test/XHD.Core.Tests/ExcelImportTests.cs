using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;
using XHD.Core.Common;
using XHD.Core.Common.Excel;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.View.Controllers;
using XHD.Core.View.Helpers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 4 Wave 3 Excel 导入管线单元测试（23 个测试）。
    ///   #04 CustomerController.Import（客户导入，普通用户）
    ///   #05 CRM_ContactController.Import（联系人导入）
    ///   #06 CustomerController.AdminImport（管理员覆盖导入）
    /// 覆盖：Excel 解析、字段映射、CodeKey 解析、必填校验、重复检测、权限、Sys_log。
    /// </summary>
    public class ExcelImportTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly CRM_CustomerRepository _custRepo;
        private readonly CRM_ContactRepository _contactRepo;

        public ExcelImportTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _custRepo = new CRM_CustomerRepository(_fsql);
            _contactRepo = new CRM_ContactRepository(_fsql);
        }

        public void Dispose() => _fsql?.Dispose();

        // ============ 测试数据工厂 ============

        private static Sys_Param NewParam(string id, string name, string type) => new Sys_Param
        {
            id = id,
            params_name = name,
            params_type = type,
            isDelete = 0,
            create_time = DateTime.Now
        };

        private static Sys_Param_Provinces NewProvince(string id, string name) => new Sys_Param_Provinces
        {
            id = id,
            Provinces = name,
            Provinces_type = "sys",
            isDelete = 0
        };

        private static Sys_Param_City NewCity(string id, string name, string provinceId) => new Sys_Param_City
        {
            id = id,
            City = name,
            City_type = "sys",
            Provinces_id = provinceId,
            isDelete = 0
        };

        private static hr_employee NewEmployee(string id, string name) => new hr_employee
        {
            id = id,
            uid = "u_" + id,
            name = name,
            create_time = DateTime.Now
        };

        /// <summary>
        /// 构造一个内存 SQLite 中的最小代码表种子数据（行业/类型/级别/来源/省份/城市/员工/客户）。
        /// </summary>
        private async Task SeedCodeTablesAsync()
        {
            await _fsql.Insert(new List<Sys_Param>
            {
                NewParam("IND_IT", "IT", "cus_industry"),
                NewParam("IND_FIN", "金融", "cus_industry"),
                NewParam("TY_A", "VIP", "cus_type"),
                NewParam("TY_B", "普通", "cus_type"),
                NewParam("LV_H", "A级", "cus_level"),
                NewParam("LV_L", "B级", "cus_level"),
                NewParam("SR_WEB", "网络", "cus_source"),
                NewParam("SR_REF", "介绍", "cus_source")
            }).ExecuteAffrowsAsync();

            await _fsql.Insert(new List<Sys_Param_Provinces>
            {
                NewProvince("PR_HZ", "浙江"),
                NewProvince("PR_BJ", "北京")
            }).ExecuteAffrowsAsync();

            await _fsql.Insert(new List<Sys_Param_City>
            {
                NewCity("CT_HZ", "杭州", "PR_HZ"),
                NewCity("CT_BJ", "北京", "PR_BJ")
            }).ExecuteAffrowsAsync();

            await _fsql.Insert(new List<hr_employee>
            {
                NewEmployee("E1", "张三"),
                NewEmployee("E2", "李四")
            }).ExecuteAffrowsAsync();

            // 一个已有的客户（供 AdminImport 覆盖测试、Contact 的 customer_id 解析）
            await _fsql.Insert(new CRM_Customer
            {
                id = "C_EXISTING",
                cus_name = "老客户",
                cus_tel = "13900000001",
                emp_id = "E1",
                create_id = "E1",
                create_time = new DateTime(2023, 1, 1),
                isDelete = 0,
                isPrivate = 1,
                state = 0,
                sn = "CU-OLD"
            }).ExecuteAffrowsAsync();
        }

        // ============ 表单文件桩 ============

        /// <summary>
        /// 用 Moq 构造一个 IFormFile，从 byte[] 提供 Stream（供 Controller 测试用）。
        /// </summary>
        private static IFormFile CreateFormFile(string fileName, byte[] bytes, string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        {
            var mock = new Mock<IFormFile>();
            mock.Setup(f => f.FileName).Returns(fileName);
            mock.Setup(f => f.Length).Returns(bytes.Length);
            mock.Setup(f => f.ContentType).Returns(contentType);
            mock.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(bytes));
            return mock.Object;
        }

        /// <summary>
        /// 生成一个内存 Excel 文件（DataTable → byte[]），返回 bytes。
        /// </summary>
        private static byte[] BuildExcelBytes(IEnumerable<string> headers, IEnumerable<IEnumerable<object>> rows)
        {
            var dt = new DataTable();
            foreach (var h in headers) dt.Columns.Add(h, typeof(string));
            foreach (var r in rows)
            {
                var row = dt.NewRow();
                var idx = 0;
                foreach (var v in r) { row[idx++] = v ?? DBNull.Value; }
                dt.Rows.Add(row);
            }
            var ms = ExcelImportHelper.BuildExcelStream(dt, "Sheet1");
            return ms.ToArray();
        }

        // ============ Controller 装配辅助 ============

        /// <summary>
        /// 构造 CustomerController，桥接 service 到真实 Repository + 注入 IFreeSql。
        /// </summary>
        private CustomerController CreateCustomerController(
            bool grantImport = true,
            bool grantAdminImport = true,
            bool fullAccess = true,
            string userId = "TEST_USER")
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = fullAccess ? 4 : 1, empList = new List<string> { "E1" } });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string btn) =>
                {
                    var allowed = btn switch
                    {
                        "CRM_Customer|import" => grantImport,
                        "CRM_Customer|adminimport" => grantAdminImport,
                        _ => true
                    };
                    return Task.FromResult(allowed);
                });

            var svc = new Mock<ICRM_CustomerService>();
            svc.Setup(s => s.ImportAsync(It.IsAny<List<CRM_Customer>>()))
                .Returns((List<CRM_Customer> m) => _custRepo.ImportRangeAsync(m));
            svc.Setup(s => s.AdminImportAsync(It.IsAny<List<CRM_Customer>>()))
                .Returns((List<CRM_Customer> m) => _custRepo.AdminImportRangeAsync(m));

            var log = new Mock<ISys_logService>();
            log.Setup(s => s.DeleteLog(It.IsAny<Sys_log>()))
                .ReturnsAsync(1);

            var ctrl = new CustomerController(
                svc.Object,
                new Mock<ICRM_ContactService>().Object,
                new Mock<ICRM_followService>().Object,
                new Mock<ISale_orderService>().Object,
                new Mock<ISale_contractService>().Object,
                auth.Object,
                new Mock<ISys_ParamService>().Object,
                new Mock<ISys_Param_ProvincesService>().Object,
                log.Object,
                new Mock<ISys_infoService>().Object,
                _fsql);

            var httpCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
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
        /// 构造 CRM_ContactController，桥接 service 到真实 Repository。
        /// </summary>
        private CRM_ContactController CreateContactController(bool grantImport = true, string userId = "TEST_USER")
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 4, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string btn) => Task.FromResult(btn == "CRM_Contact|import" ? grantImport : true));

            var svc = new Mock<ICRM_ContactService>();
            svc.Setup(s => s.ImportAsync(It.IsAny<List<CRM_Contact>>()))
                .Returns((List<CRM_Contact> m) => _contactRepo.ImportRangeAsync(m));

            var log = new Mock<ISys_logService>();
            log.Setup(s => s.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);

            var ctrl = new CRM_ContactController(
                new Mock<Microsoft.Extensions.Logging.ILogger<CRM_ContactController>>().Object,
                svc.Object,
                new Mock<ICRM_followService>().Object,
                log.Object,
                auth.Object,
                _fsql);

            var httpCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
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

        // ============ #04 客户导入测试 ============

        [Fact]
        public async Task Import_NullFile_ReturnsError()
        {
            var ctrl = CreateCustomerController();
            var result = await ctrl.Import(null);
            var json = JObject.Parse(result);
            Assert.Equal(-1, json["code"].Value<int>());
            Assert.Contains("请选择要导入的文件", json["msg"].Value<string>());
        }

        [Fact]
        public async Task Import_NoPermission_ReturnsError()
        {
            var ctrl = CreateCustomerController(grantImport: false);
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "地址", "电话" },
                new[] { new object[] { "新客户", "地址1", "139" } });
            var file = CreateFormFile("test.xlsx", bytes);
            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            Assert.Equal(-1, json["code"].Value<int>());
            Assert.Contains("无权限", json["msg"].Value<string>());
        }

        [Fact]
        public async Task Import_SingleValidRow_ImportsSuccessfully()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateCustomerController();
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "地址", "电话", "行业", "省份", "城市" },
                new[] { new object[] { "新客户A", "addr", "13900000001", "IT", "浙江", "杭州" } });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            Assert.Equal(0, json["code"].Value<int>());
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(1, data["success"].Value<int>());
            Assert.Equal(0, data["error"].Value<int>());

            var saved = await _fsql.Select<CRM_Customer>().Where(c => c.cus_name == "新客户A").ToListAsync();
            Assert.Single(saved);
            Assert.Equal("IND_IT", saved[0].cus_industry_id);
            Assert.Equal("PR_HZ", saved[0].Provinces_id);
            Assert.Equal("CT_HZ", saved[0].City_id);
        }

        [Fact]
        public async Task Import_MultipleRows_ImportsAll()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateCustomerController();
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "电话", "行业" },
                new[]
                {
                    new object[] { "客户1", "1391", "IT" },
                    new object[] { "客户2", "1392", "金融" },
                    new object[] { "客户3", "1393", "IT" }
                });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(3, data["success"].Value<int>());
            Assert.Equal(0, data["error"].Value<int>());

            var count = await _fsql.Select<CRM_Customer>().CountAsync();
            // SeedCodeTablesAsync 已插入 1 个 "老客户"，本测试新增 3 个 → 共 4 个
            Assert.Equal(4, count);
        }

        [Fact]
        public async Task Import_MissingRequiredCustomerName_CountsAsError()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateCustomerController();
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "地址", "电话" },
                new[] { new object[] { "", "addr", "139" } });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            // 全部失败 → 返回 error code
            Assert.Equal(-1, json["code"].Value<int>());
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(0, data["success"].Value<int>());
            Assert.Equal(1, data["error"].Value<int>());
            Assert.Contains("不能为空", data["message"].Value<string>());
        }

        [Fact]
        public async Task Import_InvalidCodeKeyValue_CountsAsError()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateCustomerController();
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "行业" },
                new[] { new object[] { "客户1", "不存在的行业" } });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(0, data["success"].Value<int>());
            Assert.Equal(1, data["error"].Value<int>());
            Assert.Contains("找不到", data["message"].Value<string>());
        }

        [Fact]
        public async Task Import_DuplicateInBatch_CountsAsError()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateCustomerController();
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "电话" },
                new[]
                {
                    new object[] { "重复客户", "1391" },
                    new object[] { "重复客户", "1392" }
                });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(1, data["success"].Value<int>());
            Assert.Equal(1, data["error"].Value<int>());
        }

        [Fact]
        public async Task Import_DuplicateWithExistingInDb_CountsAsError()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateCustomerController();
            // "老客户" 已在 SeedCodeTablesAsync 中插入
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "电话" },
                new[]
                {
                    new object[] { "新客户X", "139" },
                    new object[] { "老客户", "139" }
                });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(1, data["success"].Value<int>());
            Assert.Equal(1, data["error"].Value<int>());
        }

        [Fact]
        public async Task Import_IsPrivateParsing_HandlesAllFormats()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateCustomerController();
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "公私" },
                new[]
                {
                    new object[] { "公客A", "公客" },
                    new object[] { "私客B", "私客" },
                    new object[] { "数字1C", "1" },
                    new object[] { "数字0D", "0" },
                    new object[] { "缺省E", null }
                });
            var file = CreateFormFile("test.xlsx", bytes);

            await ctrl.Import(file);

            var customers = await _fsql.Select<CRM_Customer>().ToListAsync();
            var byName = customers.ToDictionary(c => c.cus_name, c => c.isPrivate);
            Assert.Equal(1, byName["公客A"]);
            Assert.Equal(0, byName["私客B"]);
            Assert.Equal(1, byName["数字1C"]);
            Assert.Equal(0, byName["数字0D"]);
            Assert.Equal(1, byName["缺省E"]); // 默认公客
        }

        [Fact]
        public async Task Import_FieldMapping_AllColumnsMapped()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateCustomerController();
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "地址", "电话", "传真", "网址", "描述", "备注", "行业", "客户类型", "级别", "来源", "员工", "省份", "城市" },
                new[]
                {
                    new object[] { "全字段客户", "addr", "139", "555", "www.x.com", "desc", "note",
                                  "IT", "VIP", "A级", "网络", "张三", "浙江", "杭州" }
                });
            var file = CreateFormFile("test.xlsx", bytes);
            await ctrl.Import(file);

            var c = await _fsql.Select<CRM_Customer>().Where(x => x.cus_name == "全字段客户").FirstAsync();
            Assert.Equal("addr", c.cus_add);
            Assert.Equal("139", c.cus_tel);
            Assert.Equal("555", c.cus_fax);
            Assert.Equal("www.x.com", c.cus_website);
            Assert.Equal("desc", c.DesCripe);
            Assert.Equal("note", c.Remarks);
            Assert.Equal("IND_IT", c.cus_industry_id);
            Assert.Equal("TY_A", c.cus_type_id);
            Assert.Equal("LV_H", c.cus_level_id);
            Assert.Equal("SR_WEB", c.cus_source_id);
            Assert.Equal("E1", c.emp_id);
            Assert.Equal("PR_HZ", c.Provinces_id);
            Assert.Equal("CT_HZ", c.City_id);
            Assert.Equal("TEST_USER", c.create_id);
        }

        // ============ #06 管理员覆盖导入测试 ============

        [Fact]
        public async Task AdminImport_NoPermission_ReturnsError()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateCustomerController(grantAdminImport: false);
            var bytes = BuildExcelBytes(
                new[] { "客户名字" },
                new[] { new object[] { "管理员新客户" } });
            var file = CreateFormFile("test.xlsx", bytes);
            var result = await ctrl.AdminImport(file);
            var json = JObject.Parse(result);
            Assert.Equal(-1, json["code"].Value<int>());
            Assert.Contains("无权限", json["msg"].Value<string>());
        }

        [Fact]
        public async Task AdminImport_NewCustomers_AddsAll()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateCustomerController();
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "电话" },
                new[]
                {
                    new object[] { "管理员新增1", "1391" },
                    new object[] { "管理员新增2", "1392" }
                });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.AdminImport(file);
            var json = JObject.Parse(result);
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(2, data["success"].Value<int>());
            Assert.Equal(0, data["update"].Value<int>());
            Assert.Equal(0, data["error"].Value<int>());
        }

        [Fact]
        public async Task AdminImport_ExistingCustomers_UpdatesAll()
        {
            await SeedCodeTablesAsync();
            // 老客户 已在 seed 中
            var oldCus = await _fsql.Select<CRM_Customer>().Where(c => c.id == "C_EXISTING").FirstAsync();
            Assert.NotNull(oldCus);

            var ctrl = CreateCustomerController();
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "电话", "地址" },
                new[] { new object[] { "老客户", "13888888888", "新地址" } });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.AdminImport(file);
            var json = JObject.Parse(result);
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(0, data["success"].Value<int>());
            Assert.Equal(1, data["update"].Value<int>());
            Assert.Equal(0, data["error"].Value<int>());

            // 验证覆盖效果
            var updated = await _fsql.Select<CRM_Customer>().Where(c => c.id == "C_EXISTING").FirstAsync();
            Assert.Equal("13888888888", updated.cus_tel);
            Assert.Equal("新地址", updated.cus_add);
            // 保留管理字段
            Assert.Equal(oldCus.sn, updated.sn);
            Assert.Equal(oldCus.create_time, updated.create_time);
        }

        [Fact]
        public async Task AdminImport_MixedCustomers_AddAndUpdate()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateCustomerController();
            var bytes = BuildExcelBytes(
                new[] { "客户名字", "电话" },
                new[]
                {
                    new object[] { "新客户甲", "1391" },      // 新增
                    new object[] { "老客户", "1382" },        // 覆盖
                    new object[] { "新客户乙", "1393" }       // 新增
                });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.AdminImport(file);
            var json = JObject.Parse(result);
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(2, data["success"].Value<int>());
            Assert.Equal(1, data["update"].Value<int>());
            Assert.Equal(0, data["error"].Value<int>());
        }

        // ============ #05 联系人导入测试 ============

        [Fact]
        public async Task ContactImport_NoPermission_ReturnsError()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateContactController(grantImport: false);
            var bytes = BuildExcelBytes(
                new[] { "姓名", "客户名字" },
                new[] { new object[] { "联系人1", "老客户" } });
            var file = CreateFormFile("test.xlsx", bytes);
            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            Assert.Equal(-1, json["code"].Value<int>());
            Assert.Contains("无权限", json["msg"].Value<string>());
        }

        [Fact]
        public async Task ContactImport_MissingName_CountsAsError()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateContactController();
            var bytes = BuildExcelBytes(
                new[] { "姓名", "客户名字" },
                new[] { new object[] { "", "老客户" } });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            Assert.Equal(-1, json["code"].Value<int>());
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(0, data["success"].Value<int>());
            Assert.Equal(1, data["error"].Value<int>());
            Assert.Contains("姓名不能为空", data["message"].Value<string>());
        }

        [Fact]
        public async Task ContactImport_MissingCustomer_CountsAsError()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateContactController();
            var bytes = BuildExcelBytes(
                new[] { "姓名" },
                new[] { new object[] { "无客户联系人" } });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(1, data["error"].Value<int>());
            Assert.Contains("客户", data["message"].Value<string>());
        }

        [Fact]
        public async Task ContactImport_InvalidCustomerName_CountsAsError()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateContactController();
            var bytes = BuildExcelBytes(
                new[] { "姓名", "客户名字" },
                new[] { new object[] { "联系人X", "不存在的客户" } });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(0, data["success"].Value<int>());
            Assert.Equal(1, data["error"].Value<int>());
            Assert.Contains("找不到", data["message"].Value<string>());
        }

        [Fact]
        public async Task ContactImport_SingleValidRow_ImportsSuccessfully()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateContactController();
            var bytes = BuildExcelBytes(
                new[] { "姓名", "性别", "职务", "电话", "客户名字" },
                new[] { new object[] { "王五", "男", "经理", "13900000099", "老客户" } });
            var file = CreateFormFile("test.xlsx", bytes);

            var result = await ctrl.Import(file);
            var json = JObject.Parse(result);
            Assert.Equal(0, json["code"].Value<int>());
            var data = JObject.Parse(json["msg"].Value<string>());
            Assert.Equal(1, data["success"].Value<int>());
            Assert.Equal(0, data["error"].Value<int>());

            var contacts = await _fsql.Select<CRM_Contact>().Where(c => c.C_name == "王五").ToListAsync();
            Assert.Single(contacts);
            Assert.Equal(1, contacts[0].C_sex);
            Assert.Equal("经理", contacts[0].C_position);
            Assert.Equal("13900000099", contacts[0].C_tel);
            Assert.Equal("C_EXISTING", contacts[0].customer_id);
        }

        [Fact]
        public async Task ContactImport_SexParsing_ParsesCorrectly()
        {
            await SeedCodeTablesAsync();
            var ctrl = CreateContactController();
            var bytes = BuildExcelBytes(
                new[] { "姓名", "性别", "客户名字" },
                new[]
                {
                    new object[] { "男中文", "男", "老客户" },
                    new object[] { "女中文", "女", "老客户" },
                    new object[] { "数字1", "1", "老客户" },
                    new object[] { "数字2", "2", "老客户" },
                    new object[] { "缺省", null, "老客户" }
                });
            var file = CreateFormFile("test.xlsx", bytes);
            await ctrl.Import(file);

            var contacts = (await _fsql.Select<CRM_Contact>().ToListAsync()).ToDictionary(c => c.C_name, c => c.C_sex);
            Assert.Equal(1, contacts["男中文"]);
            Assert.Equal(2, contacts["女中文"]);
            Assert.Equal(1, contacts["数字1"]);
            Assert.Equal(2, contacts["数字2"]);
            Assert.Equal(0, contacts["缺省"]);
        }

        // ============ Helper 层直接测试 ============

        [Fact]
        public async Task Helper_ReadRowsAsync_EmptyFile_ReturnsEmptyList()
        {
            var bytes = BuildExcelBytes(
                new[] { "列1" },
                new[] { new object[] { "值1" } });
            using var ms = new MemoryStream(bytes);
            var rows = await ExcelImportHelper.ReadRowsAsync(ms);
            Assert.Single(rows);
        }

        [Fact]
        public async Task Helper_BuildExcelStream_CanRoundTrip()
        {
            var dt = new DataTable();
            dt.Columns.Add("A");
            dt.Columns.Add("B");
            var row = dt.NewRow();
            row["A"] = "1";
            row["B"] = "2";
            dt.Rows.Add(row);

            using var ms = ExcelImportHelper.BuildExcelStream(dt, "Sheet1");
            var bytes = ms.ToArray();
            Assert.True(bytes.Length > 0);
        }
    }
}
