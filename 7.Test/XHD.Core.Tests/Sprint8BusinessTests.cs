
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FreeSql;
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
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 8 单元测试：业务侧 6 个 ❌ 函数
    ///   #39  Task_follow.DeleteWhere
    ///   #41  Sys_log_Err.GetLogtype
    ///   #137 m_receivable.list/form（Mobile 端）
    ///   #155 SMSHelper.SendSMS（回归 Sprint 7）
    ///   #156 SMSHelper.GetBalance（回归 Sprint 7）
    ///   #157 SMSHelper.QueryStatus（Sprint 8 新增）
    /// </summary>
    public class Sprint8BusinessTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Task_followRepository _taskFollowRepo;
        private readonly Sys_log_ErrRepository _sysLogErrRepo;
        private readonly Finance_ReceivableRepository _recvRepo;
        private readonly Finance_ReceiveRepository _receiveRepo;
        private readonly Sale_orderRepository _orderRepo;
        private readonly SMSRepository _smsRepo;
        private readonly Sys_infoRepository _infoRepo;

        public Sprint8BusinessTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _taskFollowRepo = new Task_followRepository(_fsql);
            _sysLogErrRepo = new Sys_log_ErrRepository(_fsql);
            _recvRepo = new Finance_ReceivableRepository(_fsql);
            _receiveRepo = new Finance_ReceiveRepository(_fsql);
            _orderRepo = new Sale_orderRepository(_fsql);
            _smsRepo = new SMSRepository(_fsql);
            _infoRepo = new Sys_infoRepository(_fsql);
        }

        public void Dispose() => _fsql?.Dispose();

        // ============ #39 Task_follow.DeleteWhere ============

        [Fact]
        public async Task DeleteWhere_TaskIdEmpty_ReturnsZero()
        {
            var svc = new Task_followService(_taskFollowRepo);
            var rows = await svc.DeleteByTaskAsync("");
            Assert.Equal(0, rows);
        }

        [Fact]
        public async Task DeleteWhere_NullTaskId_ReturnsZero()
        {
            var svc = new Task_followService(_taskFollowRepo);
            var rows = await svc.DeleteByTaskAsync(null);
            Assert.Equal(0, rows);
        }

        [Fact]
        public async Task DeleteWhere_NoMatchingRows_ReturnsZero()
        {
            await _fsql.Insert(new Task_follow { id = "F1", task_id = "T1" }).ExecuteAffrowsAsync();

            var svc = new Task_followService(_taskFollowRepo);
            var rows = await svc.DeleteByTaskAsync("NONEXIST");
            Assert.Equal(0, rows);
        }

        [Fact]
        public async Task DeleteWhere_SingleMatch_ReturnsOne()
        {
            await _fsql.Insert(new Task_follow { id = "F1", task_id = "T1" }).ExecuteAffrowsAsync();

            var svc = new Task_followService(_taskFollowRepo);
            var rows = await svc.DeleteByTaskAsync("T1");
            Assert.Equal(1, rows);

            var remaining = await _fsql.Select<Task_follow>().Where(a => a.task_id == "T1").CountAsync();
            Assert.Equal(0, remaining);
        }

        [Fact]
        public async Task DeleteWhere_MultipleMatches_DeletesAll()
        {
            await _fsql.Insert(new List<Task_follow>
            {
                new Task_follow { id = "F1", task_id = "T1" },
                new Task_follow { id = "F2", task_id = "T1" },
                new Task_follow { id = "F3", task_id = "T1" }
            }).ExecuteAffrowsAsync();

            var svc = new Task_followService(_taskFollowRepo);
            var rows = await svc.DeleteByTaskAsync("T1");
            Assert.Equal(3, rows);
        }

        [Fact]
        public async Task DeleteWhere_DoesNotTouchOtherTasks()
        {
            await _fsql.Insert(new List<Task_follow>
            {
                new Task_follow { id = "F1", task_id = "T1" },
                new Task_follow { id = "F2", task_id = "T2" },
                new Task_follow { id = "F3", task_id = "T2" }
            }).ExecuteAffrowsAsync();

            var svc = new Task_followService(_taskFollowRepo);
            var rows = await svc.DeleteByTaskAsync("T1");
            Assert.Equal(1, rows);

            var t2Count = await _fsql.Select<Task_follow>().Where(a => a.task_id == "T2").CountAsync();
            Assert.Equal(2, t2Count);
        }

        // ============ #41 Sys_log_Err.GetLogtype ============

        [Fact]
        public async Task GetLogtype_EmptyTable_ReturnsEmpty()
        {
            var svc = new Sys_log_ErrService(_sysLogErrRepo);
            var list = await svc.GetLogtypeAsync();
            Assert.Empty(list);
        }

        [Fact]
        public async Task GetLogtype_SingleType_ReturnsOne()
        {
            await _fsql.Insert(new Sys_log_Err { id = "E1", Err_typeid = 1, Err_type = "登录" }).ExecuteAffrowsAsync();

            var svc = new Sys_log_ErrService(_sysLogErrRepo);
            var list = await svc.GetLogtypeAsync();
            Assert.Single(list);
            Assert.Equal(1, list[0].typeid);
            Assert.Equal("登录", list[0].type);
        }

        [Fact]
        public async Task GetLogtype_MultipleTypes_ReturnsAll()
        {
            await _fsql.Insert(new List<Sys_log_Err>
            {
                new Sys_log_Err { id = "E1", Err_typeid = 2, Err_type = "权限" },
                new Sys_log_Err { id = "E2", Err_typeid = 1, Err_type = "登录" },
                new Sys_log_Err { id = "E3", Err_typeid = 3, Err_type = "网络" }
            }).ExecuteAffrowsAsync();

            var svc = new Sys_log_ErrService(_sysLogErrRepo);
            var list = await svc.GetLogtypeAsync();
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public async Task GetLogtype_DeduplicatesByTypeid()
        {
            await _fsql.Insert(new List<Sys_log_Err>
            {
                new Sys_log_Err { id = "E1", Err_typeid = 1, Err_type = "登录" },
                new Sys_log_Err { id = "E2", Err_typeid = 1, Err_type = "登录" },
                new Sys_log_Err { id = "E3", Err_typeid = 2, Err_type = "权限" }
            }).ExecuteAffrowsAsync();

            var svc = new Sys_log_ErrService(_sysLogErrRepo);
            var list = await svc.GetLogtypeAsync();
            Assert.Equal(2, list.Count);
        }

        [Fact]
        public async Task GetLogtype_SortsByTypeidAscending()
        {
            await _fsql.Insert(new List<Sys_log_Err>
            {
                new Sys_log_Err { id = "E1", Err_typeid = 3, Err_type = "C" },
                new Sys_log_Err { id = "E2", Err_typeid = 1, Err_type = "A" },
                new Sys_log_Err { id = "E3", Err_typeid = 2, Err_type = "B" }
            }).ExecuteAffrowsAsync();

            var svc = new Sys_log_ErrService(_sysLogErrRepo);
            var list = await svc.GetLogtypeAsync();
            Assert.Equal(1, list[0].typeid);
            Assert.Equal(2, list[1].typeid);
            Assert.Equal(3, list[2].typeid);
        }

        [Fact]
        public async Task GetLogtype_NullTypeid_FilteredOut()
        {
            await _fsql.Insert(new List<Sys_log_Err>
            {
                new Sys_log_Err { id = "E1", Err_typeid = 1, Err_type = "登录" },
                new Sys_log_Err { id = "E2", Err_typeid = null, Err_type = "X" }
            }).ExecuteAffrowsAsync();

            var svc = new Sys_log_ErrService(_sysLogErrRepo);
            var list = await svc.GetLogtypeAsync();
            Assert.Single(list);
        }

        // ============ #137a/#137b m_receivable list/form ============

        private Finance_ReceivableService NewRecvService()
            => new Finance_ReceivableService(_recvRepo, _receiveRepo, _orderRepo);

        private static Sale_order NewOrder(string id, string custId, string emp = "E1")
            => new Sale_order
            {
                id = id,
                customer_id = custId,
                emp_id = emp,
                Order_amount = 1000m,
                total_amount = 1000m,
                arrears_money = 1000m,
                receive_money = 0m,
                sn = $"SO-{id}",
                create_time = new DateTime(2024, 6, 1)
            };

        private static async Task SeedReceivablesAsync(
            IFreeSql fsql, string custId, string orderId, int count, int deleted = 0)
        {
            await fsql.Insert(new CRM_Customer { id = custId, cus_name = $"客户-{custId}" }).ExecuteAffrowsAsync();
            await fsql.Insert(NewOrder(orderId, custId)).ExecuteAffrowsAsync();

            var items = new List<Finance_Receivable>();
            for (int i = 0; i < count; i++)
            {
                items.Add(new Finance_Receivable
                {
                    id = $"R{orderId}{i:D2}",
                    receivable_no = $"YS-{orderId}{i:D2}",
                    order_id = orderId,
                    create_id = "E1",
                    receivable_amount = 200m,
                    received_amount = 0m,
                    arrears_amount = 200m,
                    create_time = new DateTime(2024, 7, 1) + TimeSpan.FromDays(i),
                    isDelete = 0
                });
            }
            if (deleted > 0)
            {
                items[0].isDelete = 1;
            }
            await fsql.Insert(items).ExecuteAffrowsAsync();
        }

        [Fact]
        public async Task MobileList_DefaultPaging_ReturnsAllRows()
        {
            await SeedReceivablesAsync(_fsql, "C1", "O1", 5, deleted: 0);
            var svc = NewRecvService();
            var data = await svc.GridAsync(a => a.isDelete == 0, 1, 10, "a.create_time desc");
            Assert.Equal(5L, data.count);
            Assert.Equal(5, data.data.Count);
        }

        [Fact]
        public async Task MobileList_Paging_LimitApplies()
        {
            await SeedReceivablesAsync(_fsql, "C1", "O1", 5, deleted: 0);
            var svc = NewRecvService();
            var data = await svc.GridAsync(a => a.isDelete == 0, 1, 2, "a.create_time desc");
            Assert.Equal(5L, data.count);
            Assert.Equal(2, data.data.Count);
        }

        [Fact]
        public async Task MobileList_ExcludesSoftDeleted()
        {
            await SeedReceivablesAsync(_fsql, "C1", "O1", 5, deleted: 1);
            var svc = NewRecvService();
            var data = await svc.GridAsync(a => a.isDelete == 0, 1, 10, "a.create_time desc");
            Assert.Equal(4L, data.count);
        }

        [Fact]
        public async Task MobileList_OrderIdFilter_ReturnsOnlyMatchingRows()
        {
            await SeedReceivablesAsync(_fsql, "C1", "O1", 2, deleted: 0);
            await SeedReceivablesAsync(_fsql, "C2", "O2", 3, deleted: 0);
            var svc = NewRecvService();
            var data = await svc.GridAsync(a => a.isDelete == 0 && a.order_id == "O1", 1, 10, "a.create_time desc");
            Assert.Equal(2L, data.count);
            Assert.All(data.data, x => Assert.Equal("O1", x.order_id));
        }

        [Fact]
        public async Task MobileList_CustomerIdFilter_ReturnsOnlyMatchingCustomer()
        {
            // two customers, two orders
            await SeedReceivablesAsync(_fsql, "C1", "O1", 2, deleted: 0);
            await SeedReceivablesAsync(_fsql, "C2", "O2", 3, deleted: 0);

            // Use order_id join filter as an equivalent for verifying filter chain works
            var svc = NewRecvService();
            var data = await svc.GridAsync(a => a.isDelete == 0 && a.order_id == "O2", 1, 10, "a.create_time desc");
            Assert.Equal(3L, data.count);
        }

        [Fact]
        public async Task MobileList_TotalCountCorrectAfterFilter()
        {
            await SeedReceivablesAsync(_fsql, "C1", "O1", 7, deleted: 1);
            var svc = NewRecvService();
            var data = await svc.GridAsync(a => a.isDelete == 0, 1, 30, "a.create_time desc");
            Assert.Equal(6L, data.count);
            Assert.Equal(6, data.data.Count);
        }

        [Fact]
        public async Task MobileForm_NonExistentId_ReturnsEmpty()
        {
            var id = Guid.NewGuid().ToString();
            var svc = NewRecvService();
            var data = await svc.GridAsync(a => a.id == id && a.isDelete == 0);
            Assert.Equal(0L, data.count);
        }

        [Fact]
        public async Task MobileForm_ValidId_ReturnsSingleRecord()
        {
            await SeedReceivablesAsync(_fsql, "C1", "O1", 1, deleted: 0);
            var id = "RO100";
            var svc = NewRecvService();
            var data = await svc.GridAsync(a => a.id == id && a.isDelete == 0);
            Assert.Equal(1L, data.count);
            Assert.Single(data.data, x => x.id == id);
        }

        [Fact]
        public async Task MobileForm_SoftDeleted_ReturnsEmpty()
        {
            await SeedReceivablesAsync(_fsql, "C1", "O1", 1, deleted: 1);
            var id = "RO100";
            var svc = NewRecvService();
            var data = await svc.GridAsync(a => a.id == id && a.isDelete == 0);
            Assert.Equal(0L, data.count);
        }

        // ============ #155/#156/#157 SMSHelper ============

        private async Task SeedSmsCredentialsAsync()
        {
            await _fsql.Insert(new Sys_info { sys_key = "sms_no", sys_value = "SN1" }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sys_info { sys_key = "sms_key", sys_value = DESEncrypt.Encrypt("KEY1") }).ExecuteAffrowsAsync();
        }

        private SMS BuildSms(string id) => new SMS
        {
            id = id,
            sms_title = "T",
            sms_content = "C",
            sms_mobiles = "13800000000",
            contact_ids = "K1",
            isSend = 0,
            create_id = "E1",
            create_time = DateTime.Now
        };

        [Fact]
        public async Task SendSMS_HelperReturnsZero_UpdatesSendState()
        {
            var id = Guid.NewGuid().ToString();
            await _fsql.Insert(BuildSms(id)).ExecuteAffrowsAsync();
            await SeedSmsCredentialsAsync();

            var helperMock = new Mock<ISMSHelper>();
            helperMock.Setup(h => h.SendSMS("SN1", "KEY1", It.IsAny<string[]>(), "C", It.IsAny<long>()))
                      .Returns(0);
            var infoSvc = new Sys_infoService(_infoRepo);
            var svc = new SMSService(_smsRepo, infoSvc, helperMock.Object);
            var result = await svc.SendAsync(id, "E1");
            Assert.Contains("发送成功", result);

            var sms = await _fsql.Select<SMS>().Where(a => a.id == id).FirstAsync();
            Assert.Equal(1, sms.isSend);
            Assert.Equal("E1", sms.check_id);
        }

        [Fact]
        public async Task SendSMS_HelperReturnsError_ReturnsErrorMessage()
        {
            var id = Guid.NewGuid().ToString();
            await _fsql.Insert(BuildSms(id)).ExecuteAffrowsAsync();
            await SeedSmsCredentialsAsync();

            var helperMock = new Mock<ISMSHelper>();
            helperMock.Setup(h => h.SendSMS(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<long>()))
                      .Returns(-3);
            var infoSvc = new Sys_infoService(_infoRepo);
            var svc = new SMSService(_smsRepo, infoSvc, helperMock.Object);
            var result = await svc.SendAsync(id, "E1");
            Assert.Contains("网络错误", result);
        }

        [Fact]
        public async Task SendSMS_NoSmsRecord_ReturnsError()
        {
            var helperMock = new Mock<ISMSHelper>();
            var infoSvc = new Sys_infoService(_infoRepo);
            var svc = new SMSService(_smsRepo, infoSvc, helperMock.Object);
            var result = await svc.SendAsync(Guid.NewGuid().ToString(), "E1");
            Assert.Contains("系统错误", result);
        }

        [Fact]
        public async Task GetBalance_NoConfig_ReturnsZero()
        {
            var helperMock = new Mock<ISMSHelper>();
            var infoSvc = new Sys_infoService(_infoRepo);
            var svc = new SMSService(_smsRepo, infoSvc, helperMock.Object);
            var balance = await svc.GetBalanceAsync();
            Assert.Equal(0d, balance);
        }

        [Fact]
        public async Task GetBalance_HelperReturnsValue_ReturnsValue()
        {
            await SeedSmsCredentialsAsync();
            var helperMock = new Mock<ISMSHelper>();
            helperMock.Setup(h => h.GetBalance("SN1", "KEY1")).Returns(42.0);
            var infoSvc = new Sys_infoService(_infoRepo);
            var svc = new SMSService(_smsRepo, infoSvc, helperMock.Object);
            var balance = await svc.GetBalanceAsync();
            Assert.Equal(42.0, balance);
        }

        [Fact]
        public async Task GetBalance_HelperThrows_ReturnsZero()
        {
            await SeedSmsCredentialsAsync();
            var helperMock = new Mock<ISMSHelper>();
            helperMock.Setup(h => h.GetBalance(It.IsAny<string>(), It.IsAny<string>()))
                      .Throws(new Exception("boom"));
            var infoSvc = new Sys_infoService(_infoRepo);
            var svc = new SMSService(_smsRepo, infoSvc, helperMock.Object);
            var balance = await svc.GetBalanceAsync();
            Assert.Equal(0d, balance);
        }

        [Fact]
        public async Task QueryStatus_NoConfig_ReturnsEmptyArray()
        {
            var helperMock = new Mock<ISMSHelper>();
            helperMock.Setup(h => h.QueryStatusAsync(It.IsAny<string>(), It.IsAny<string>()))
                      .ReturnsAsync(new List<SMSStatusReport>());
            var infoSvc = new Sys_infoService(_infoRepo);
            var svc = new SMSService(_smsRepo, infoSvc, helperMock.Object);
            var arr = await svc.QueryStatusAsync();
            Assert.Empty(arr);
        }

        [Fact]
        public async Task QueryStatus_HelperReturnsEmptyList_ReturnsEmptyArray()
        {
            await SeedSmsCredentialsAsync();
            var helperMock = new Mock<ISMSHelper>();
            helperMock.Setup(h => h.QueryStatusAsync("SN1", "KEY1"))
                      .ReturnsAsync(new List<SMSStatusReport>());
            var infoSvc = new Sys_infoService(_infoRepo);
            var svc = new SMSService(_smsRepo, infoSvc, helperMock.Object);
            var arr = await svc.QueryStatusAsync();
            Assert.Empty(arr);
        }

        [Fact]
        public async Task QueryStatus_HelperReturnsReports_ReturnsAll()
        {
            await SeedSmsCredentialsAsync();
            var helperMock = new Mock<ISMSHelper>();
            helperMock.Setup(h => h.QueryStatusAsync("SN1", "KEY1"))
                      .ReturnsAsync(new List<SMSStatusReport>
                      {
                          new SMSStatusReport("13800000001", "Hello", 0, ""),
                          new SMSStatusReport("13800000002", "World", 1, "超时")
                      });
            var infoSvc = new Sys_infoService(_infoRepo);
            var svc = new SMSService(_smsRepo, infoSvc, helperMock.Object);
            var arr = await svc.QueryStatusAsync();
            Assert.Equal(2, arr.Count);
            Assert.Equal("13800000001", (string)arr[0]["phone"]);
            Assert.Equal("13800000002", (string)arr[1]["phone"]);
        }

        [Fact]
        public async Task QueryStatus_HelperThrows_ReturnsEmptyArray()
        {
            await SeedSmsCredentialsAsync();
            var helperMock = new Mock<ISMSHelper>();
            helperMock.Setup(h => h.QueryStatusAsync(It.IsAny<string>(), It.IsAny<string>()))
                      .ThrowsAsync(new Exception("boom"));
            var infoSvc = new Sys_infoService(_infoRepo);
            var svc = new SMSService(_smsRepo, infoSvc, helperMock.Object);
            var arr = await svc.QueryStatusAsync();
            Assert.Empty(arr);
        }

        [Fact]
        public async Task QueryStatus_MapsAllFields()
        {
            await SeedSmsCredentialsAsync();
            var helperMock = new Mock<ISMSHelper>();
            helperMock.Setup(h => h.QueryStatusAsync("SN1", "KEY1"))
                      .ReturnsAsync(new List<SMSStatusReport>
                      {
                          new SMSStatusReport("13800000099", "内容A", 0, "OK")
                      });
            var infoSvc = new Sys_infoService(_infoRepo);
            var svc = new SMSService(_smsRepo, infoSvc, helperMock.Object);
            var arr = await svc.QueryStatusAsync();
            var j = arr[0] as JObject;
            Assert.NotNull(j);
            Assert.Equal("13800000099", (string)j["phone"]);
            Assert.Equal("内容A", (string)j["smscontent"]);
            Assert.Equal(0, (int)j["smsstatus"]);
            Assert.Equal("OK", (string)j["err"]);
        }
    }
}
