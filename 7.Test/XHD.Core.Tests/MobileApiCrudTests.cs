using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using XHD.Core.Common;
using XHD.Core.Common.DEncrypt;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.Services;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.38 P3-W7：移动 API 线（APIController）业务 CRUD 真测试。
    /// 覆盖 客户/联系人/跟进/订单/合同/回款/参数/产品/员工 共 20 个 action。
    /// 走真实 SQLite 内存库 + 真实 Service/Repository，每个 Save 都断言落库真实，
    /// 每个 List 都覆盖 有数据 / 空表 / customer_id 过滤（跨客户数据不串）三态。
    /// </summary>
    public class MobileApiCrudTests : IDisposable
    {
        private readonly IFreeSql _fsql;

        private const string EmpId = "EMP-CRUD-1";
        private const string EmpName = "张三";
        private const string CustId = "CUST-1";
        private const string CustId2 = "CUST-2";
        private const string ContactId = "CT-1";
        private const string FollowId = "FW-1";
        private const string ProductId = "PRD-1";

        // OrderInfo / ContractInfo / ReceiveInfo 用 PageValidate.checkID 校验，必须是合法 Guid
        private const string OrderId = "11111111-1111-1111-1111-111111111111";
        private const string OrderId2 = "22222222-2222-2222-2222-222222222222";
        private const string MissingGuid = "55555555-5555-5555-5555-555555555555";
        private const string ContractId = "33333333-3333-3333-3333-333333333333";
        private const string ReceiveId = "44444444-4444-4444-4444-444444444444";

        public MobileApiCrudTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static hr_employee NewEmployee(string id, string uid, string name)
            => new hr_employee
            {
                id = id,
                uid = uid,
                name = name,
                pwd = PasswordHasher.Hash(MD5Comm.MD5Hash("123456")),
                status = 1,
                tel = "13800000000",
                create_time = new DateTime(2024, 1, 1)
            };

        private static CRM_Customer NewCustomer(string id, string name, string empId, string tel = "13811111111")
            => new CRM_Customer
            {
                id = id,
                cus_name = name,
                cus_tel = tel,
                emp_id = empId,
                create_id = empId,
                create_time = new DateTime(2024, 1, 1),
                state = 0,
                isDelete = 0,
                isPrivate = 1,
                sn = "CU-" + id
            };

        private static CRM_Contact NewContact(string id, string customerId, string name)
            => new CRM_Contact
            {
                id = id,
                customer_id = customerId,
                C_name = name,
                C_mob = "13900000000",
                create_id = EmpId,
                create_time = new DateTime(2024, 1, 1),
                isDelete = 0
            };

        private static CRM_follow NewFollow(string id, string customerId, string content)
            => new CRM_follow
            {
                id = id,
                customer_id = customerId,
                employee_id = EmpId,
                follow_content = content,
                follow_time = new DateTime(2024, 1, 1),
                isDelete = 0
            };

        private static Sale_order NewOrder(string id, string customerId, decimal amount)
            => new Sale_order
            {
                id = id,
                customer_id = customerId,
                emp_id = EmpId,
                create_id = EmpId,
                create_time = new DateTime(2024, 1, 1),
                Order_amount = amount,
                discount_amount = 0,
                total_amount = amount,
                sn = "SO-" + id,
                Order_details = string.Empty,
                isDelete = 0
            };

        private static Sale_contract NewContract(string id, string customerId, string name, decimal amount)
            => new Sale_contract
            {
                id = id,
                customer_id = customerId,
                Contract_name = name,
                Contract_amount = amount,
                Our_Contractor_id = EmpId,
                create_id = EmpId,
                create_time = new DateTime(2024, 1, 1),
                Sign_date = new DateTime(2024, 1, 1),
                sn = "HT-" + id,
                isDelete = 0
            };

        private static Finance_Receive NewReceive(string id, string orderId, string num, decimal amount)
            => new Finance_Receive
            {
                id = id,
                order_id = orderId,
                Receive_num = num,
                Receive_amount = amount,
                Receive_date = new DateTime(2024, 1, 1),
                Pay_type_id = "PT-1",
                Payee_id = EmpId,
                create_id = EmpId,
                create_time = new DateTime(2024, 1, 1),
                isDelete = 0
            };

        private static Product NewProduct(string id, string name)
            => new Product
            {
                id = id,
                product_name = name,
                price = 10m,
                create_time = new DateTime(2024, 1, 1),
                isDelete = 0
            };

        private static Sys_Param NewParam(string id, string type, string name, int order)
            => new Sys_Param
            {
                id = id,
                params_type = type,
                params_name = name,
                params_order = order,
                create_time = new DateTime(2024, 1, 1),
                isDelete = 0
            };

        /// <summary>
        /// 种入标准数据集：员工 + 两个客户（含各自的联系人/跟进/订单/合同/回款）。
        /// </summary>
        private async Task SeedFullAsync()
        {
            await _fsql.Insert(NewEmployee(EmpId, "empcrud", EmpName)).ExecuteAffrowsAsync();
            await _fsql.Insert(NewCustomer(CustId, "黄豆科技", EmpId, "13811111111")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewCustomer(CustId2, "绿豆餐饮", EmpId, "13822222222")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewContact(ContactId, CustId, "王五")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewContact("CT-2", CustId2, "赵六")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewFollow(FollowId, CustId, "电话回访")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewFollow("FW-2", CustId2, "上门拜访")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewOrder(OrderId, CustId, 1000m)).ExecuteAffrowsAsync();
            await _fsql.Insert(NewOrder(OrderId2, CustId2, 2000m)).ExecuteAffrowsAsync();
            await _fsql.Insert(NewContract(ContractId, CustId, "年度服务合同", 5000m)).ExecuteAffrowsAsync();
            await _fsql.Insert(NewReceive(ReceiveId, OrderId, "RC-001", 600m)).ExecuteAffrowsAsync();
            await _fsql.Insert(NewReceive("RC-002", OrderId2, "RC-002", 800m)).ExecuteAffrowsAsync();
            await _fsql.Insert(NewProduct(ProductId, "小豆产品A")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewProduct("PRD-2", "其他产品B")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewParam("PT-1", "pay_type", "银行转账", 1)).ExecuteAffrowsAsync();
            await _fsql.Insert(NewParam("PT-2", "pay_type", "微信支付", 2)).ExecuteAffrowsAsync();
            await _fsql.Insert(NewParam("CT-1", "cus_type", "国有企业", 1)).ExecuteAffrowsAsync();
        }

        // ============ Controller 装配 ============

        /// <summary>
        /// 构造 APIController：13 个业务服务全部使用真实实现（直连 SQLite 内存库），
        /// 仅 IDBAuthService 用 Mock 放开数据/按钮权限（权限规则不属本文件测试范围）。
        /// authHeader 为 null 表示不携带 Authorization 头（模拟未登录）。
        /// </summary>
        private APIController CreateController(string? authHeader = null)
        {
            var authMock = new Mock<IDBAuthService>();
            authMock.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 4, empList = new List<string>() });
            authMock.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var ctrl = TestControllerHelper.CreateWithHttpContext<APIController>(
                string.Empty, EmpId, EmpName,
                new hr_employeeService(new hr_employeeRepository(_fsql)),
                new CRM_CustomerService(new CRM_CustomerRepository(_fsql)),
                authMock.Object,
                new CRM_followService(new CRM_followRepository(_fsql)),
                new Sale_orderService(new Sale_orderRepository(_fsql)),
                new Sale_contractService(new Sale_contractRepository(_fsql)),
                new Finance_ReceiveService(new Finance_ReceiveRepository(_fsql)),
                new CRM_ContactService(new CRM_ContactRepository(_fsql)),
                new Sys_ParamService(new Sys_ParamRepository(_fsql)),
                new Sys_logService(new Sys_logRepository(_fsql)),
                new ProductService(new ProductRepository(_fsql)),
                new Sale_order_detailsService(new Sale_order_detailsRepository(_fsql)),
                new Sale_contract_attaService(new Sale_contract_attaRepository(_fsql)),
                _fsql);

            // TestControllerHelper 只能设 QueryString，Header 必须创建后手动设
            if (authHeader != null)
            {
                ctrl.ControllerContext.HttpContext.Request.Headers["Authorization"] = authHeader;
            }

            return ctrl;
        }

        /// <summary>
        /// 按移动端协议造 token：DESEncrypt.Encrypt("id,过期时间")，
        /// 时间格式与 APIController.Login 完全一致（yyyy-MM-dd hh:mm:ss）。
        /// </summary>
        private static string MakeToken(string empId, DateTime? expiry = null)
            => DESEncrypt.Encrypt($"{empId},{(expiry ?? DateTime.Now.AddMonths(1)).ToString("yyyy-MM-dd hh:mm:ss")}");

        /// <summary>带有效 token 的 controller（绝大多数业务 action 的默认装配）。</summary>
        private APIController CreateAuthedController()
            => CreateController("Bearer " + MakeToken(EmpId));

        /// <summary>断言 checkToken 闸门未通过时统一返回的认证失败错误。</summary>
        private static async Task AssertAuthBlocked(Task<string> call)
        {
            var obj = JObject.Parse(await call);
            Assert.Equal(-9, (int)obj["code"]!);
            Assert.Equal("认证失败！", (string)obj["msg"]!);
        }

        // =========================================================
        #region 客户 CustomerList（:237）/ CustomerSave（:273）
        // =========================================================

        [Fact]
        public async Task CustomerList_WithData_ReturnsAll()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.CustomerList(null, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var names = ((JArray)obj["data"]!).Select(d => (string)d["cus_name"]!).ToList();
            Assert.Contains("黄豆科技", names);
            Assert.Contains("绿豆餐饮", names);
        }

        [Fact]
        public async Task CustomerList_EmptyTable_ReturnsEmpty()
        {
            await _fsql.Insert(NewEmployee(EmpId, "empcrud", EmpName)).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.CustomerList(null, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task CustomerList_SerchTxt_FiltersByNameAndTel()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            // 按名称匹配
            var byName = JObject.Parse(await ctrl.CustomerList("黄豆", 1, 50));
            Assert.Equal(1, (int)byName["count"]!);
            Assert.Equal("黄豆科技", (string)((JArray)byName["data"]!)[0]["cus_name"]!);

            // 按电话匹配
            var byTel = JObject.Parse(await ctrl.CustomerList("13822222222", 1, 50));
            Assert.Equal(1, (int)byTel["count"]!);
            Assert.Equal("绿豆餐饮", (string)((JArray)byTel["data"]!)[0]["cus_name"]!);
        }

        [Fact]
        public async Task CustomerSave_New_PersistsWithSnAndCreator()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();
            var model = new CRM_Customer
            {
                id = string.Empty,
                cus_name = "新客户",
                cus_tel = "13833333333",
                emp_id = EmpId
            };

            var obj = JObject.Parse(await ctrl.CustomerSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            // 落库断言：不只是看返回 code，查表确认字段值
            var fetched = await _fsql.Select<CRM_Customer>().Where(a => a.cus_name == "新客户").FirstAsync();
            Assert.NotNull(fetched);
            Assert.Equal("13833333333", fetched.cus_tel);
            Assert.Equal(EmpId, fetched.create_id);
            Assert.Equal(EmpId, fetched.emp_id);
            Assert.Equal(0, fetched.isDelete);
            Assert.False(string.IsNullOrEmpty(fetched.sn));
        }

        [Fact]
        public async Task CustomerSave_Update_PersistsChanges()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();
            var model = NewCustomer(CustId, "改名后的客户", EmpId, "13844444444");

            var obj = JObject.Parse(await ctrl.CustomerSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<CRM_Customer>().Where(a => a.id == CustId).FirstAsync();
            Assert.Equal("改名后的客户", fetched.cus_name);
            Assert.Equal("13844444444", fetched.cus_tel);
        }

        [Fact]
        public async Task CustomerSave_Update_NotFound_ReturnsError()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();
            var model = NewCustomer("CUST-NOT-EXIST", "幽灵客户", EmpId);

            var obj = JObject.Parse(await ctrl.CustomerSave(model));

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("找不到数据！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task CustomerActions_WithoutToken_Blocked()
        {
            await SeedFullAsync();
            var ctrl = CreateController(null);

            await AssertAuthBlocked(ctrl.CustomerList(null, 1, 50));
            await AssertAuthBlocked(ctrl.CustomerSave(new CRM_Customer { cus_name = "无令牌" }));
        }

        #endregion

        // =========================================================
        #region 联系人 ContactList（:376）/ ContactSave（:414）
        // =========================================================

        [Fact]
        public async Task ContactList_WithData_ReturnsAll()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ContactList(null, null));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
        }

        [Fact]
        public async Task ContactList_EmptyTable_ReturnsEmpty()
        {
            await _fsql.Insert(NewEmployee(EmpId, "empcrud", EmpName)).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ContactList(null, null));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task ContactList_ByCustomerId_FiltersOtherCustomer()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ContactList(null, CustId));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal(ContactId, (string)((JArray)obj["data"]!)[0]["id"]!);

            // 跨客户数据不串
            var ids = ((JArray)obj["data"]!).Select(d => (string)d["id"]!).ToList();
            Assert.DoesNotContain("CT-2", ids);
        }

        [Fact]
        public async Task ContactSave_New_PersistsWithCreator()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();
            var model = new CRM_Contact
            {
                id = string.Empty,
                customer_id = CustId,
                C_name = "新联系人",
                C_mob = "13911111111"
            };

            var obj = JObject.Parse(await ctrl.ContactSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<CRM_Contact>().Where(a => a.C_name == "新联系人").FirstAsync();
            Assert.NotNull(fetched);
            Assert.Equal(CustId, fetched.customer_id);
            Assert.Equal(EmpId, fetched.create_id);
            Assert.Equal("13911111111", fetched.C_mob);
        }

        [Fact]
        public async Task ContactSave_Update_PersistsChanges()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();
            var model = NewContact(ContactId, CustId, "改名联系人");
            model.C_mob = "13922222222";

            var obj = JObject.Parse(await ctrl.ContactSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<CRM_Contact>().Where(a => a.id == ContactId).FirstAsync();
            Assert.Equal("改名联系人", fetched.C_name);
            Assert.Equal("13922222222", fetched.C_mob);
        }

        [Fact]
        public async Task ContactActions_WithoutToken_Blocked()
        {
            await SeedFullAsync();
            var ctrl = CreateController(null);

            await AssertAuthBlocked(ctrl.ContactList(null, null));
            await AssertAuthBlocked(ctrl.ContactSave(new CRM_Contact { C_name = "无令牌" }));
        }

        #endregion

        // =========================================================
        #region 跟进 FollowList（:509）/ FollowSave（:550）
        // =========================================================

        [Fact]
        public async Task FollowList_WithData_ReturnsAll()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.FollowList(null, null, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var contents = ((JArray)obj["data"]!).Select(d => (string)d["follow_content"]!).ToList();
            Assert.Contains("电话回访", contents);
            Assert.Contains("上门拜访", contents);
        }

        [Fact]
        public async Task FollowList_EmptyTable_ReturnsEmpty()
        {
            await _fsql.Insert(NewEmployee(EmpId, "empcrud", EmpName)).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.FollowList(null, null, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task FollowList_ByCustomerId_FiltersOtherCustomer()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.FollowList(null, CustId, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal(FollowId, (string)((JArray)obj["data"]!)[0]["id"]!);

            var ids = ((JArray)obj["data"]!).Select(d => (string)d["id"]!).ToList();
            Assert.DoesNotContain("FW-2", ids);
        }

        [Fact]
        public async Task FollowSave_New_PersistsAndUpdatesCustomerLastFollow()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();
            var model = new CRM_follow
            {
                id = string.Empty,
                customer_id = CustId,
                follow_content = "新增跟进记录",
                follow_type_id = "FT-1"
            };

            var obj = JObject.Parse(await ctrl.FollowSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<CRM_follow>().Where(a => a.follow_content == "新增跟进记录").FirstAsync();
            Assert.NotNull(fetched);
            Assert.Equal(CustId, fetched.customer_id);
            Assert.Equal(EmpId, fetched.employee_id);
            Assert.NotNull(fetched.follow_time);

            // LastFollow 副作用：客户最后跟进时间被更新
            var cust = await _fsql.Select<CRM_Customer>().Where(a => a.id == CustId).FirstAsync();
            Assert.NotNull(cust.lastfollow);
        }

        [Fact]
        public async Task FollowSave_Update_PersistsChanges()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();
            var model = NewFollow(FollowId, CustId, "修改后的跟进内容");

            var obj = JObject.Parse(await ctrl.FollowSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<CRM_follow>().Where(a => a.id == FollowId).FirstAsync();
            Assert.Equal("修改后的跟进内容", fetched.follow_content);
        }

        [Fact]
        public async Task FollowActions_WithoutToken_Blocked()
        {
            await SeedFullAsync();
            var ctrl = CreateController(null);

            await AssertAuthBlocked(ctrl.FollowList(null, null, 1, 50));
            await AssertAuthBlocked(ctrl.FollowSave(new CRM_follow { follow_content = "无令牌" }));
        }

        #endregion

        // =========================================================
        #region 订单 OrderList（:650）/ OrderInfo（:695）/ OrderDetails（:722）/ OrderSave（:739）
        // =========================================================

        [Fact]
        public async Task OrderList_WithData_HasSerialnumberAlias()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.OrderList(null, null, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            // APP 按 A 版契约读取 Serialnumber，API 边界别名必须存在且等于 sn
            var row = (JObject)((JArray)obj["data"]!)[0];
            Assert.Equal(row["sn"]!.ToString(), row["Serialnumber"]!.ToString());
            Assert.False(string.IsNullOrEmpty(row["Serialnumber"]!.ToString()));
        }

        [Fact]
        public async Task OrderList_EmptyTable_ReturnsEmpty()
        {
            await _fsql.Insert(NewEmployee(EmpId, "empcrud", EmpName)).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.OrderList(null, null, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task OrderList_ByCustomerId_FiltersOtherCustomer()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.OrderList(null, CustId, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal(OrderId, (string)((JArray)obj["data"]!)[0]["id"]!);

            var ids = ((JArray)obj["data"]!).Select(d => (string)d["id"]!).ToList();
            Assert.DoesNotContain(OrderId2, ids);
        }

        [Fact]
        public async Task OrderInfo_ValidId_ReturnsSingleObject()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.OrderInfo(OrderId));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            // 移动端详情页约定 res.data 为对象本身（不是数组）
            var row = (JObject)obj["data"]!;
            Assert.Equal(OrderId, (string)row["id"]!);
            Assert.Equal("SO-" + OrderId, (string)row["Serialnumber"]!);
            Assert.Equal(1000m, (decimal)row["Order_amount"]!);
        }

        [Fact]
        public async Task OrderInfo_NotFound_ReturnsError()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.OrderInfo(MissingGuid));

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("数据不存在", (string)obj["msg"]!);
        }

        [Fact]
        public async Task OrderInfo_BadId_ReturnsParamError()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.OrderInfo("NOT_A_GUID"));

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数错误", (string)obj["msg"]!);
        }

        [Fact]
        public async Task OrderDetails_ByOrderId_ReturnsDetails()
        {
            await SeedFullAsync();
            await _fsql.Insert(new Sale_order_details
            {
                id = "OD-1",
                order_id = OrderId,
                product_id = ProductId,
                price = 10m,
                quantity = 2,
                amount = 20m
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sale_order_details
            {
                id = "OD-2",
                order_id = OrderId,
                product_id = "PRD-2",
                price = 5m,
                quantity = 4,
                amount = 20m
            }).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.OrderDetails(OrderId));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var productIds = ((JArray)obj["data"]!).Select(d => (string)d["product_id"]!).ToList();
            Assert.Contains(ProductId, productIds);
            Assert.Contains("PRD-2", productIds);
        }

        [Fact]
        public async Task OrderSave_New_PersistsOrderAndDetails()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            string details = new JArray
            {
                new JObject
                {
                    ["product_id"] = ProductId,
                    ["price"] = 40m,
                    ["quantity"] = 2,
                    ["amount"] = 80m
                }
            }.ToString(Formatting.None);

            var model = new Sale_order
            {
                id = string.Empty,
                customer_id = CustId,
                Order_amount = 100m,
                discount_amount = 20m,
                Order_details = details
            };

            var obj = JObject.Parse(await ctrl.OrderSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            // 订单落库：合计金额 = 订单金额 - 优惠金额
            var order = await _fsql.Select<Sale_order>()
                .Where(a => a.customer_id == CustId && a.Order_amount == 100m && a.discount_amount == 20m)
                .FirstAsync();
            Assert.NotNull(order);
            Assert.Equal(80m, order.total_amount);
            Assert.Equal(EmpId, order.create_id);
            // Order_details 在保存前被清空（只作为传输载体）
            Assert.Equal(string.Empty, order.Order_details);

            // 详情落库
            var detailRows = await _fsql.Select<Sale_order_details>().Where(a => a.order_id == order.id).ToListAsync();
            Assert.Single(detailRows);
            Assert.Equal(ProductId, detailRows[0].product_id);
            Assert.Equal(40m, detailRows[0].price);
            Assert.Equal(2, detailRows[0].quantity);
            Assert.Equal(80m, detailRows[0].amount);
        }

        [Fact]
        public async Task OrderSave_Update_PersistsChangesAndReplacesDetails()
        {
            await SeedFullAsync();
            await _fsql.Insert(new Sale_order_details
            {
                id = "OD-OLD",
                order_id = OrderId,
                product_id = ProductId,
                price = 10m,
                quantity = 1,
                amount = 10m
            }).ExecuteAffrowsAsync();

            var ctrl = CreateAuthedController();

            string details = new JArray
            {
                new JObject
                {
                    ["product_id"] = "PRD-2",
                    ["price"] = 50m,
                    ["quantity"] = 3,
                    ["amount"] = 150m
                }
            }.ToString(Formatting.None);

            var model = new Sale_order
            {
                id = OrderId,
                customer_id = CustId,
                Order_amount = 200m,
                discount_amount = 20m,
                Order_details = details
            };

            var obj = JObject.Parse(await ctrl.OrderSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<Sale_order>().Where(a => a.id == OrderId).FirstAsync();
            Assert.Equal(200m, fetched.Order_amount);
            Assert.Equal(180m, fetched.total_amount);

            // 旧详情被删除，新详情被写入
            var detailRows = await _fsql.Select<Sale_order_details>().Where(a => a.order_id == OrderId).ToListAsync();
            Assert.Single(detailRows);
            Assert.Equal("PRD-2", detailRows[0].product_id);
            Assert.Equal(150m, detailRows[0].amount);
        }

        [Fact]
        public async Task OrderActions_WithoutToken_Blocked()
        {
            await SeedFullAsync();
            var ctrl = CreateController(null);

            await AssertAuthBlocked(ctrl.OrderList(null, null, 1, 50));
            await AssertAuthBlocked(ctrl.OrderInfo(OrderId));
            await AssertAuthBlocked(ctrl.OrderDetails(OrderId));
            await AssertAuthBlocked(ctrl.OrderSave(new Sale_order { customer_id = CustId }));
        }

        #endregion

        // =========================================================
        #region 合同 ContractList（:878）/ ContractInfo（:919）/ ContractAtta（:946）/ ContractSave（:963）
        // =========================================================

        [Fact]
        public async Task ContractList_WithData_HasSerialnumberAlias()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ContractList(null, null, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            var row = (JObject)((JArray)obj["data"]!)[0];
            Assert.Equal(row["sn"]!.ToString(), row["Serialnumber"]!.ToString());
            Assert.Equal(ContractId, (string)row["id"]!);
        }

        [Fact]
        public async Task ContractList_EmptyTable_ReturnsEmpty()
        {
            await _fsql.Insert(NewEmployee(EmpId, "empcrud", EmpName)).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ContractList(null, null, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task ContractList_ByCustomerId_FiltersOtherCustomer()
        {
            await SeedFullAsync();
            await _fsql.Insert(NewContract("33333333-3333-3333-3333-333333333334", CustId2, "绿豆合同", 1000m)).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ContractList(null, CustId, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal(ContractId, (string)((JArray)obj["data"]!)[0]["id"]!);
        }

        [Fact]
        public async Task ContractInfo_ValidId_ReturnsSingleObject()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ContractInfo(ContractId));

            Assert.Equal(0, (int)obj["code"]!);
            var row = (JObject)obj["data"]!;
            Assert.Equal(ContractId, (string)row["id"]!);
            Assert.Equal("HT-" + ContractId, (string)row["Serialnumber"]!);
            Assert.Equal("年度服务合同", (string)row["Contract_name"]!);
            Assert.Equal(5000m, (decimal)row["Contract_amount"]!);
        }

        [Fact]
        public async Task ContractInfo_NotFound_ReturnsError()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ContractInfo(MissingGuid));

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("数据不存在", (string)obj["msg"]!);
        }

        [Fact]
        public async Task ContractAtta_ByContractId_ReturnsAttas()
        {
            await SeedFullAsync();
            await _fsql.Insert(new Sale_contract_atta
            {
                id = "AT-1",
                contract_id = ContractId,
                file_name = "合同.pdf",
                real_name = "2024/a.pdf",
                file_size = 1024,
                create_id = EmpId,
                create_time = new DateTime(2024, 1, 1)
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sale_contract_atta
            {
                id = "AT-2",
                contract_id = ContractId,
                file_name = "附件.docx",
                real_name = "2024/b.docx",
                file_size = 2048,
                create_id = EmpId,
                create_time = new DateTime(2024, 1, 1)
            }).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ContractAtta(ContractId));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var names = ((JArray)obj["data"]!).Select(d => (string)d["file_name"]!).ToList();
            Assert.Contains("合同.pdf", names);
            Assert.Contains("附件.docx", names);
        }

        [Fact]
        public async Task ContractSave_New_PersistsWithOurContractor()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();
            var model = new Sale_contract
            {
                id = string.Empty,
                customer_id = CustId,
                Contract_name = "新签合同",
                Contract_amount = 8000m,
                sn = "HT-NEW"
            };

            var obj = JObject.Parse(await ctrl.ContractSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<Sale_contract>().Where(a => a.Contract_name == "新签合同").FirstAsync();
            Assert.NotNull(fetched);
            Assert.Equal(CustId, fetched.customer_id);
            Assert.Equal(EmpId, fetched.Our_Contractor_id);
            Assert.Equal(8000m, fetched.Contract_amount);
            Assert.Equal("HT-NEW", fetched.sn);
            Assert.NotNull(fetched.create_time);
        }

        [Fact]
        public async Task ContractSave_Update_PersistsChanges()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();
            var model = NewContract(ContractId, CustId, "改签合同", 9000m);

            var obj = JObject.Parse(await ctrl.ContractSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<Sale_contract>().Where(a => a.id == ContractId).FirstAsync();
            Assert.Equal("改签合同", fetched.Contract_name);
            Assert.Equal(9000m, fetched.Contract_amount);
        }

        [Fact]
        public async Task ContractActions_WithoutToken_Blocked()
        {
            await SeedFullAsync();
            var ctrl = CreateController(null);

            await AssertAuthBlocked(ctrl.ContractList(null, null, 1, 50));
            await AssertAuthBlocked(ctrl.ContractInfo(ContractId));
            await AssertAuthBlocked(ctrl.ContractAtta(ContractId));
            await AssertAuthBlocked(ctrl.ContractSave(new Sale_contract { customer_id = CustId }));
        }

        #endregion

        // =========================================================
        #region 回款 ReceiveList（:1064）/ ReceiveSave（:1103）/ ReceiveInfo（:1316）
        // =========================================================

        [Fact]
        public async Task ReceiveList_WithData_ReturnsAll()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ReceiveList(null, null, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var nums = ((JArray)obj["data"]!).Select(d => (string)d["Receive_num"]!).ToList();
            Assert.Contains("RC-001", nums);
            Assert.Contains("RC-002", nums);
        }

        [Fact]
        public async Task ReceiveList_EmptyTable_ReturnsEmpty()
        {
            await _fsql.Insert(NewEmployee(EmpId, "empcrud", EmpName)).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ReceiveList(null, null, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task ReceiveList_ByCustomerId_FiltersViaOrder()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            // 按 Order.customer.id 过滤，跨客户数据不串
            var obj = JObject.Parse(await ctrl.ReceiveList(null, CustId, 1, 50));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1, (int)obj["count"]!);
            Assert.Equal(ReceiveId, (string)((JArray)obj["data"]!)[0]["id"]!);
        }

        [Fact]
        public async Task ReceiveInfo_ValidId_ReturnsSingleObject()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ReceiveInfo(ReceiveId));

            Assert.Equal(0, (int)obj["code"]!);
            var row = (JObject)obj["data"]!;
            Assert.Equal(ReceiveId, (string)row["id"]!);
            Assert.Equal("RC-001", (string)row["Receive_num"]!);
            Assert.Equal(600m, (decimal)row["Receive_amount"]!);
            Assert.Equal(OrderId, (string)row["order_id"]!);
        }

        [Fact]
        public async Task ReceiveInfo_NotFound_ReturnsError()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ReceiveInfo(MissingGuid));

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("数据不存在", (string)obj["msg"]!);
        }

        [Fact]
        public async Task ReceiveSave_New_PersistsWithCreator()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();
            var model = new Finance_Receive
            {
                id = string.Empty,
                order_id = OrderId,
                Receive_num = "RC-NEW",
                Receive_amount = 300m,
                Receive_date = new DateTime(2024, 6, 1),
                Pay_type_id = "PT-1"
            };

            var obj = JObject.Parse(await ctrl.ReceiveSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<Finance_Receive>().Where(a => a.Receive_num == "RC-NEW").FirstAsync();
            Assert.NotNull(fetched);
            Assert.Equal(OrderId, fetched.order_id);
            Assert.Equal(EmpId, fetched.create_id);
            Assert.Equal(300m, fetched.Receive_amount);
            Assert.NotNull(fetched.create_time);
        }

        [Fact]
        public async Task ReceiveSave_Update_PersistsChanges()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();
            var model = NewReceive(ReceiveId, OrderId, "RC-001", 999m);
            model.Remarks = "调整回款";

            var obj = JObject.Parse(await ctrl.ReceiveSave(model));

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<Finance_Receive>().Where(a => a.id == ReceiveId).FirstAsync();
            Assert.Equal(999m, fetched.Receive_amount);
            Assert.Equal("调整回款", fetched.Remarks);
        }

        [Fact]
        public async Task ReceiveActions_WithoutToken_Blocked()
        {
            await SeedFullAsync();
            var ctrl = CreateController(null);

            await AssertAuthBlocked(ctrl.ReceiveList(null, null, 1, 50));
            await AssertAuthBlocked(ctrl.ReceiveInfo(ReceiveId));
            await AssertAuthBlocked(ctrl.ReceiveSave(new Finance_Receive { order_id = OrderId }));
        }

        #endregion

        // =========================================================
        #region 参数 / 产品 / 员工 paramsCombo（:1231）/ ProductList（:1253）/ EmployeeList（:1276）
        // =========================================================

        [Fact]
        public async Task ParamsCombo_ByType_ReturnsFilteredAndOrdered()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.paramsCombo("pay_type"));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, (int)obj["count"]!);
            var rows = (JArray)obj["data"]!;
            Assert.All(rows, r => Assert.Equal("pay_type", (string)r["params_type"]!));
            // 按 params_order 排序
            Assert.Equal("银行转账", (string)rows[0]["params_name"]!);
            Assert.Equal("微信支付", (string)rows[1]["params_name"]!);
        }

        [Fact]
        public async Task ProductList_WithData_FiltersBySerchTxt()
        {
            await SeedFullAsync();
            var ctrl = CreateAuthedController();

            var all = JObject.Parse(await ctrl.ProductList(null));
            Assert.Equal(0, (int)all["code"]!);
            Assert.Equal(2, (int)all["count"]!);

            var filtered = JObject.Parse(await ctrl.ProductList("小豆"));
            Assert.Equal(0, (int)filtered["code"]!);
            Assert.Equal(1, (int)filtered["count"]!);
            Assert.Equal("小豆产品A", (string)((JArray)filtered["data"]!)[0]["product_name"]!);
        }

        [Fact]
        public async Task ProductList_EmptyTable_ReturnsEmpty()
        {
            await _fsql.Insert(NewEmployee(EmpId, "empcrud", EmpName)).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.ProductList(null));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task EmployeeList_WithData_FiltersBySerchTxt()
        {
            await SeedFullAsync();
            await _fsql.Insert(NewEmployee("EMP-CRUD-2", "empcrud2", "李四")).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var all = JObject.Parse(await ctrl.EmployeeList(null));
            Assert.Equal(0, (int)all["code"]!);
            Assert.Equal(2, (int)all["count"]!);

            var filtered = JObject.Parse(await ctrl.EmployeeList("张三"));
            Assert.Equal(0, (int)filtered["code"]!);
            Assert.Equal(1, (int)filtered["count"]!);
            Assert.Equal(EmpName, (string)((JArray)filtered["data"]!)[0]["name"]!);
        }

        [Fact]
        public async Task EmployeeList_SerchTxt_NoMatch_ReturnsEmpty()
        {
            await _fsql.Insert(NewEmployee(EmpId, "empcrud", EmpName)).ExecuteAffrowsAsync();
            var ctrl = CreateAuthedController();

            var obj = JObject.Parse(await ctrl.EmployeeList("不存在的员工"));

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (int)obj["count"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task ParamsAndProductAndEmployee_WithoutToken_Blocked()
        {
            await SeedFullAsync();
            var ctrl = CreateController(null);

            await AssertAuthBlocked(ctrl.paramsCombo("pay_type"));
            await AssertAuthBlocked(ctrl.ProductList(null));
            await AssertAuthBlocked(ctrl.EmployeeList(null));
        }

        #endregion
    }
}
