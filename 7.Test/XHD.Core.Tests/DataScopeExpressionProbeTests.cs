// SPDX-License-Identifier: MIT
// Sprint 10.40 — 数据权限范围表达式实证探针（真实 SQLite 内存库）
//
// 背景（为什么需要这个探针）：
//   Sprint 10.33 的「[v11] 数据权限注入」在多个控制器的 Grid 过滤与编辑/删除归属校验里
//   使用了导航表达式（a.customer.emp_id / a.Order.customer.emp_id）。但：
//     ① 4.Entity 模型层【没有任何】[Navigate] 特性（全仓 grep = 0 命中）；
//     ② BaseRepository.GridAsync 只是 _fsql.Select<T>().Where(exp).ToListAsync()，
//        没有 .Include(...) 贪婪加载。
//   FreeSql 官方文档声称支持「约定命名」导航（customer_id 列 + customer 实体属性自动关联），
//   但从未在真实构建里验证过。本测试用真实数据库给出可复现答案，决定三仓库分叉合并时
//   Grid 过滤与归属校验各应采用哪一套字段口径（本地的 emp_id/create_id/employee_id，
//   还是 CNB 的 customer.emp_id / Order.customer.emp_id）。
//
// 探针问题：
//   Q1 一跳导航 Where(a => scope.Contains(a.customer.emp_id)) 能否翻译成 SQL JOIN？
//   Q2 两跳导航 Where(a => scope.Contains(a.Order.customer.emp_id)) 能否翻译？
//   Q3 ToListAsync() 之后不调 Include，返回实体的导航属性是否已被回填？
//      （归属校验 existing.customer.emp_id 依赖 Q3 为真）
//
// 数据：E1/E2 两个业务员；C1 归 E1、C2 归 E2；各业务实体各挂一条在 C1、一条在 C2。
//       期望 scope={E1} 时每类实体恰好命中 1 条（挂在 C1 上的那条）。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using XHD.Core.Models;
using Xunit;

namespace XHD.Core.Tests
{
    public class DataScopeExpressionProbeTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private static readonly List<string> ScopeE1 = new List<string> { "E1" };

        public DataScopeExpressionProbeTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static hr_employee NewEmployee(string id, string name) => new hr_employee
        {
            id = id,
            name = name,
            create_id = "SYSTEM",
            create_time = new DateTime(2023, 1, 1),
            isDelete = 0
        };

        private static CRM_Customer NewCustomer(string id, string empId) => new CRM_Customer
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

        private static CRM_follow NewFollow(string id, string customerId, string employeeId) => new CRM_follow
        {
            id = id,
            customer_id = customerId,
            employee_id = employeeId,
            follow_time = new DateTime(2024, 6, 1)
        };

        private static CRM_Contact NewContact(string id, string customerId, string createId) => new CRM_Contact
        {
            id = id,
            customer_id = customerId,
            create_id = createId,
            C_name = $"联系人-{id}"
        };

        private static Sale_order NewOrder(string id, string customerId, string empId) => new Sale_order
        {
            id = id,
            customer_id = customerId,
            emp_id = empId,
            Order_date = new DateTime(2024, 6, 1),
            Order_amount = 1000m,
            total_amount = 1000m,
            create_id = empId,
            create_time = new DateTime(2024, 6, 1),
            isDelete = 0
        };

        private static Finance_Invoice NewInvoice(string id, string orderId, string createId) => new Finance_Invoice
        {
            id = id,
            order_id = orderId,
            create_id = createId,
            create_time = new DateTime(2024, 6, 1),
            invoice_num = $"INV-{id}"
        };

        private static Finance_Receive NewReceive(string id, string orderId, string createId) => new Finance_Receive
        {
            id = id,
            order_id = orderId,
            create_id = createId,
            create_time = new DateTime(2024, 6, 1),
            Receive_num = $"REC-{id}"
        };

