using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
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
    /// Sprint 10.38 P3-W7：移动 API 线（APIController）登录认证链路真测试。
    /// 覆盖 Login / checkToken / ModifyPWD / CountData 四个 action 的全部分支。
    /// 走真实 SQLite 内存库 + 真实 hr_employeeService + 真实 DES/密码学，禁止 Mock 业务逻辑。
    /// </summary>
    public class MobileApiAuthTests : IDisposable
    {
        private readonly IFreeSql _fsql;

        private const string EmpId = "EMP-AUTH-1";
        private const string AdminId = "EMP-AUTH-ADMIN";
        private const string EmpPhone = "13800000001";
        private const string EmpName = "张三";

        public MobileApiAuthTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static hr_employee NewEmployee(string id, string uid, string name, string pwd, int status = 1, string tel = EmpPhone)
        {
            return new hr_employee
            {
                id = id,
                uid = uid,
                name = name,
                pwd = pwd,
                status = status,
                tel = tel,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        private Task<int> InsertEmployeeAsync(hr_employee emp)
            => _fsql.Insert(emp).ExecuteAffrowsAsync();

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

        /// <summary>
        /// 移动端登录协议：客户端发送 MD5(明文).ToUpper()，即 PasswordHasher 的规范密钥。
        /// </summary>
        private static string ClientSecret(string plaintext)
            => MD5Comm.MD5Hash(plaintext);

        // =========================================================
        // Login（:77）
        // =========================================================

        [Fact]
        public async Task Login_Success_ReturnsTokenIdAndProfile()
        {
            var secret = ClientSecret("123456");
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(secret)));

            var ctrl = CreateController();
            var json = await ctrl.Login("emp1", secret);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var row = (JObject)((JArray)obj["data"]!)[0];
            Assert.Equal(EmpId, (string)row["id"]!);
            Assert.Equal(EmpName, (string)row["RealName"]!);
            Assert.Equal(EmpPhone, (string)row["phone"]!);
            Assert.False(string.IsNullOrEmpty((string)row["token"]!));
            Assert.False(string.IsNullOrEmpty((string)row["outtime"]!));
        }

        [Fact]
        public async Task Login_Success_TokenDecryptsToIdAndFutureExpiry()
        {
            var secret = ClientSecret("123456");
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(secret)));

            var ctrl = CreateController();
            var json = await ctrl.Login("emp1", secret);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var row = (JObject)((JArray)obj["data"]!)[0];

            // token 必须可被 DESEncrypt.Decrypt 解回，且拆出的 id 与员工一致
            string plain = DESEncrypt.Decrypt((string)row["token"]!);
            string[] parts = plain.Split(',');
            Assert.Equal(EmpId, parts[0]);

            DateTime expiry = DateTime.Parse(parts[1]);
            Assert.True(expiry > DateTime.Now, "token 过期时间应在未来");
            Assert.True(expiry < DateTime.Now.AddMonths(2), "token 过期时间应是一个月后");
        }

        [Fact]
        public async Task Login_LegacySaltlessMd5Pwd_TransparentUpgradeToPbkdf2()
        {
            // 存量记录：pwd 就是规范密钥本身（无盐 MD5）
            var secret = ClientSecret("123456");
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, secret));

            var ctrl = CreateController();
            var json = await ctrl.Login("emp1", secret);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            // 透明升级：DB 里的 pwd 应已被改写为 PBKDF2 格式，且仍能用同一规范密钥校验通过
            var fetched = await _fsql.Select<hr_employee>().Where(a => a.id == EmpId).FirstAsync();
            Assert.StartsWith("XHD-PBKDF2$", fetched.pwd);
            Assert.True(PasswordHasher.Verify(fetched.pwd, secret));
        }

        [Fact]
        public async Task Login_NewFormatPwd_NotRehashed()
        {
            var secret = ClientSecret("123456");
            string hashed = PasswordHasher.Hash(secret);
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, hashed));

            var ctrl = CreateController();
            var json = await ctrl.Login("emp1", secret);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            // 已是新格式，不应再重写
            var fetched = await _fsql.Select<hr_employee>().Where(a => a.id == EmpId).FirstAsync();
            Assert.Equal(hashed, fetched.pwd);
        }

        [Fact]
        public async Task Login_WrongPassword_ReturnsMismatchError()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("123456"))));

            var ctrl = CreateController();
            var json = await ctrl.Login("emp1", ClientSecret("999999"));
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("账号密码不匹配！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Login_NonAdminWithStatusZero_BlockedByStatusCheck()
        {
            // uid != "admin" 时追加 a.status == 1，status=0 的员工即便密码正确也登录失败
            var secret = ClientSecret("123456");
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(secret), status: 0));

            var ctrl = CreateController();
            var json = await ctrl.Login("emp1", secret);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("账号密码不匹配！", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Login_AdminUid_BypassesStatusCheck()
        {
            // uid == "admin" 绕过 status 检查：status=0 仍可登录
            var secret = ClientSecret("admin123");
            await InsertEmployeeAsync(NewEmployee(AdminId, "admin", "管理员", PasswordHasher.Hash(secret), status: 0, tel: "13900000000"));

            var ctrl = CreateController();
            var json = await ctrl.Login("admin", secret);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var row = (JObject)((JArray)obj["data"]!)[0];
            Assert.Equal(AdminId, (string)row["id"]!);
            Assert.Equal("管理员", (string)row["RealName"]!);
        }

        [Fact]
        public async Task Login_UnknownUid_ReturnsMismatchError()
        {
            var ctrl = CreateController();
            var json = await ctrl.Login("nobody", ClientSecret("123456"));
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("账号密码不匹配！", (string)obj["msg"]!);
        }

        // =========================================================
        // checkToken（:135）
        // =========================================================

        [Fact]
        public async Task CheckToken_ValidToken_ReturnsSuccess()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("123456"))));

            var ctrl = CreateController("Bearer " + MakeToken(EmpId));
            var obj = ctrl.checkToken();

            Assert.Equal(0, obj.Value<int>("code"));
        }

        [Fact]
        public async Task CheckToken_ExpiredToken_ReturnsExpiredError()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("123456"))));

            var ctrl = CreateController("Bearer " + MakeToken(EmpId, DateTime.Now.AddDays(-1)));
            var obj = ctrl.checkToken();

            Assert.Equal(-9, obj.Value<int>("code"));
            Assert.Equal("身份验证过期，请重新登录！", obj.Value<string>("msg"));
        }

        [Fact]
        public async Task CheckToken_NoAuthHeader_ReturnsAuthFailedError()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("123456"))));

            // 不携带 Authorization 头
            var ctrl = CreateController(null);
            var obj = ctrl.checkToken();

            Assert.Equal(-9, obj.Value<int>("code"));
            Assert.Equal("认证失败！", obj.Value<string>("msg"));
        }

        [Fact]
        public async Task CheckToken_EmptyAuthValue_ReturnsTokenError()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("123456"))));

            // Authorization 头存在但为空白：命中 "Token错误！" 分支
            var ctrl = CreateController("   ");
            var obj = ctrl.checkToken();

            Assert.Equal(-9, obj.Value<int>("code"));
            Assert.Equal("Token错误！", obj.Value<string>("msg"));
        }

        [Fact]
        public async Task CheckToken_TokenForUnknownUser_ReturnsUserNotFoundError()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("123456"))));

            // token 可解密、未过期，但 id 在员工表中不存在
            var ctrl = CreateController("Bearer " + MakeToken("EMP-DOES-NOT-EXIST"));
            var obj = ctrl.checkToken();

            Assert.Equal(-9, obj.Value<int>("code"));
            Assert.Equal("找不到此用户！", obj.Value<string>("msg"));
        }

        [Fact]
        public async Task CheckToken_GarbageCipher_ThrowsFormatException()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("123456"))));

            // 非法密文不是有效十六进制：DESEncrypt.Decrypt 解析字节时直接抛 FormatException
            // checkToken 无 try/catch，该异常会原样上抛——这是真实行为，在此显式锁定
            var ctrl = CreateController("Bearer NOT_HEXADECIMAL_STRING");

            Assert.Throws<FormatException>(() => ctrl.checkToken());
        }

        // =========================================================
        // ModifyPWD（:198）
        // =========================================================

        [Fact]
        public async Task ModifyPWD_WithCorrectOldPwd_UpdatesDbPwd()
        {
            string oldSecret = ClientSecret("old123");
            string newSecret = ClientSecret("new456");
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(oldSecret)));

            var ctrl = CreateController("Bearer " + MakeToken(EmpId));
            var json = await ctrl.ModifyPWD("old123", "new456");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal("修改成功", (string)obj["msg"]!);

            // 落库断言：pwd 真的被改成可被新密码 Verify 的 PBKDF2 值，且旧密码不再生效
            var fetched = await _fsql.Select<hr_employee>().Where(a => a.id == EmpId).FirstAsync();
            Assert.True(PasswordHasher.Verify(fetched.pwd, newSecret));
            Assert.False(PasswordHasher.Verify(fetched.pwd, oldSecret));
        }

        [Fact]
        public async Task ModifyPWD_WithWrongOldPwd_ReturnsError()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("realold"))));

            var ctrl = CreateController("Bearer " + MakeToken(EmpId));
            var json = await ctrl.ModifyPWD("wrongold", "new456");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("原密码不正确", (string)obj["msg"]!);

            // 未被改动
            var fetched = await _fsql.Select<hr_employee>().Where(a => a.id == EmpId).FirstAsync();
            Assert.True(PasswordHasher.Verify(fetched.pwd, ClientSecret("realold")));
        }

        [Fact]
        public async Task ModifyPWD_LegacySaltlessPwd_AcceptsCanonicalOldPwd()
        {
            // 存量无盐 MD5 记录：CanonicalSecret(明文) 与库存值相等，旧密码校验通过
            string legacy = ClientSecret("old123");
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, legacy));

            var ctrl = CreateController("Bearer " + MakeToken(EmpId));
            var json = await ctrl.ModifyPWD("old123", "new456");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var fetched = await _fsql.Select<hr_employee>().Where(a => a.id == EmpId).FirstAsync();
            Assert.True(PasswordHasher.Verify(fetched.pwd, ClientSecret("new456")));
        }

        [Fact]
        public async Task ModifyPWD_WithoutToken_ReturnsAuthFailedError()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("old123"))));

            var ctrl = CreateController(null);
            var json = await ctrl.ModifyPWD("old123", "new456");
            var obj = JObject.Parse(json);

            Assert.Equal(-9, (int)obj["code"]!);
            Assert.Equal("认证失败！", (string)obj["msg"]!);

            // 未被改动
            var fetched = await _fsql.Select<hr_employee>().Where(a => a.id == EmpId).FirstAsync();
            Assert.True(PasswordHasher.Verify(fetched.pwd, ClientSecret("old123")));
        }

        // =========================================================
        // CountData（:1195）
        // =========================================================

        [Fact]
        public async Task CountData_ReturnsExactCountsPerEmployee()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("123456"))));
            await InsertEmployeeAsync(NewEmployee("EMP-OTHER", "emp2", "李四", PasswordHasher.Hash(ClientSecret("123456"))));

            // 当前员工：3 客户 / 2 跟进 / 5 订单 / 1 合同
            for (int i = 0; i < 3; i++)
            {
                await _fsql.Insert(new CRM_Customer
                {
                    id = $"C{i}", cus_name = $"客户{i}", emp_id = EmpId, create_id = EmpId,
                    create_time = new DateTime(2024, 1, 1), isDelete = 0
                }).ExecuteAffrowsAsync();
            }
            for (int i = 0; i < 2; i++)
            {
                await _fsql.Insert(new CRM_follow
                {
                    id = $"F{i}", customer_id = $"C{i}", employee_id = EmpId,
                    follow_time = new DateTime(2024, 1, 1), isDelete = 0
                }).ExecuteAffrowsAsync();
            }
            for (int i = 0; i < 5; i++)
            {
                await _fsql.Insert(new Sale_order
                {
                    id = Guid.NewGuid().ToString(), customer_id = "C0", emp_id = EmpId,
                    create_id = EmpId, create_time = new DateTime(2024, 1, 1), isDelete = 0
                }).ExecuteAffrowsAsync();
            }
            await _fsql.Insert(new Sale_contract
            {
                id = Guid.NewGuid().ToString(), customer_id = "C0", Our_Contractor_id = EmpId,
                create_time = new DateTime(2024, 1, 1), isDelete = 0
            }).ExecuteAffrowsAsync();

            // 他人名下数据：各 1 条，必须被排除
            await _fsql.Insert(new CRM_Customer
            {
                id = "CX", cus_name = "他人客户", emp_id = "EMP-OTHER", create_id = "EMP-OTHER",
                create_time = new DateTime(2024, 1, 1), isDelete = 0
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new CRM_follow
            {
                id = "FX", customer_id = "CX", employee_id = "EMP-OTHER",
                follow_time = new DateTime(2024, 1, 1), isDelete = 0
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sale_order
            {
                id = Guid.NewGuid().ToString(), customer_id = "CX", emp_id = "EMP-OTHER",
                create_id = "EMP-OTHER", create_time = new DateTime(2024, 1, 1), isDelete = 0
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sale_contract
            {
                id = Guid.NewGuid().ToString(), customer_id = "CX", Our_Contractor_id = "EMP-OTHER",
                create_time = new DateTime(2024, 1, 1), isDelete = 0
            }).ExecuteAffrowsAsync();

            var ctrl = CreateController("Bearer " + MakeToken(EmpId));
            var json = await ctrl.CountData();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var row = (JObject)((JArray)obj["data"]!)[0];
            Assert.Equal(3, (int)row["cuscount"]!);
            Assert.Equal(2, (int)row["followcount"]!);
            Assert.Equal(5, (int)row["ordercount"]!);
            Assert.Equal(1, (int)row["contractcount"]!);
        }

        [Fact]
        public async Task CountData_NoData_ReturnsAllZeros()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("123456"))));

            var ctrl = CreateController("Bearer " + MakeToken(EmpId));
            var json = await ctrl.CountData();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var row = (JObject)((JArray)obj["data"]!)[0];
            Assert.Equal(0, (int)row["cuscount"]!);
            Assert.Equal(0, (int)row["followcount"]!);
            Assert.Equal(0, (int)row["ordercount"]!);
            Assert.Equal(0, (int)row["contractcount"]!);
        }

        [Fact]
        public async Task CountData_WithoutToken_ReturnsAuthFailedError()
        {
            await InsertEmployeeAsync(NewEmployee(EmpId, "emp1", EmpName, PasswordHasher.Hash(ClientSecret("123456"))));

            var ctrl = CreateController(null);
            var json = await ctrl.CountData();
            var obj = JObject.Parse(json);

            Assert.Equal(-9, (int)obj["code"]!);
            Assert.Equal("认证失败！", (string)obj["msg"]!);
        }
    }
}
