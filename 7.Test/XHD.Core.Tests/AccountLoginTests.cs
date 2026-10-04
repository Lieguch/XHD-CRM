using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

using XHD.Core.Common;
using XHD.Core.Common.DEncrypt;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.Services;
using XHD.Core.View.Controllers;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.38 系统基座线（一）：AccountController 登录链路真测试。
    /// 登录链路此前零真测试，本文件逐条覆盖 Login() :191 的 5 条分支
    /// （验证码 / AES 密钥缺失 / AES 解密失败 / 服务层业务错误 / 登录成功），
    /// 并通过真实 hr_employeeService + hr_employeeRepository 覆盖服务层 4 条分支
    /// （密码错 / 限制登录 / 透明升级 / 成功），外加 SignOut / Index / ValiCode。
    /// 全部走真实 SQLite 内存库与真实服务，不 Mock 业务逻辑。
    /// </summary>
    public class AccountLoginTests : IDisposable
    {
        private readonly IFreeSql _fsql;

        /// <summary>
        /// 测试用 AES 密钥（16 字符，满足 CBC 的 Key/IV 长度约束：IV 取 key 末 16 字符）。
        /// </summary>
        private const string TestAesKey = "XHD_TEST_AES_KEY!";

        /// <summary>
        /// 造数用的「另一把」AES 密钥，仅用于制造解密失败场景。
        /// </summary>
        private const string OtherAesKey = "OTHER_KEY_16CHAR";

        private const string PlainPassword = "123456";

        public AccountLoginTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 装配辅助 ============

        /// <summary>
        /// 字典版 ISession：DefaultHttpContext 默认不带 Session 特性，
        /// 登录链路依赖 HttpContext.Session，必须手动装配。
        /// </summary>
        public class DictSession : ISession
        {
            private readonly Dictionary<string, byte[]> _store = new Dictionary<string, byte[]>();

            public string Id { get; } = Guid.NewGuid().ToString("N");

            public bool IsAvailable => true;

            public IEnumerable<string> Keys => _store.Keys.ToList();

            public Task LoadAsync() => Task.CompletedTask;

            public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task CommitAsync() => Task.CompletedTask;

            public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public void Clear() => _store.Clear();

            public void Remove(string key) => _store.Remove(key);

            public void Set(string key, byte[] value) => _store[key] = value;

            public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
        }

        /// <summary>
        /// SignInAsync / SignOutAsync 是扩展方法，从 RequestServices 解析 IAuthenticationService，
        /// 必须在 HttpContext 上挂一个 ServiceProvider，否则登录成功分支必崩。
        /// </summary>
        private static IServiceProvider BuildRequestServices()
        {
            var authMock = new Mock<IAuthenticationService>();
            authMock.Setup(a => a.SignInAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask);
            authMock.Setup(a => a.SignOutAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string>(),
                    It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask);
            return new ServiceCollection()
                .AddSingleton(authMock.Object)
                .BuildServiceProvider();
        }

        /// <summary>
        /// 构造一个装配好 Session / Form / RequestServices 的 AccountController。
        /// 9 个构造参数全部使用真实 Service（Service 内部使用真实 Repository）。
        /// </summary>
        /// <param name="captcha">写入 session 的验证码；null 表示不写（模拟 session 为空）</param>
        /// <param name="formValicode">表单回传的验证码；null 表示表单不带该字段</param>
        /// <param name="aesKeyForm">表单回传的 aes_key；null 表示表单不带该字段</param>
        /// <param name="sessionAesKey">写入 session 的 AES_Key 兜底值；null 表示不写</param>
        private AccountController CreateController(
            string captcha = "验证码",
            string formValicode = "验证码",
            string aesKeyForm = TestAesKey,
            string sessionAesKey = null,
            string userId = "TEST_USER",
            string userName = "Test User")
        {
            var ctrl = TestControllerHelper.CreateWithHttpContext<AccountController>(
                string.Empty, userId, userName,
                new hr_employeeService(new hr_employeeRepository(_fsql)),
                new Sys_MenuService(new Sys_MenuRepository(_fsql)),
                new Sys_ButtonService(new Sys_ButtonRepository(_fsql)),
                new Sys_Param_ProvincesService(new Sys_Param_ProvincesRepository(_fsql)),
                new Sys_Param_CityService(new Sys_Param_CityRepository(_fsql)),
                new Sys_Param_TypeService(new Sys_Param_TypeRepository(_fsql)),
                new Sys_logService(new Sys_logRepository(_fsql)),
                new Sys_infoService(new Sys_infoRepository(_fsql)),
                new Mock<ILogger<AccountController>>().Object);

            var ctx = ctrl.ControllerContext.HttpContext;

            // Session：写入验证码（模拟 CreateImageAsync 已生成验证码）
            var session = new DictSession();
            if (!string.IsNullOrEmpty(captcha))
            {
                session.SetString("CaptchaCode", captcha);
            }
            if (!string.IsNullOrEmpty(sessionAesKey))
            {
                session.SetString("AES_Key", sessionAesKey);
            }
            ctx.Session = session;

            // Form：valicode / aes_key（[FromBody] 不生效，直接构造 FormCollection）
            var form = new Dictionary<string, StringValues>();
            if (formValicode != null)
            {
                form["valicode"] = formValicode;
            }
            if (aesKeyForm != null)
            {
                form["aes_key"] = aesKeyForm;
            }
            ctx.Request.Form = new FormCollection(form);

            ctx.RequestServices = BuildRequestServices();
            return ctrl;
        }

        /// <summary>
        /// 插入员工；pwd 传入已哈希的最终落库值。
        /// </summary>
        private async Task InsertEmployeeAsync(string id, string uid, string storedPwd, int canlogin = 1)
        {
            await _fsql.Insert(new hr_employee
            {
                id = id,
                uid = uid,
                name = uid,
                pwd = storedPwd,
                canlogin = canlogin
            }).ExecuteAffrowsAsync();
        }

        /// <summary>
        /// 规范密钥 = MD5(明文).ToUpper()，与移动端协议和服务端校验路径一致。
        /// </summary>
        private static string SecretOf(string plain) => PasswordHasher.CanonicalSecret(plain);

        /// <summary>
        /// 前端真实加密后的密码密文（与 Login() 的 AesDecrypt 配对）。
        /// </summary>
        private static string EncryptedPwd(string plain = PlainPassword, string key = TestAesKey)
            => AESEncrypt.AesEncrypt(plain, key);

        private static void AssertError(string json, int code, string msg)
        {
            var obj = JObject.Parse(json);
            Assert.Equal(code, (int)obj["code"]!);
            Assert.Equal(msg, (string)obj["msg"]!);
        }

        // ============ Login 分支 1：验证码 ============

        [Fact]
        public async Task Login_WrongValicode_ReturnsCaptchaError()
        {
            // session 验证码与表单验证码不一致 → 验证码错误
            await InsertEmployeeAsync("E1", "user1", PasswordHasher.Hash(SecretOf(PlainPassword)));

            var ctrl = CreateController(captcha: "ABCD", formValicode: "WXYZ");

            var json = await ctrl.Login(new hr_employee { uid = "user1", pwd = EncryptedPwd() });

            AssertError(json, -1, "验证码错误");
        }

        [Fact]
        public async Task Login_MissingCaptchaInSession_ReturnsCaptchaError()
        {
            // session 无验证码（首次直接调登录接口）也走验证码错误分支
            await InsertEmployeeAsync("E1", "user1", PasswordHasher.Hash(SecretOf(PlainPassword)));

            var ctrl = CreateController(captcha: null, formValicode: "ABCD");

            var json = await ctrl.Login(new hr_employee { uid = "user1", pwd = EncryptedPwd() });

            AssertError(json, -1, "验证码错误");
        }

        [Fact]
        public async Task Login_CorrectValicode_IgnoresCase()
        {
            // 校验使用 OrdinalIgnoreCase，前端常见的大小写差异应放行
            await InsertEmployeeAsync("E1", "user1", PasswordHasher.Hash(SecretOf(PlainPassword)));

            var ctrl = CreateController(captcha: "AB2D", formValicode: "ab2d");

            var json = await ctrl.Login(new hr_employee { uid = "user1", pwd = EncryptedPwd() });

            Assert.Equal(0, (int)JObject.Parse(json)["code"]!);
        }

        // ============ Login 分支 2：AES 密钥 ============

        [Fact]
        public async Task Login_MissingAesKey_ReturnsKeyMissingError()
        {
            // 表单不带 aes_key 且 session 无 AES_Key → 加密密钥缺失
            await InsertEmployeeAsync("E1", "user1", PasswordHasher.Hash(SecretOf(PlainPassword)));

            var ctrl = CreateController(aesKeyForm: null, sessionAesKey: null);

            var json = await ctrl.Login(new hr_employee { uid = "user1", pwd = EncryptedPwd() });

            AssertError(json, -1, "加密密钥缺失，请刷新页面重试");
        }

        [Fact]
        public async Task Login_EmptyAesKeyInForm_FallsBackToSession()
        {
            // [Sprint 10.11] 表单 aes_key 为空时兜底读 session 的 AES_Key
            await InsertEmployeeAsync("E1", "user1", PasswordHasher.Hash(SecretOf(PlainPassword)));

            var ctrl = CreateController(aesKeyForm: string.Empty, sessionAesKey: TestAesKey);

            var json = await ctrl.Login(new hr_employee { uid = "user1", pwd = EncryptedPwd() });

            Assert.Equal(0, (int)JObject.Parse(json)["code"]!);
        }

        // ============ Login 分支 3：AES 解密失败 ============

        [Fact]
        public async Task Login_AesDecryptFails_ReturnsSystemError()
        {
            // 密文用「另一把 key」加密，Login 用 TestAesKey 解密必抛异常 → -9 系统错误
            await InsertEmployeeAsync("E1", "user1", PasswordHasher.Hash(SecretOf(PlainPassword)));

            var ctrl = CreateController();
            var model = new hr_employee
            {
                uid = "user1",
                pwd = AESEncrypt.AesEncrypt(PlainPassword, OtherAesKey)
            };

            var json = await ctrl.Login(model);

            AssertError(json, -9, "系统错误！");
        }

        // ============ Login 分支 4：服务层业务错误（真实服务） ============

        [Fact]
        public async Task Login_WrongPassword_ReturnsCredentialError()
        {
            // 库里存的是「654321」的哈希，提交「123456」→ 用户名或密码错误
            await InsertEmployeeAsync("E1", "user1", PasswordHasher.Hash(SecretOf("654321")));

            var ctrl = CreateController();

            var json = await ctrl.Login(new hr_employee { uid = "user1", pwd = EncryptedPwd() });

            AssertError(json, -1, "用户名或密码错误！");
        }

        [Fact]
        public async Task Login_UnknownUser_ReturnsCredentialError()
        {
            // 用户不存在与密码错误返回同一句，不泄露用户是否存在
            await InsertEmployeeAsync("E1", "user1", PasswordHasher.Hash(SecretOf(PlainPassword)));

            var ctrl = CreateController();

            var json = await ctrl.Login(new hr_employee { uid = "nobody", pwd = EncryptedPwd() });

            AssertError(json, -1, "用户名或密码错误！");
        }

        [Fact]
        public async Task Login_CanloginZero_ReturnsLoginRestricted()
        {
            // canlogin == 0 且提交模型 id 不是 admin → 限制登录
            await InsertEmployeeAsync("E1", "user1", PasswordHasher.Hash(SecretOf(PlainPassword)), canlogin: 0);

            var ctrl = CreateController();

            var json = await ctrl.Login(new hr_employee { uid = "user1", pwd = EncryptedPwd() });

            AssertError(json, -1, "此用户限制登录！");
        }

        [Fact]
        public async Task Login_CanloginZeroButPostedAsAdmin_BypassesRestriction()
        {
            // 记录现有行为：限制登录判定用的是「客户端回传的 model.id」而非库内员工 id，
            // 回传 id="admin" 时限制被绕过（密码仍需校验通过）。
            await InsertEmployeeAsync("E1", "user1", PasswordHasher.Hash(SecretOf(PlainPassword)), canlogin: 0);

            var ctrl = CreateController();

            var json = await ctrl.Login(new hr_employee { uid = "user1", id = "admin", pwd = EncryptedPwd() });

            Assert.Equal(0, (int)JObject.Parse(json)["code"]!);
        }

        [Fact]
        public async Task Login_CanloginZeroLegacyPwd_NotUpgraded()
        {
            // 透明升级只在「完全登录成功后」触发：被限制登录的旧格式密码保持原样
            string legacy = SecretOf(PlainPassword);
            await InsertEmployeeAsync("E1", "user1", legacy, canlogin: 0);

            var ctrl = CreateController();
            await ctrl.Login(new hr_employee { uid = "user1", pwd = EncryptedPwd() });

            string current = await _fsql.Select<hr_employee>().Where(a => a.id == "E1").FirstAsync(a => a.pwd);
            Assert.Equal(legacy, current);
            Assert.True(PasswordHasher.NeedsRehash(current));
        }

        // ============ Login 分支 5：成功 + 透明升级（真实服务 + 真实 DB） ============

        [Fact]
        public async Task Login_LegacyMd5Pwd_TransparentUpgradeToPbkdf2()
        {
            // 存量无盐 MD5（规范密钥本身）登录成功后应被改写为 PBKDF2
            string legacy = SecretOf(PlainPassword);
            await InsertEmployeeAsync("E1", "user1", legacy);

            var ctrl = CreateController();

            var json = await ctrl.Login(new hr_employee { uid = "user1", pwd = EncryptedPwd() });

            Assert.Equal(0, (int)JObject.Parse(json)["code"]!);

            string upgraded = await _fsql.Select<hr_employee>().Where(a => a.id == "E1").FirstAsync(a => a.pwd);
            Assert.NotEqual(legacy, upgraded);
            Assert.StartsWith("XHD-PBKDF2$", upgraded);
            Assert.False(PasswordHasher.NeedsRehash(upgraded));
            // 升级后用同一规范密钥仍可校验通过
            Assert.True(PasswordHasher.Verify(upgraded, SecretOf(PlainPassword)));
        }

        [Fact]
        public async Task Login_Pbkdf2Pwd_SuccessWithoutRehash()
        {
            // 已是 PBKDF2 格式时登录成功不改动 pwd
            string hashed = PasswordHasher.Hash(SecretOf(PlainPassword));
            await InsertEmployeeAsync("E1", "user1", hashed);

            var ctrl = CreateController();

            var json = await ctrl.Login(new hr_employee { uid = "user1", pwd = EncryptedPwd() });

            Assert.Equal(0, (int)JObject.Parse(json)["code"]!);

            string current = await _fsql.Select<hr_employee>().Where(a => a.id == "E1").FirstAsync(a => a.pwd);
            Assert.Equal(hashed, current);
        }

        [Fact]
        public async Task Login_Success_WritesLoginLog()
        {
            // 成功路径调用真实 Sys_logService.LoginLog，断言日志真写进 DB
            await InsertEmployeeAsync("E1", "user1", PasswordHasher.Hash(SecretOf(PlainPassword)));

            var ctrl = CreateController();

            var json = await ctrl.Login(new hr_employee { uid = "user1", pwd = EncryptedPwd() });

            Assert.Equal(0, (int)JObject.Parse(json)["code"]!);

            var logs = await _fsql.Select<Sys_log>().Where(a => a.EventType == "用户登录").ToListAsync();
            Assert.Single(logs);
            Assert.Equal("E1", logs[0].EventID);
            Assert.Equal("user1", logs[0].UserName);
            Assert.Equal("127.0.0.1", logs[0].IPStreet);
        }

        // ============ SignOut ============

        [Fact]
        public async Task SignOut_ReturnsSuccess()
        {
            // SignOutAsync 同样需要 RequestServices 里的 IAuthenticationService
            var ctrl = CreateController();

            var json = await ctrl.SignOut();

            Assert.Equal(0, (int)JObject.Parse(json)["code"]!);
        }

        // ============ Index / ValiCode ============

        [Fact]
        public async Task Index_ReturnsViewAndSetsAesKey()
        {
            // 即使初始化数据 json 缺失（被 try/catch 兜底），登录页也必须渲染
            var ctrl = CreateController();

            var result = await ctrl.Index();

            var viewResult = Assert.IsType<ViewResult>(result);
            // AES Key 必须下发给页面，否则前端 JS 加密报错（Sprint 10.12 根因）
            string aesKey = (string)viewResult.ViewData["AES_Key"]!;
            Assert.NotNull(aesKey);
            Assert.Equal(16, aesKey.Length);
        }

        [Fact]
        public async Task ValiCode_ReturnsPngAndStoresCaptchaInSession()
        {
            var ctrl = CreateController();

            var result = await ctrl.ValiCode();

            var fileResult = Assert.IsType<FileContentResult>(result);
            Assert.Equal("image/png", fileResult.ContentType);
            Assert.NotEmpty(fileResult.FileContents);

            // 验证码写入 session，供后续 Login 校验
            string code = ctrl.HttpContext.Session.GetString("CaptchaCode");
            Assert.NotNull(code);
            Assert.Equal(4, code.Length);
        }
    }
}
