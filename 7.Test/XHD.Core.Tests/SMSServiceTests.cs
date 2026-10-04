using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;

using FreeSql;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using Moq;
using Newtonsoft.Json.Linq;

using XHD.Core.Common;
using XHD.Core.Common.DEncrypt;
using XHD.Core.Common.SMS;
using XHD.Core.IRepository;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.Services;
using XHD.Core.View.Controllers;

using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.38 难点线：SMSController + SMSService 短信网关降级真测试。
    /// 覆盖：
    ///   - 网关未配置（DB 无 sms_no/sms_key）：GetBalance 返回 0、QueryStatus 返回空数组、
    ///     Send 返回配置错误 —— 全部不抛异常（降级契约的根因验证）。
    ///   - 网关已配置但不可达：helper 抛异常 / 返回错误码时降级为 0 / 空数组 / 中文错误消息。
    ///   - Send 的登录态、记录不存在、happy-path 落库（isSend/sendtime/check_id）。
    ///   - SMS CRUD：Save（含手机号校验、新增/编辑）、Delete（已发送拒删）、Grid（富化员工姓名）。
    /// 全部走真实 SQLite 内存库 + 真实 SMSRepository / Sys_infoService / SMSService，
    /// 仅 ISMSHelper（真实 HTTP 网关边界）用 Moq 桥接。
    /// </summary>
    public class SMSServiceTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly SMSRepository _smsRepo;
        private readonly hr_employeeRepository _empRepo;
        private readonly Mock<ISMSHelper> _helperMock;

        public SMSServiceTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _smsRepo = new SMSRepository(_fsql);
            _empRepo = new hr_employeeRepository(_fsql);
            _helperMock = new Mock<ISMSHelper>();
        }

        public void Dispose() => _fsql?.Dispose();

        // ============ 辅助 ============

        private SMSService CreateSMSService() =>
            new SMSService(_smsRepo, new Sys_infoService(new Sys_infoRepository(_fsql)), _helperMock.Object);

        private SMSController CreateController(string userId = "TEST_USER", string queryString = "")
        {
            var empMock = new Mock<Ihr_employeeService>();
            empMock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<hr_employee, bool>>>()))
                .Returns((Expression<Func<hr_employee, bool>> e) => _empRepo.GridAsync(e, 1, 100000));

            var ctrl = new SMSController(
                new Mock<ILogger<SMSController>>().Object,
                CreateSMSService(),
                _smsRepo,
                empMock.Object);

            var httpCtx = new DefaultHttpContext();
            httpCtx.Connection.RemoteIpAddress = IPAddress.Loopback;
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, userId),
                new Claim(ClaimTypes.Name, "Test User")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            if (!string.IsNullOrEmpty(queryString))
            {
                httpCtx.Request.QueryString = new QueryString("?" + queryString.TrimStart('?'));
            }
            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return ctrl;
        }

        /// <summary>
        /// 写入短信网关配置（sms_no 明文 + sms_key DES 加密），模拟"已配置"状态。
        /// </summary>
        private async Task ConfigureGatewayAsync(string serialNo = "SER123", string key = "KEYABC")
        {
            await _fsql.Insert(new Sys_info { sys_key = "sms_no", sys_value = serialNo }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_info { sys_key = "sms_key", sys_value = DESEncrypt.Encrypt(key) }).ExecuteAffrowsAsync();
        }

        private static SMS NewSms(string id, string title = "促销短信", string content = "【XHD】周末特惠",
            string mobiles = "13800000000,13900000000", int isSend = 0, string createId = "TEST_USER")
        {
            return new SMS
            {
                id = id,
                sms_title = title,
                sms_content = content,
                contact_ids = "CT1",
                sms_mobiles = mobiles,
                isSend = isSend,
                create_id = createId,
                create_time = new DateTime(2024, 6, 15, 10, 0, 0)
            };
        }

        // ============ 网关未配置：降级契约（不抛异常） ============

        [Fact]
        public async Task GetBalance_NoGatewayConfig_ReturnsZeroWithoutThrowing()
        {
            var ctrl = CreateController();

            var json = await ctrl.GetBalance();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (double)obj["data"]![0]["balance"]!);
        }

        [Fact]
        public async Task QueryStatus_NoGatewayConfig_ReturnsEmptyArrayWithoutThrowing()
        {
            var ctrl = CreateController();

            var json = await ctrl.QueryStatus();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task Send_NoGatewayConfig_ReturnsConfigError()
        {
            var smsId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewSms(smsId)).ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Send(smsId);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("未配置", (string)obj["msg"]!);

            // 未发送：状态不变
            var row = await _fsql.Select<SMS>().Where(a => a.id == smsId).FirstAsync();
            Assert.Equal(0, row.isSend);
        }

        // ============ 网关已配置但不可达：降级为错误码/空值，不抛异常 ============

        [Fact]
        public async Task GetBalance_GatewayThrows_ReturnsZero()
        {
            await ConfigureGatewayAsync();
            _helperMock.Setup(h => h.GetBalance(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new Exception("network unreachable"));

            var ctrl = CreateController();

            var json = await ctrl.GetBalance();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, (double)obj["data"]![0]["balance"]!);
        }

        [Fact]
        public async Task GetBalance_GatewayReachable_ReturnsBalance()
        {
            await ConfigureGatewayAsync("SER123", "KEYABC");
            _helperMock.Setup(h => h.GetBalance("SER123", "KEYABC")).Returns(1234.5);

            var ctrl = CreateController();

            var json = await ctrl.GetBalance();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(1234.5, (double)obj["data"]![0]["balance"]!);
        }

        [Fact]
        public async Task QueryStatus_GatewayThrows_ReturnsEmptyArray()
        {
            await ConfigureGatewayAsync();
            _helperMock.Setup(h => h.QueryStatusAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("gateway down"));

            var ctrl = CreateController();

            var json = await ctrl.QueryStatus();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Empty((JArray)obj["data"]!);
        }

        [Fact]
        public async Task QueryStatus_GatewayReturnsReports_PassesThrough()
        {
            await ConfigureGatewayAsync();
            _helperMock.Setup(h => h.QueryStatusAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new List<SMSStatusReport>
                {
                    new SMSStatusReport("13800000000", "【XHD】周末特惠", 0, ""),
                    new SMSStatusReport("13900000000", "【XHD】周末特惠", 1, "空号")
                });

            var ctrl = CreateController();

            var json = await ctrl.QueryStatus();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(2, data.Count);
            Assert.Equal("13800000000", (string)data[0]["phone"]!);
            Assert.Equal(0, (int)data[0]["smsstatus"]!);
            Assert.Equal("空号", (string)data[1]["err"]!);
        }

        [Fact]
        public async Task Send_GatewayReturnsErrorCode_ReturnsMappedMessageAndKeepsUnsent()
        {
            var smsId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewSms(smsId)).ExecuteAffrowsAsync();
            await ConfigureGatewayAsync();
            _helperMock.Setup(h => h.SendSMS(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<long>())).Returns(-3);

            var ctrl = CreateController();

            var json = await ctrl.Send(smsId);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal(ISMSHelper.SmsResult(-3), (string)obj["msg"]!);
            Assert.Contains("网络错误", (string)obj["msg"]!);

            var row = await _fsql.Select<SMS>().Where(a => a.id == smsId).FirstAsync();
            Assert.Equal(0, row.isSend);
        }

        [Fact]
        public async Task Send_GatewayThrows_ReturnsExceptionMessageWithoutThrowing()
        {
            var smsId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewSms(smsId)).ExecuteAffrowsAsync();
            await ConfigureGatewayAsync();
            _helperMock.Setup(h => h.SendSMS(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<long>()))
                .Throws(new Exception("net error"));

            var ctrl = CreateController();

            var json = await ctrl.Send(smsId);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("短信发送异常", (string)obj["msg"]!);
        }

        // ============ Send 端点分支 ============

        [Fact]
        public async Task Send_ExpiredLogin_ReturnsLoginExpiredError()
        {
            var ctrl = CreateController(userId: "");

            var json = await ctrl.Send(Guid.NewGuid().ToString());
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("登录状态已过期", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Send_NonExistentSms_ReturnsNoDataError()
        {
            await ConfigureGatewayAsync();
            var ctrl = CreateController();

            var json = await ctrl.Send(Guid.NewGuid().ToString());
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无数据", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Send_HappyPath_MarksSentAndUpdatesCheckId()
        {
            var smsId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewSms(smsId)).ExecuteAffrowsAsync();
            await ConfigureGatewayAsync();
            _helperMock.Setup(h => h.SendSMS("SER123", "KEYABC",
                It.IsAny<string[]>(), "【XHD】周末特惠", It.IsAny<long>())).Returns(0);

            var ctrl = CreateController(userId: "EMP_CHECKER");

            var json = await ctrl.Send(smsId);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Contains("发送成功", (string)obj["msg"]!);

            var row = await _fsql.Select<SMS>().Where(a => a.id == smsId).FirstAsync();
            Assert.Equal(1, row.isSend);
            Assert.Equal("EMP_CHECKER", row.check_id);
            Assert.NotNull(row.sendtime);
        }

        // ============ Save CRUD ============

        [Fact]
        public async Task Save_NewSms_InsertsRow()
        {
            var ctrl = CreateController(userId: "EMP1");

            var json = await ctrl.Save(new SMS
            {
                sms_title = "节日短信",
                sms_content = "【XHD】中秋快乐",
                contact_ids = "CT1",
                sms_mobiles = "13800000000, 13900000000"
            });
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var rows = await _fsql.Select<SMS>().ToListAsync();
            Assert.Single(rows);
            Assert.Equal("EMP1", rows[0].create_id);
            Assert.Equal("13800000000,13900000000", rows[0].sms_mobiles);
            Assert.Equal(0, rows[0].isSend);
            Assert.NotNull(rows[0].create_time);
        }

        [Fact]
        public async Task Save_EmptyTitle_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new SMS { sms_content = "c", sms_mobiles = "13800000000" });
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("短信主题不能为空", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Save_EmptyContent_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new SMS { sms_title = "t", sms_mobiles = "13800000000" });
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("短信内容不能为空", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Save_BadMobileFormat_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(new SMS
            {
                sms_title = "t",
                sms_content = "c",
                sms_mobiles = "123"
            });
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("手机号格式不正确", (string)obj["msg"]!);
            Assert.Equal(0, await _fsql.Select<SMS>().CountAsync());
        }

        [Fact]
        public async Task Save_NullModel_ReturnsInvalidParam()
        {
            var ctrl = CreateController();

            var json = await ctrl.Save(null!);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数无效", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Save_ExistingSms_UpdatesContentOnly()
        {
            var smsId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewSms(smsId, title: "旧标题", content: "旧内容")).ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Save(new SMS
            {
                id = smsId,
                sms_title = "新标题",
                sms_content = "新内容",
                contact_ids = "CT2",
                sms_mobiles = "13700000000"
            });
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<SMS>().Where(a => a.id == smsId).FirstAsync();
            Assert.Equal("新标题", row.sms_title);
            Assert.Equal("新内容", row.sms_content);
            Assert.Equal("13700000000", row.sms_mobiles);
            // isSend/sendtime/create_id 不被编辑分支修改
            Assert.Equal(0, row.isSend);
            Assert.Equal("TEST_USER", row.create_id);
        }

        [Fact]
        public async Task Save_ExpiredLogin_ReturnsLoginExpiredError()
        {
            var ctrl = CreateController(userId: "");

            var json = await ctrl.Save(new SMS { sms_title = "t", sms_content = "c", sms_mobiles = "13800000000" });
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("登录状态已过期", (string)obj["msg"]!);
            Assert.Equal(0, await _fsql.Select<SMS>().CountAsync());
        }

        // ============ Delete CRUD ============

        [Fact]
        public async Task Delete_UnsentSms_DeletesRow()
        {
            var smsId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewSms(smsId, isSend: 0)).ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Delete(smsId);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, await _fsql.Select<SMS>().Where(a => a.id == smsId).CountAsync());
        }

        [Fact]
        public async Task Delete_SentSms_ReturnsError()
        {
            var smsId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewSms(smsId, isSend: 1)).ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Delete(smsId);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("此短信已发送，不能删除！", (string)obj["msg"]!);
            Assert.Equal(1, await _fsql.Select<SMS>().Where(a => a.id == smsId).CountAsync());
        }

        [Fact]
        public async Task Delete_NonExistent_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Delete(Guid.NewGuid().ToString());
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("短信不存在", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Delete_EmptyId_ReturnsError()
        {
            var ctrl = CreateController();

            var json = await ctrl.Delete("");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("短信ID无效", (string)obj["msg"]!);
        }

        // ============ Grid 富化 ============

        [Fact]
        public async Task Grid_ReturnsEnrichedCreateAndCheckNames()
        {
            await _fsql.Insert(new hr_employee
            {
                id = "EMP_A",
                name = "张三",
                dep_id = "D1"
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new SMS
            {
                id = "S1",
                sms_title = "一季度短信",
                sms_content = "c",
                sms_mobiles = "13800000000",
                isSend = 1,
                check_id = "EMP_A",
                create_id = "EMP_A",
                create_time = DateTime.Now
            }).ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Grid(new PageView<SMS> { Page = 1, Limit = 30 });
            var data = JObject.Parse(json);

            Assert.Equal(0, (int)data["code"]!);
            Assert.Equal(1, (long)data["count"]!);
            var row = ((JArray)data["data"]!)[0];
            Assert.Equal("张三", (string)row["create_name"]!);
            Assert.Equal("张三", (string)row["check_name"]!);
        }

        [Fact]
        public async Task Grid_WithSerchTxt_FiltersByTitle()
        {
            await _fsql.Insert(NewSms("S1", title: "促销短信")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewSms("S2", title: "节日问候")).ExecuteAffrowsAsync();

            var ctrl = CreateController(queryString: "?serchtxt=促销");

            var json = await ctrl.Grid(new PageView<SMS> { Page = 1, Limit = 30 });
            var data = JObject.Parse(json);

            Assert.Equal(0, (int)data["code"]!);
            Assert.Equal(1, (long)data["count"]!);
            Assert.Equal("S1", (string)((JArray)data["data"]!)[0]["id"]!);
        }

        [Fact]
        public async Task Grid_NoEmployeeMatch_LeavesNamesEmpty()
        {
            await _fsql.Insert(NewSms("S9", createId: "GHOST")).ExecuteAffrowsAsync();

            var ctrl = CreateController();

            var json = await ctrl.Grid(new PageView<SMS> { Page = 1, Limit = 30 });
            var data = JObject.Parse(json);

            Assert.Equal(0, (int)data["code"]!);
            var row = ((JArray)data["data"]!)[0];
            Assert.Equal("", (string)row["create_name"]!);
        }

        // ============ Config / Report 视图 ============

        [Fact]
        public void Config_ReturnsViewResult()
        {
            var ctrl = CreateController();

            var result = ctrl.Config();

            Assert.IsType<ViewResult>(result);
        }

        [Fact]
        public void Report_PassesSmsIdToView()
        {
            var ctrl = CreateController();

            var result = Assert.IsType<ViewResult>(ctrl.Report("SMS-001"));

            Assert.Equal("SMS-001", (string)result.ViewData["smsid"]!);
        }
    }
}
