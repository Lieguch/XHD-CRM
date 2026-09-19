using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
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
    /// Sprint 3 Wave 1 跟进报表单元测试。
    /// 覆盖 #01-#05 仓库层 + #06-#09 端点层。
    /// </summary>
    public class FollowReportTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly CRM_followRepository _followRepo;

        public FollowReportTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _followRepo = new CRM_followRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static CRM_follow NewFollow(
            string id,
            DateTime followTime,
            string employeeId,
            string followTypeId = "",
            string followAimId = "",
            string customerId = "",
            int isDelete = 0)
        {
            return new CRM_follow
            {
                id = id,
                follow_time = followTime,
                employee_id = employeeId,
                follow_type_id = followTypeId,
                follow_aim_id = followAimId,
                customer_id = customerId,
                contact_id = "",
                follow_content = "跟进内容",
                isDelete = isDelete
            };
        }

        private static Sys_Param NewFollowTypeParam(string id, string name)
        {
            return new Sys_Param
            {
                id = id,
                params_name = name,
                params_type = "follow_type",
                params_order = 1,
                create_id = "SYSTEM",
                create_time = new DateTime(2023, 1, 1),
                isDelete = 0
            };
        }

        private static Sys_Param NewFollowAimParam(string id, string name)
        {
            return new Sys_Param
            {
                id = id,
                params_name = name,
                params_type = "follow_aim",
                params_order = 1,
                create_id = "SYSTEM",
                create_time = new DateTime(2023, 1, 1),
                isDelete = 0
            };
        }

        private static CRM_Customer NewCustomer(string id, string empId = "E1")
        {
            return new CRM_Customer
            {
                id = id,
                cus_name = $"客户-{id}",
                emp_id = empId,
                create_id = empId,
                create_time = new DateTime(2024, 1, 1),
                state = 0,
                isDelete = 0,
                isPrivate = 1,
                sn = $"CU-{id}"
            };
        }

        private static hr_employee NewEmployee(string id, string name)
        {
            return new hr_employee
            {
                id = id,
                name = name,
                create_id = "SYSTEM",
                create_time = new DateTime(2023, 1, 1),
                isDelete = 0
            };
        }

        private async Task InsertFollowAsync(CRM_follow f)
            => await _fsql.Insert(f).ExecuteAffrowsAsync();

        private async Task InsertParamAsync(Sys_Param p)
            => await _fsql.Insert(p).ExecuteAffrowsAsync();

        private async Task InsertCustomerAsync(CRM_Customer c)
            => await _fsql.Insert(c).ExecuteAffrowsAsync();

        private async Task InsertEmployeeAsync(hr_employee e)
            => await _fsql.Insert(e).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        private static CRMFollowController CreateFollowController(
            CRM_followRepository repo,
            string queryString = "",
            string userId = "TEST_USER")
        {
            var svcMock = new Mock<ICRM_followService>();
            svcMock.Setup(s => s.ComparedFollowAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((int y1, int m1, int y2, int m2) => repo.ComparedFollowAsync(y1, m1, y2, m2));
            svcMock.Setup(s => s.ComparedEmpCusFollowAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns((int y1, int m1, int y2, int m2, List<string> eids) => repo.ComparedEmpCusFollowAsync(y1, m1, y2, m2, eids));
            svcMock.Setup(s => s.ReportMonthEmpFollowAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<List<string>>()))
                .Returns((DateTime s, DateTime e, List<string> eids) => repo.ReportMonthEmpFollowAsync(s, e, eids));
            svcMock.Setup(s => s.ReportEmpFollowAsync(It.IsAny<int>(), It.IsAny<List<string>>()))
                .Returns((int y, List<string> eids) => repo.ReportEmpFollowAsync(y, eids));

            var logger = new Mock<ILogger<CRMFollowController>>().Object;
            var logSvc = new Mock<ISys_logService>().Object;
            var authMock = new Mock<IDBAuthService>();
            authMock.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 4, empList = new List<string>() });
            var custSvc = new Mock<ICRM_CustomerService>().Object;

            var ctrl = new CRMFollowController(logger, svcMock.Object, logSvc, authMock.Object, custSvc);

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

        // =========================================================
        // #01 ReportsYearAsync（跟进年度趋势，按类型分组 + 12 月矩阵）
        // =========================================================

        [Fact]
        public async Task ReportsYearAsync_FollowType_ReturnsYearMatrix()
        {
            // Arrange：3 条 2024 跟进，类型分布：电话(2) / 邮件(1)
            await InsertParamAsync(NewFollowTypeParam("FT1", "电话"));
            await InsertParamAsync(NewFollowTypeParam("FT2", "邮件"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 10), "E1", followTypeId: "FT1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 3, 20), "E1", followTypeId: "FT1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F3", new DateTime(2024, 6, 15), "E1", followTypeId: "FT2", customerId: "C1"));

            // Act
            var arr = await _followRepo.ReportsYearAsync("Follow_Type", 2024, null);

            // Assert
            Assert.Equal(2, arr.Count);
            var phone = arr.FirstOrDefault(o => (string)o["params_name"] == "电话");
            var email = arr.FirstOrDefault(o => (string)o["params_name"] == "邮件");
            Assert.NotNull(phone);
            Assert.NotNull(email);
            Assert.Equal(2, (int)phone["m3"]!);
            Assert.Equal(0, (int)phone["m6"]!);
            Assert.Equal(1, (int)email["m6"]!);
            Assert.Equal(0, (int)email["m3"]!);
        }

        [Fact]
        public async Task ReportsYearAsync_FollowAim_ReturnsYearMatrix()
        {
            // Arrange：2 条跟进，目标：意向(1) / 报价(1)
            await InsertParamAsync(NewFollowAimParam("FA1", "意向"));
            await InsertParamAsync(NewFollowAimParam("FA2", "报价"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 5, 10), "E1", followAimId: "FA1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 8, 15), "E1", followAimId: "FA2", customerId: "C1"));

            // Act
            var arr = await _followRepo.ReportsYearAsync("Follow_aim", 2024, null);

            // Assert
            Assert.Equal(2, arr.Count);
            var intent = arr.FirstOrDefault(o => (string)o["params_name"] == "意向");
            var quote = arr.FirstOrDefault(o => (string)o["params_name"] == "报价");
            Assert.NotNull(intent);
            Assert.NotNull(quote);
            Assert.Equal(1, (int)intent["m5"]!);
            Assert.Equal(0, (int)intent["m8"]!);
            Assert.Equal(1, (int)quote["m8"]!);
        }

        [Fact]
        public async Task ReportsYearAsync_EmptyDB_ReturnsEmptyArray()
        {
            // Act
            var arr = await _followRepo.ReportsYearAsync("Follow_Type", 2024, null);

            // Assert
            Assert.Empty(arr);
        }

        // =========================================================
        // #02 ComparedFollowAsync（跟进双月对比）
        // =========================================================

        [Fact]
        public async Task ComparedFollowAsync_TwoDifferentMonths_ReturnsBothCounts()
        {
            // Arrange：电话 3 月 2 条 + 4 月 3 条；邮件 3 月 1 条 + 4 月 0 条
            await InsertParamAsync(NewFollowTypeParam("FT1", "电话"));
            await InsertParamAsync(NewFollowTypeParam("FT2", "邮件"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", followTypeId: "FT1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 3, 15), "E1", followTypeId: "FT1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F3", new DateTime(2024, 4, 5), "E1", followTypeId: "FT1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F4", new DateTime(2024, 4, 10), "E1", followTypeId: "FT1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F5", new DateTime(2024, 4, 20), "E1", followTypeId: "FT1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F6", new DateTime(2024, 3, 20), "E1", followTypeId: "FT2", customerId: "C1"));

            // Act
            var arr = await _followRepo.ComparedFollowAsync(2024, 3, 2024, 4);

            // Assert
            Assert.Equal(2, arr.Count);
            var phone = arr.FirstOrDefault(o => (string)o["yy"] == "电话");
            var email = arr.FirstOrDefault(o => (string)o["yy"] == "邮件");
            Assert.NotNull(phone);
            Assert.NotNull(email);
            Assert.Equal(2, (int)phone["dt1"]!);
            Assert.Equal(3, (int)phone["dt2"]!);
            Assert.Equal(1, (int)email["dt1"]!);
            Assert.Equal(0, (int)email["dt2"]!);
        }

        [Fact]
        public async Task ComparedFollowAsync_SameMonth_ReturnsNormalResult()
        {
            await InsertParamAsync(NewFollowTypeParam("FT1", "电话"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", followTypeId: "FT1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 3, 15), "E1", followTypeId: "FT1", customerId: "C1"));

            var arr = await _followRepo.ComparedFollowAsync(2024, 3, 2024, 3);

            Assert.Single(arr);
            Assert.Equal("电话", (string)arr[0]["yy"]!);
            Assert.Equal(2, (int)arr[0]["dt1"]!);
            Assert.Equal(2, (int)arr[0]["dt2"]!);
        }

        [Fact]
        public async Task ComparedFollowAsync_EmptyDB_ReturnsEmptyArray()
        {
            var arr = await _followRepo.ComparedFollowAsync(2024, 3, 2024, 4);

            Assert.Empty(arr);
        }

        // =========================================================
        // #03 ComparedEmpCusFollowAsync（员工维度双月对比）
        // =========================================================

        [Fact]
        public async Task ComparedEmpCusFollowAsync_WithEmpIds_OnlyFilteredEmployees()
        {
            // Arrange：E1 3 月 2 条 + 4 月 1 条；E2 3 月 1 条 + 4 月 2 条
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1", "E1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 3, 15), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F3", new DateTime(2024, 4, 5), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F4", new DateTime(2024, 3, 10), "E2", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F5", new DateTime(2024, 4, 10), "E2", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F6", new DateTime(2024, 4, 20), "E2", customerId: "C1"));

            // Act：只统计 E2
            var arr = await _followRepo.ComparedEmpCusFollowAsync(2024, 3, 2024, 4, new List<string> { "E2" });

            // Assert
            Assert.Single(arr);
            Assert.Equal("李四", (string)arr[0]["yy"]!);
            Assert.Equal(1, (int)arr[0]["dt1"]!);
            Assert.Equal(2, (int)arr[0]["dt2"]!);
        }

        [Fact]
        public async Task ComparedEmpCusFollowAsync_EmptyEmpIds_ReturnsAllEmployees()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 4, 5), "E2", customerId: "C1"));

            // Act：empIds=null 表示全部
            var arr = await _followRepo.ComparedEmpCusFollowAsync(2024, 3, 2024, 4, null);

            // Assert
            Assert.Equal(2, arr.Count);
            var e1 = arr.FirstOrDefault(o => (string)o["yy"] == "张三");
            var e2 = arr.FirstOrDefault(o => (string)o["yy"] == "李四");
            Assert.NotNull(e1);
            Assert.NotNull(e2);
            Assert.Equal(1, (int)e1["dt1"]!);
            Assert.Equal(0, (int)e1["dt2"]!);
            Assert.Equal(0, (int)e2["dt1"]!);
            Assert.Equal(1, (int)e2["dt2"]!);
        }

        [Fact]
        public async Task ComparedEmpCusFollowAsync_EmptyDB_ReturnsEmptyArray()
        {
            var arr = await _followRepo.ComparedEmpCusFollowAsync(2024, 3, 2024, 4, null);

            Assert.Empty(arr);
        }

        // =========================================================
        // #04 ReportMonthEmpFollowAsync（员工月度跟进矩阵）
        // =========================================================

        [Fact]
        public async Task ReportMonthEmpFollowAsync_MultipleEmployees_ReturnsMatrix()
        {
            // Arrange：E1 3 月 2 条 + 4 月 1 条；E2 3 月 1 条
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 3, 15), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F3", new DateTime(2024, 4, 10), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F4", new DateTime(2024, 3, 20), "E2", customerId: "C1"));

            // Act
            var arr = await _followRepo.ReportMonthEmpFollowAsync(
                new DateTime(2024, 1, 1),
                new DateTime(2024, 12, 31, 23, 59, 59),
                null);

            // Assert
            Assert.Equal(2, arr.Count);
            var e1 = arr.FirstOrDefault(o => (string)o["name"] == "张三");
            var e2 = arr.FirstOrDefault(o => (string)o["name"] == "李四");
            Assert.NotNull(e1);
            Assert.NotNull(e2);
            Assert.Equal(2024, (int)e1["yy"]!);
            Assert.Equal(2, (int)e1["m3"]!);
            Assert.Equal(1, (int)e1["m4"]!);
            Assert.Equal(1, (int)e2["m3"]!);
            Assert.Equal(0, (int)e2["m4"]!);
        }

        [Fact]
        public async Task ReportMonthEmpFollowAsync_SingleEmployee_ReturnsSingleRow()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", customerId: "C1"));

            var arr = await _followRepo.ReportMonthEmpFollowAsync(
                new DateTime(2024, 1, 1),
                new DateTime(2024, 12, 31, 23, 59, 59),
                null);

            Assert.Single(arr);
            Assert.Equal("张三", (string)arr[0]["name"]!);
            Assert.Equal(1, (int)arr[0]["m3"]!);
            // 其他月份应为 0
            for (int m = 1; m <= 12; m++)
            {
                if (m != 3)
                    Assert.Equal(0, (int)arr[0][$"m{m}"]!);
            }
        }

        [Fact]
        public async Task ReportMonthEmpFollowAsync_EmptyDB_ReturnsEmptyArray()
        {
            var arr = await _followRepo.ReportMonthEmpFollowAsync(
                new DateTime(2024, 1, 1),
                new DateTime(2024, 12, 31, 23, 59, 59),
                null);

            Assert.Empty(arr);
        }

        // =========================================================
        // #05 ReportEmpFollowAsync（员工年度跟进矩阵）
        // =========================================================

        [Fact]
        public async Task ReportEmpFollowAsync_YearlyMatrix_CorrectCounts()
        {
            // Arrange：E1 全年 3 月 2 条 + 6 月 3 条
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 3, 15), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F3", new DateTime(2024, 6, 10), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F4", new DateTime(2024, 6, 20), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F5", new DateTime(2024, 6, 25), "E1", customerId: "C1"));

            // Act
            var arr = await _followRepo.ReportEmpFollowAsync(2024, null);

            // Assert
            Assert.Single(arr);
            Assert.Equal("张三", (string)arr[0]["name"]!);
            Assert.Equal(2024, (int)arr[0]["yy"]!);
            Assert.Equal(2, (int)arr[0]["m3"]!);
            Assert.Equal(3, (int)arr[0]["m6"]!);
            Assert.Equal(0, (int)arr[0]["m1"]!);
            Assert.Equal(0, (int)arr[0]["m12"]!);
        }

        [Fact]
        public async Task ReportEmpFollowAsync_WithEmpIdsFilter_OnlyFiltered()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 3, 10), "E2", customerId: "C1"));

            var arr = await _followRepo.ReportEmpFollowAsync(2024, new List<string> { "E2" });

            Assert.Single(arr);
            Assert.Equal("李四", (string)arr[0]["name"]!);
            Assert.Equal(1, (int)arr[0]["m3"]!);
        }

        [Fact]
        public async Task ReportEmpFollowAsync_EmptyDB_ReturnsEmptyArray()
        {
            var arr = await _followRepo.ReportEmpFollowAsync(2024, null);

            Assert.Empty(arr);
        }

        // =========================================================
        // #06 ComparedFollow 端点（CRMFollowController）
        // =========================================================

        [Fact]
        public async Task Controller_ComparedFollow_ValidParams_ReturnsSuccess()
        {
            await InsertParamAsync(NewFollowTypeParam("FT1", "电话"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", followTypeId: "FT1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 4, 5), "E1", followTypeId: "FT1", customerId: "C1"));

            var ctrl = CreateFollowController(_followRepo);

            var json = await ctrl.ComparedFollow(2024, 3, 2024, 4);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("电话", (string)data[0]["yy"]!);
            Assert.Equal(1, (int)data[0]["dt1"]!);
            Assert.Equal(1, (int)data[0]["dt2"]!);
        }

        [Fact]
        public async Task Controller_ComparedFollow_InvalidMonth_ReturnsError()
        {
            var ctrl = CreateFollowController(_followRepo);

            var json = await ctrl.ComparedFollow(2024, 13, 2024, 4);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("月份", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Controller_ComparedFollow_EmptyData_ReturnsEmptyArray()
        {
            var ctrl = CreateFollowController(_followRepo);

            var json = await ctrl.ComparedFollow(2024, 3, 2024, 4);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Empty(data);
        }

        // =========================================================
        // #07 ComparedEmpCusFollow 端点
        // =========================================================

        [Fact]
        public async Task Controller_ComparedEmpCusFollow_ValidParams_ReturnsSuccess()
        {
            // 使用 GUID 格式员工 ID 以通过 ParseEmpIds 的 Guid.TryParse 校验
            string guid1 = "11111111-1111-1111-1111-111111111111";
            await InsertEmployeeAsync(NewEmployee(guid1, "张三"));
            await InsertCustomerAsync(NewCustomer("C1", guid1));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), guid1, customerId: "C1"));

            var ctrl = CreateFollowController(_followRepo, $"idlist={guid1}");

            var json = await ctrl.ComparedEmpCusFollow(guid1, 2024, 3, 2024, 4);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("张三", (string)data[0]["yy"]!);
            Assert.Equal(1, (int)data[0]["dt1"]!);
        }

        [Fact]
        public async Task Controller_ComparedEmpCusFollow_EmptyIdlist_ReturnsAllEmployees()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 4, 5), "E2", customerId: "C1"));

            var ctrl = CreateFollowController(_followRepo);

            // idlist=null → ParseEmpIds 返回 null → 不过滤
            var json = await ctrl.ComparedEmpCusFollow(null!, 2024, 3, 2024, 4);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(2, data.Count);
        }

        [Fact]
        public async Task Controller_ComparedEmpCusFollow_InvalidMonth_ReturnsError()
        {
            var ctrl = CreateFollowController(_followRepo);

            var json = await ctrl.ComparedEmpCusFollow(null!, 2024, 0, 2024, 4);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("月份", (string)obj["msg"]!);
        }

        // =========================================================
        // #08 EmpMonthCusFollow 端点
        // =========================================================

        [Fact]
        public async Task Controller_EmpMonthCusFollow_ValidParams_ReturnsMatrix()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 4, 10), "E1", customerId: "C1"));

            var ctrl = CreateFollowController(_followRepo, "idlist=&sstart=2024-01-01&sdend=2024-06-30");

            var json = await ctrl.EmpMonthCusFollow(null!, "2024-01-01", "2024-06-30");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("张三", (string)data[0]["name"]!);
            Assert.Equal(1, (int)data[0]["m3"]!);
            Assert.Equal(1, (int)data[0]["m4"]!);
        }

        [Fact]
        public async Task Controller_EmpMonthCusFollow_StartAfterEnd_ReturnsError()
        {
            var ctrl = CreateFollowController(_followRepo);

            var json = await ctrl.EmpMonthCusFollow(null!, "2024-06-01", "2024-03-01");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("开始时间", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Controller_EmpMonthCusFollow_EmptyData_ReturnsEmptyArray()
        {
            var ctrl = CreateFollowController(_followRepo);

            var json = await ctrl.EmpMonthCusFollow(null!, "2024-01-01", "2024-06-30");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Empty(data);
        }

        // =========================================================
        // #09 EmpCusFollow 端点
        // =========================================================

        [Fact]
        public async Task Controller_EmpCusFollow_ValidParams_ReturnsYearMatrix()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1"));
            await InsertFollowAsync(NewFollow("F1", new DateTime(2024, 3, 5), "E1", customerId: "C1"));
            await InsertFollowAsync(NewFollow("F2", new DateTime(2024, 6, 15), "E1", customerId: "C1"));

            var ctrl = CreateFollowController(_followRepo);

            var json = await ctrl.EmpCusFollow(null!, 2024);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("张三", (string)data[0]["name"]!);
            Assert.Equal(2024, (int)data[0]["yy"]!);
            Assert.Equal(1, (int)data[0]["m3"]!);
            Assert.Equal(1, (int)data[0]["m6"]!);
        }

        [Fact]
        public async Task Controller_EmpCusFollow_InvalidYear_ReturnsError()
        {
            var ctrl = CreateFollowController(_followRepo);

            var json = await ctrl.EmpCusFollow(null!, 1900);
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("年份", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Controller_EmpCusFollow_EmptyData_ReturnsEmptyArray()
        {
            var ctrl = CreateFollowController(_followRepo);

            var json = await ctrl.EmpCusFollow(null!, 2024);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Empty(data);
        }
    }
}