        private async Task SeedAsync()
        {
            await _fsql.Insert(NewEmployee("E1", "业务员一")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewEmployee("E2", "业务员二")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewCustomer("C1", "E1")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewCustomer("C2", "E2")).ExecuteAffrowsAsync();

            await _fsql.Insert(NewFollow("F1", "C1", "E1")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewFollow("F2", "C2", "E2")).ExecuteAffrowsAsync();

            await _fsql.Insert(NewContact("T1", "C1", "E1")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewContact("T2", "C2", "E2")).ExecuteAffrowsAsync();

            await _fsql.Insert(NewOrder("O1", "C1", "E1")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewOrder("O2", "C2", "E2")).ExecuteAffrowsAsync();

            await _fsql.Insert(NewInvoice("INV1", "O1", "E1")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewInvoice("INV2", "O2", "E2")).ExecuteAffrowsAsync();

            await _fsql.Insert(NewReceive("REC1", "O1", "E1")).ExecuteAffrowsAsync();
            await _fsql.Insert(NewReceive("REC2", "O2", "E2")).ExecuteAffrowsAsync();
        }

        // ============ Q1：一跳导航 Where 翻译 ============

        [Fact]
        public async Task Q1_CRM_follow_Grid_Where_CustomerEmpId()
        {
            await SeedAsync();

            // CRMFollowController.Grid 的 [v11] 口径（CNB 侧）
            var rows = await _fsql.Select<CRM_follow>()
                .Where(a => ScopeE1.Contains(a.customer.emp_id))
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal("F1", rows[0].id);
        }

        [Fact]
        public async Task Q1_CRM_Contact_Grid_Where_CustomerEmpId()
        {
            await SeedAsync();

            var rows = await _fsql.Select<CRM_Contact>()
                .Where(a => ScopeE1.Contains(a.customer.emp_id))
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal("T1", rows[0].id);
        }

        [Fact]
        public async Task Q1_Sale_order_Grid_Where_CustomerEmpId()
        {
            await SeedAsync();

            // SaleOrderController.Grid 的 CNB 口径
            var rows = await _fsql.Select<Sale_order>()
                .Where(a => ScopeE1.Contains(a.customer.emp_id))
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal("O1", rows[0].id);
        }

        // ============ Q2：两跳导航 Where 翻译 ============

        [Fact]
        public async Task Q2_Finance_Invoice_Grid_Where_OrderCustomerEmpId()
        {
            await SeedAsync();

            // FinanceInvoiceController.Grid 的 [v11] 口径（发票 → 订单 → 客户）
            var rows = await _fsql.Select<Finance_Invoice>()
                .Where(a => ScopeE1.Contains(a.Order.customer.emp_id))
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal("INV1", rows[0].id);
        }

        [Fact]
        public async Task Q2_Finance_Receive_Grid_Where_OrderCustomerEmpId()
        {
            await SeedAsync();

            var rows = await _fsql.Select<Finance_Receive>()
                .Where(a => ScopeE1.Contains(a.Order.customer.emp_id))
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal("REC1", rows[0].id);
        }

        // ============ 对照组：本地当前口径（普通列） ============

        [Fact]
        public async Task Control_Finance_Invoice_Grid_Where_EmpId_IsEmpty()
        {
            await SeedAsync();

            // FinanceInvoiceController.Grid 的本地口径：a.emp_id
            // 发票新增路径只写 create_id，从不写 emp_id（Finance_Invoice.emp_id 恒为空串）
            // ⇒ 非全员权限角色用这个过滤将看到 0 条 —— 这是功能性 bug 的实证
            var rows = await _fsql.Select<Finance_Invoice>()
                .Where(a => ScopeE1.Contains(a.emp_id))
                .ToListAsync();

            Assert.Empty(rows);
        }

        [Fact]
        public async Task Control_Sale_order_Grid_Where_EmpId()
        {
            await SeedAsync();

            // 本地口径：订单按自己的 emp_id 过滤（该列在新增时会被赋值）
            var rows = await _fsql.Select<Sale_order>()
                .Where(a => ScopeE1.Contains(a.emp_id))
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal("O1", rows[0].id);
        }

