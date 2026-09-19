using FreeSql;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XHD.Core.Models;
using XHD.Core.Repository;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// #08 Funnel 漏斗 + #13/#14/#15 三报表单元测试。
    /// </summary>
    public class ReportTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly CRM_CustomerRepository _custRepo;

        public ReportTests()
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
            string createId,
            DateTime createTime,
            string cusTypeId = "",
            int state = 0,
            int isDelete = 0)
        {
            return new CRM_Customer
            {
                id = id,
                create_id = createId,
                create_time = createTime,
                cus_type_id = cusTypeId,
                cus_name = $"客户-{id}",
                state = state,
                isDelete = isDelete,
                isPrivate = 1,
                emp_id = createId,
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

        private static Sys_Param NewParam(string id, string name, int order, string type)
        {
            return new Sys_Param
            {
                id = id,
                params_name = name,
                params_order = order,
                params_type = type,
                create_id = "SYSTEM",
                create_time = new DateTime(2023, 1, 1),
                isDelete = 0
            };
        }

        private async Task InsertCustomerAsync(CRM_Customer c)
            => await _fsql.Insert(c).ExecuteAffrowsAsync();

        private async Task InsertEmployeeAsync(hr_employee e)
            => await _fsql.Insert(e).ExecuteAffrowsAsync();

        private async Task InsertParamAsync(Sys_Param p)
            => await _fsql.Insert(p).ExecuteAffrowsAsync();

        // =========================================================
        // #08 Funnel（客户漏斗，按 cus_type 分组）
        // =========================================================

        [Fact]
        public async Task Funnel_WithYear_ReturnsCountByType()
        {
            // Arrange：3 条 2024 客户，类型分布：T1(2) / T2(1)
            await InsertParamAsync(NewParam("T1", "意向", 1, "cus_type"));
            await InsertParamAsync(NewParam("T2", "高意向", 2, "cus_type"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10), cusTypeId: "T1"));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 6, 20), cusTypeId: "T1"));
            await InsertCustomerAsync(NewCustomer("C3", "E2", new DateTime(2024, 9, 15), cusTypeId: "T2"));
            // 额外：非 2024 客户应被过滤
            await InsertCustomerAsync(NewCustomer("C0", "E1", new DateTime(2023, 12, 31), cusTypeId: "T1"));

            // Act
            var arr = await _custRepo.FunnelAsync(2024);

            // Assert
            Assert.Equal(2, arr.Count);
            var t1 = arr.FirstOrDefault(o => (string)o["CustomerType"] == "意向");
            var t2 = arr.FirstOrDefault(o => (string)o["CustomerType"] == "高意向");
            Assert.NotNull(t1);
            Assert.NotNull(t2);
            Assert.Equal(2, (int)t1["cc"]!);
            Assert.Equal(1, (int)t2["cc"]!);
        }

        [Fact]
        public async Task Funnel_WithNullYear_ReturnsAllYears()
        {
            await InsertParamAsync(NewParam("T1", "意向", 1, "cus_type"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10), cusTypeId: "T1"));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2023, 3, 10), cusTypeId: "T1"));
            await InsertCustomerAsync(NewCustomer("C3", "E1", new DateTime(2022, 3, 10), cusTypeId: "T1"));

            var arr = await _custRepo.FunnelAsync(null);

            Assert.Single(arr);
            Assert.Equal(3, (int)arr[0]["cc"]!);
        }

        [Fact]
        public async Task Funnel_OrderedByParamsOrder()
        {
            // Arrange：故意乱序创建参数，检查输出按 params_order 升序
            await InsertParamAsync(NewParam("T3", "成交", 3, "cus_type"));
            await InsertParamAsync(NewParam("T1", "意向", 1, "cus_type"));
            await InsertParamAsync(NewParam("T2", "高意向", 2, "cus_type"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10), cusTypeId: "T3"));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 11), cusTypeId: "T1"));
            await InsertCustomerAsync(NewCustomer("C3", "E1", new DateTime(2024, 3, 12), cusTypeId: "T2"));

            // Act
            var arr = await _custRepo.FunnelAsync(2024);

            // Assert
            Assert.Equal(3, arr.Count);
            Assert.Equal(1, (int)arr[0]["params_order"]!);
            Assert.Equal(2, (int)arr[1]["params_order"]!);
            Assert.Equal(3, (int)arr[2]["params_order"]!);
        }

        [Fact]
        public async Task Funnel_EmptyDB_ReturnsEmptyArray()
        {
            var arr = await _custRepo.FunnelAsync(2024);
            Assert.Empty(arr);
        }

        [Fact]
        public async Task Funnel_CustomerWithNoType_MapsToUncategorized()
        {
            // Arrange：cus_type_id 不匹配任何 Sys_Param
            await InsertParamAsync(NewParam("T1", "意向", 1, "cus_type"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10), cusTypeId: "UNKNOWN"));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 11), cusTypeId: "T1"));

            // Act
            var arr = await _custRepo.FunnelAsync(2024);

            // Assert
            Assert.True(arr.Count >= 2);
            var uncategorized = arr.FirstOrDefault(o => (string)o["CustomerType"] == "未分类");
            Assert.NotNull(uncategorized);
            Assert.Equal(1, (int)uncategorized["cc"]!);
        }

        [Fact]
        public async Task Funnel_EmptyParamTable_AllCustomersUncategorized()
        {
            // Arrange：无任何 Sys_Param 记录
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10), cusTypeId: "T1"));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 11), cusTypeId: "T1"));

            // Act
            var arr = await _custRepo.FunnelAsync(2024);

            // Assert
            Assert.Single(arr);
            Assert.Equal("未分类", (string)arr[0]["CustomerType"]!);
            Assert.Equal(2, (int)arr[0]["cc"]!);
        }

        // =========================================================
        // #13 ReportEmpCusAsync（员工年度客户新增，PIVOT）
        // =========================================================

        [Fact]
        public async Task ReportEmpCus_PivotByMonth_CorrectCount()
        {
            // Arrange：E1 3 月 2 个 + 6 月 5 个
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            for (int i = 0; i < 2; i++)
                await InsertCustomerAsync(NewCustomer($"C{i}", "E1", new DateTime(2024, 3, 5 + i)));
            for (int i = 0; i < 5; i++)
                await InsertCustomerAsync(NewCustomer($"C{10 + i}", "E1", new DateTime(2024, 6, 5 + i)));

            // Act
            var arr = await _custRepo.ReportEmpCusAsync(2024, null);

            // Assert
            Assert.Single(arr);
            Assert.Equal("张三", (string)arr[0]["name"]!);
            Assert.Equal(2024, (int)arr[0]["yy"]!);
            Assert.Equal(2, (int)arr[0]["m3"]!);
            Assert.Equal(5, (int)arr[0]["m6"]!);
            Assert.Equal(0, (int)arr[0]["m1"]!);
            Assert.Equal(0, (int)arr[0]["m12"]!);
        }

        [Fact]
        public async Task ReportEmpCus_NoCustomersInYear_AllMonthsZero()
        {
            // Arrange：员工存在但 2024 无客户
            await InsertEmployeeAsync(NewEmployee("E1", "李四"));

            // Act
            var arr = await _custRepo.ReportEmpCusAsync(2024, null);

            // Assert
            Assert.Single(arr);
            Assert.Equal("李四", (string)arr[0]["name"]!);
            for (int m = 1; m <= 12; m++)
            {
                Assert.Equal(0, (int)arr[0][$"m{m}"]!);
            }
        }

        [Fact]
        public async Task ReportEmpCus_WithEmpIdsFilter_OnlyFilteredEmployee()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10)));
            await InsertCustomerAsync(NewCustomer("C2", "E2", new DateTime(2024, 3, 11)));

            var arr = await _custRepo.ReportEmpCusAsync(2024, new List<string> { "E1" });

            Assert.Single(arr);
            Assert.Equal("张三", (string)arr[0]["name"]!);
            Assert.Equal(1, (int)arr[0]["m3"]!);
        }

        [Fact]
        public async Task ReportEmpCus_YearBoundary_FiltersOutOfYear()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            // 2023 年底 + 2024 年内 + 2025 年初
            await InsertCustomerAsync(NewCustomer("C0", "E1", new DateTime(2023, 12, 31)));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 1, 1)));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 12, 31)));
            await InsertCustomerAsync(NewCustomer("C3", "E1", new DateTime(2025, 1, 1)));

            var arr = await _custRepo.ReportEmpCusAsync(2024, null);

            Assert.Single(arr);
            Assert.Equal(1, (int)arr[0]["m1"]!);
            Assert.Equal(1, (int)arr[0]["m12"]!);
            // 总客户数 = m1 + m12 = 2
            int total = 0;
            for (int m = 1; m <= 12; m++) total += (int)arr[0][$"m{m}"]!;
            Assert.Equal(2, total);
        }

        [Fact]
        public async Task ReportEmpCus_EmptyDB_ReturnsEmpty()
        {
            var arr = await _custRepo.ReportEmpCusAsync(2024, null);
            Assert.Empty(arr);
        }

        [Fact]
        public async Task ReportEmpCus_ExcludesSoftDeleted()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10), isDelete: 0));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 11), isDelete: 1));

            var arr = await _custRepo.ReportEmpCusAsync(2024, null);

            Assert.Single(arr);
            Assert.Equal(1, (int)arr[0]["m3"]!);
        }

        // =========================================================
        // #14 ReportMonthEmpCusAsync（员工月度客户新增）
        // =========================================================

        [Fact]
        public async Task ReportMonthEmpCus_WithDateRange_FiltersByDate()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 5)));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 15)));
            await InsertCustomerAsync(NewCustomer("C3", "E1", new DateTime(2024, 3, 25)));
            await InsertCustomerAsync(NewCustomer("C4", "E1", new DateTime(2024, 4, 5)));
            await InsertCustomerAsync(NewCustomer("C5", "E1", new DateTime(2024, 2, 10)));

            var arr = await _custRepo.ReportMonthEmpCusAsync(
                new DateTime(2024, 3, 1),
                new DateTime(2024, 3, 31, 23, 59, 59),
                null);

            Assert.Single(arr);
            Assert.Equal(3, (int)arr[0]["m3"]!);
            Assert.Equal(0, (int)arr[0]["m4"]!);
            Assert.Equal(0, (int)arr[0]["m2"]!);
        }

        [Fact]
        public async Task ReportMonthEmpCus_NullDateRange_ReturnsAll()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 1, 10)));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 6, 15)));
            await InsertCustomerAsync(NewCustomer("C3", "E1", new DateTime(2023, 8, 20)));

            var arr = await _custRepo.ReportMonthEmpCusAsync(null, null, null);

            Assert.Single(arr);
            Assert.Equal(1, (int)arr[0]["m1"]!);
            Assert.Equal(1, (int)arr[0]["m6"]!);
            Assert.Equal(1, (int)arr[0]["m8"]!);
        }

        [Fact]
        public async Task ReportMonthEmpCus_MultipleEmployees_ReturnsEachRow()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10)));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 11)));
            await InsertCustomerAsync(NewCustomer("C3", "E2", new DateTime(2024, 3, 12)));

            var arr = await _custRepo.ReportMonthEmpCusAsync(null, null, null);

            Assert.Equal(2, arr.Count);
            var e1 = arr.FirstOrDefault(o => (string)o["name"] == "张三");
            var e2 = arr.FirstOrDefault(o => (string)o["name"] == "李四");
            Assert.Equal(2, (int)e1["m3"]!);
            Assert.Equal(1, (int)e2["m3"]!);
        }

        [Fact]
        public async Task ReportMonthEmpCus_WithEmpIdsFilter_OnlyFiltered()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10)));
            await InsertCustomerAsync(NewCustomer("C2", "E2", new DateTime(2024, 3, 11)));

            var arr = await _custRepo.ReportMonthEmpCusAsync(null, null, new List<string> { "E2" });

            Assert.Single(arr);
            Assert.Equal("李四", (string)arr[0]["name"]!);
            Assert.Equal(1, (int)arr[0]["m3"]!);
        }

        [Fact]
        public async Task ReportMonthEmpCus_EmptyDB_ReturnsEmpty()
        {
            var arr = await _custRepo.ReportMonthEmpCusAsync(null, null, null);
            Assert.Empty(arr);
        }

        // =========================================================
        // #15 ComparedEmpCusAddAsync（员工双月对比）
        // =========================================================

        [Fact]
        public async Task ComparedEmpCusAdd_TwoMonthComparison_CorrectDiff()
        {
            // Arrange：E1 三月 2 个 + 四月 5 个
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            for (int i = 0; i < 2; i++)
                await InsertCustomerAsync(NewCustomer($"C1{i}", "E1", new DateTime(2024, 3, 5 + i)));
            for (int i = 0; i < 5; i++)
                await InsertCustomerAsync(NewCustomer($"C2{i}", "E1", new DateTime(2024, 4, 5 + i)));

            // Act：startMonth=3, endMonth=4
            var arr = await _custRepo.ComparedEmpCusAddAsync(3, 4, 2024, null);

            // Assert
            Assert.Single(arr);
            Assert.Equal("张三", (string)arr[0]["name"]!);
            Assert.Equal(3, (int)arr[0]["startMonth"]!);
            Assert.Equal(4, (int)arr[0]["endMonth"]!);
            Assert.Equal(2, (int)arr[0]["startMonth_count"]!);
            Assert.Equal(5, (int)arr[0]["endMonth_count"]!);
            Assert.Equal(3, (int)arr[0]["diff"]!);  // 5 - 2 = 3
        }

        [Fact]
        public async Task ComparedEmpCusAdd_NoMatchingData_ReturnsZeroCounts()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));

            var arr = await _custRepo.ComparedEmpCusAddAsync(3, 4, 2024, null);

            Assert.Single(arr);
            Assert.Equal(0, (int)arr[0]["startMonth_count"]!);
            Assert.Equal(0, (int)arr[0]["endMonth_count"]!);
            Assert.Equal(0, (int)arr[0]["diff"]!);
        }

        [Fact]
        public async Task ComparedEmpCusAdd_CrossYear_StillSameYear()
        {
            // Arrange：year=2024，但 2023 和 2025 数据
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2023, 3, 10)));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 10)));
            await InsertCustomerAsync(NewCustomer("C3", "E1", new DateTime(2025, 3, 10)));

            // Act
            var arr = await _custRepo.ComparedEmpCusAddAsync(3, 3, 2024, null);

            // Assert：只有 2024 年内的 3 月客户被计入
            Assert.Single(arr);
            Assert.Equal(1, (int)arr[0]["startMonth_count"]!);
            Assert.Equal(1, (int)arr[0]["endMonth_count"]!);
            Assert.Equal(0, (int)arr[0]["diff"]!);
        }

        [Fact]
        public async Task ComparedEmpCusAdd_WithEmpIdsFilter_OnlyFiltered()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertEmployeeAsync(NewEmployee("E2", "李四"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10)));
            await InsertCustomerAsync(NewCustomer("C2", "E2", new DateTime(2024, 3, 11)));
            await InsertCustomerAsync(NewCustomer("C3", "E2", new DateTime(2024, 4, 11)));

            // Act：只统计 E2，startMonth=3, endMonth=4
            var arr = await _custRepo.ComparedEmpCusAddAsync(3, 4, 2024, new List<string> { "E2" });

            // Assert
            Assert.Single(arr);
            Assert.Equal("李四", (string)arr[0]["name"]!);
            Assert.Equal(1, (int)arr[0]["startMonth_count"]!);
            Assert.Equal(1, (int)arr[0]["endMonth_count"]!);
            Assert.Equal(0, (int)arr[0]["diff"]!);
        }

        [Fact]
        public async Task ComparedEmpCusAdd_EmptyDB_ReturnsEmpty()
        {
            var arr = await _custRepo.ComparedEmpCusAddAsync(3, 4, 2024, null);
            Assert.Empty(arr);
        }

        [Fact]
        public async Task ComparedEmpCusAdd_NullMonths_BothCountsZero()
        {
            await InsertEmployeeAsync(NewEmployee("E1", "张三"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10)));

            // Act：startMonth / endMonth 都传 null
            var arr = await _custRepo.ComparedEmpCusAddAsync(null, null, 2024, null);

            // Assert
            Assert.Single(arr);
            // JObject["key"] 对 JSON null 返回 JValue(Type=Null)，不是 C# null
            // .Value<object>() 对 JValue.Null 不保证返回 C# null，用 Type 显式判断
            JToken sm = arr[0]["startMonth"];
            JToken em = arr[0]["endMonth"];
            Assert.True(sm == null || sm.Type == JTokenType.Null, $"startMonth not null: {sm}");
            Assert.True(em == null || em.Type == JTokenType.Null, $"endMonth not null: {em}");
            Assert.Equal(0, (int)arr[0]["startMonth_count"]!);
            Assert.Equal(0, (int)arr[0]["endMonth_count"]!);
            Assert.Equal(0, (int)arr[0]["diff"]!);
        }

        // =========================================================
        // Sprint 3 Wave 2 #11：Funnel 扩展 stype_val 参数
        // =========================================================

        [Fact]
        public async Task Funnel_WithTypeIdWhitelist_OnlyReturnsMatchingTypes()
        {
            // Arrange：3 种类型 T1/T2/T3，白名单仅 T1+T2
            await InsertParamAsync(NewParam("T1", "意向", 1, "cus_type"));
            await InsertParamAsync(NewParam("T2", "高意向", 2, "cus_type"));
            await InsertParamAsync(NewParam("T3", "成交", 3, "cus_type"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10), cusTypeId: "T1"));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 11), cusTypeId: "T2"));
            await InsertCustomerAsync(NewCustomer("C3", "E1", new DateTime(2024, 3, 12), cusTypeId: "T3"));
            await InsertCustomerAsync(NewCustomer("C4", "E1", new DateTime(2024, 3, 13), cusTypeId: "T3"));

            // Act
            var arr = await _custRepo.FunnelAsync(2024, new List<string> { "T1", "T2" });

            // Assert：仅返回 T1+T2 两类，T3 被过滤
            Assert.Equal(2, arr.Count);
            var typeIds = arr.Select(o => (string)o["CustomerType_id"]).ToList();
            Assert.Contains("T1", typeIds);
            Assert.Contains("T2", typeIds);
            Assert.DoesNotContain("T3", typeIds);
        }

        [Fact]
        public async Task Funnel_WithEmptyTypeIds_FallsBackToUnlimited()
        {
            // Arrange：空白名单应等价于 null，走原逻辑返回全部
            await InsertParamAsync(NewParam("T1", "意向", 1, "cus_type"));
            await InsertParamAsync(NewParam("T2", "高意向", 2, "cus_type"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10), cusTypeId: "T1"));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 11), cusTypeId: "T2"));

            // Act
            var arr = await _custRepo.FunnelAsync(2024, new List<string>());

            // Assert
            Assert.Equal(2, arr.Count);
        }

        [Fact]
        public async Task Funnel_WithNullTypeIds_FallsBackToUnlimited()
        {
            // Arrange：null 白名单保持原逻辑
            await InsertParamAsync(NewParam("T1", "意向", 1, "cus_type"));
            await InsertParamAsync(NewParam("T2", "高意向", 2, "cus_type"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10), cusTypeId: "T1"));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 11), cusTypeId: "T2"));

            // Act
            var arr = await _custRepo.FunnelAsync(2024, null);

            // Assert
            Assert.Equal(2, arr.Count);
        }

        [Fact]
        public async Task Funnel_WithUnknownTypeInWhitelist_ReturnsOnlyMatchedTypes()
        {
            // Arrange：白名单含不存在的类型 ID "T4"，应仅返回存在的 T1
            await InsertParamAsync(NewParam("T1", "意向", 1, "cus_type"));
            await InsertParamAsync(NewParam("T2", "高意向", 2, "cus_type"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10), cusTypeId: "T1"));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 11), cusTypeId: "T2"));

            // Act
            var arr = await _custRepo.FunnelAsync(2024, new List<string> { "T1", "T4" });

            // Assert
            Assert.Single(arr);
            Assert.Equal("T1", (string)arr[0]["CustomerType_id"]);
        }

        [Fact]
        public async Task Funnel_WithYearAndTypeIds_BothFiltersApplied()
        {
            // Arrange：验证 year 与 typeIds 两条件叠加生效
            await InsertParamAsync(NewParam("T1", "意向", 1, "cus_type"));
            await InsertParamAsync(NewParam("T2", "高意向", 2, "cus_type"));
            await InsertCustomerAsync(NewCustomer("C1", "E1", new DateTime(2024, 3, 10), cusTypeId: "T1"));
            await InsertCustomerAsync(NewCustomer("C2", "E1", new DateTime(2024, 3, 11), cusTypeId: "T2"));
            await InsertCustomerAsync(NewCustomer("C3", "E1", new DateTime(2023, 3, 10), cusTypeId: "T1")); // 非 2024
            await InsertCustomerAsync(NewCustomer("C4", "E1", new DateTime(2024, 4, 1), cusTypeId: "T2"));

            // Act：year=2024 + 白名单仅 T1
            var arr = await _custRepo.FunnelAsync(2024, new List<string> { "T1" });

            // Assert：仅 2024 年且类型 T1 的 1 条
            Assert.Single(arr);
            Assert.Equal("T1", (string)arr[0]["CustomerType_id"]);
            Assert.Equal(1, (int)arr[0]["cc"]!);
        }
    }
}