        // ============ Q3：ToListAsync 后导航是否回填（结论：不回填） ============
        // BaseRepository.GridAsync 无 .Include()，模型无 [Navigate] 的贪婪加载，
        // ToListAsync() 返回的实体导航属性恒为 null。
        // ⇒ 归属校验绝不能写 existing.customer.emp_id（会 NullReferenceException），
        //   必须改用 Q5 的「范围内查询」（SQL 侧导航过滤）。以下两条是回归看门。

        [Fact]
        public async Task Q3_CRM_follow_Fetch_NavigationIsNullWithoutInclude()
        {
            await SeedAsync();

            var follow = (await _fsql.Select<CRM_follow>()
                    .Where(a => a.id == "F1")
                    .ToListAsync())
                .FirstOrDefault();

            Assert.NotNull(follow);
            Assert.Null(follow!.customer);
        }

        [Fact]
        public async Task Q3_Finance_Invoice_Fetch_OrderNavigationIsNullWithoutInclude()
        {
            await SeedAsync();

            var invoice = (await _fsql.Select<Finance_Invoice>()
                    .Where(a => a.id == "INV1")
                    .ToListAsync())
                .FirstOrDefault();

            Assert.NotNull(invoice);
            Assert.Null(invoice!.Order);
        }

        // ============ Q4：空范围列表（authtype==0）Where 是否安全翻译 ============

        [Fact]
        public async Task Q4_EmptyScopeList_Where_YieldsZeroRows()
        {
            await SeedAsync();

            var empty = new List<string>();

            // authtype==0 时 empList 为空列表，五个实体的范围过滤都应得到 0 行且不抛异常
            Assert.Empty(await _fsql.Select<CRM_follow>()
                .Where(a => empty.Contains(a.customer.emp_id)).ToListAsync());
            Assert.Empty(await _fsql.Select<CRM_Contact>()
                .Where(a => empty.Contains(a.customer.emp_id)).ToListAsync());
            Assert.Empty(await _fsql.Select<Sale_order>()
                .Where(a => empty.Contains(a.emp_id)).ToListAsync());
            Assert.Empty(await _fsql.Select<Finance_Invoice>()
                .Where(a => empty.Contains(a.Order.customer.emp_id)).ToListAsync());
            Assert.Empty(await _fsql.Select<Finance_Receive>()
                .Where(a => empty.Contains(a.Order.customer.emp_id)).ToListAsync());
        }

        // ============ Q5：归属校验新写法（范围内查询）是否正确 ============

        [Fact]
        public async Task Q5_ScopedOwnershipQuery_SeparatesInScopeFromOutOfScope()
        {
            await SeedAsync();

            // 范围内查询：id 命中且客户归属在 scope 内 —— 编辑/删除归属校验就靠它
            var inScope = (await _fsql.Select<CRM_follow>()
                    .Where(a => a.id == "F1" && ScopeE1.Contains(a.customer.emp_id))
                    .ToListAsync());
            Assert.Single(inScope);

            // F2 挂在 C2（归属 E2），E1 的范围内查询必须查不到它（越权拦截）
            var outOfScope = (await _fsql.Select<CRM_follow>()
                    .Where(a => a.id == "F2" && ScopeE1.Contains(a.customer.emp_id))
                    .ToListAsync());
            Assert.Empty(outOfScope);
        }

        [Fact]
        public async Task Q5_ScopedOwnershipQuery_TwoHop_Finance_Invoice()
        {
            await SeedAsync();

            var inScope = (await _fsql.Select<Finance_Invoice>()
                    .Where(a => a.id == "INV1" && ScopeE1.Contains(a.Order.customer.emp_id))
                    .ToListAsync());
            Assert.Single(inScope);

            var outOfScope = (await _fsql.Select<Finance_Invoice>()
                    .Where(a => a.id == "INV2" && ScopeE1.Contains(a.Order.customer.emp_id))
                    .ToListAsync());
            Assert.Empty(outOfScope);
        }
    }
}
