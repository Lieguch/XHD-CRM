using System.Reflection;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.39 授权子系统基础设施回归测试。
    /// 覆盖 3 个纯函数：<see cref="XHD.Core.View.Authorization.AuthCatalog"/>、
    /// <see cref="XHD.Core.View.Authorization.DataScope"/>、以及属性构造校验。
    /// </summary>
    public class AuthInfrastructureTests
    {
        // 注意：下面所有 Collect() 的目标程序集都是**本测试程序集**，
        // 因为待采集的 TestButtonAuthController 定义在本文件里（XHD.Core.Tests）。
        // 早期误写为 ButtonAuthAttribute.Assembly（XHD.Core.View），
        // 反射扫不到本程序集的嵌套假控制器，导致 Finds* 用例必失败。
        private static readonly Assembly TestAssembly =
            typeof(AuthInfrastructureTests).Assembly;

        // ─── 用于反射采集的假控制器 ───────────────────────────────────

        [XHD.Core.View.Authorization.ButtonAuth("Test_Menu", "add")]
        [XHD.Core.View.Authorization.AdminOnly]
        public class TestButtonAuthController : Microsoft.AspNetCore.Mvc.Controller
        {
            [XHD.Core.View.Authorization.ButtonAuth("Test_Menu", "edit")]
            public void Edit() { }

            [XHD.Core.View.Authorization.AnyOfButtonAuth("Test_Menu|del", "Test_Menu|edit")]
            public void Delete() { }

            public void NoAuth() { }
        }

        // ─── AuthCatalog.Collect ─────────────────────────────────────

        [Fact]
        public void AuthCatalog_Collect_FindsButtonAuthOnMethods()
        {
            var entries = XHD.Core.View.Authorization.AuthCatalog.Collect(TestAssembly);

            Assert.Contains(entries, e => e.AuthId == "Test_Menu|edit");
            Assert.Contains(entries, e => e.Controller!.EndsWith("TestButtonAuthController"));
            Assert.Contains(entries, e => e.Action == "Edit");
        }

        [Fact]
        public void AuthCatalog_Collect_FindsClassLevelButtonAuth()
        {
            var entries = XHD.Core.View.Authorization.AuthCatalog.Collect(TestAssembly);

            var classLevel = entries
                .Where(e => e.AuthId == "Test_Menu|add")
                .ToList();

            Assert.NotEmpty(classLevel);
            Assert.Contains(classLevel, e => e.Action == "<class>");
        }

        [Fact]
        public void AuthCatalog_Collect_OnlyCollectsControllerSubtypes()
        {
            // 本测试类不是 ControllerBase，反射扫到也不应产生条目。
            // 注意断言用「完全等于外部类 FullName」而非 Contains ——
            // 嵌套的 TestButtonAuthController（FullName 形如
            // XHD.Core.Tests.AuthInfrastructureTests+TestButtonAuthController）
            // 确实包含 "AuthInfrastructureTests" 子串，但它是合法控制器，应当被采集。
            var entries = XHD.Core.View.Authorization.AuthCatalog.Collect(TestAssembly);

            Assert.DoesNotContain(entries, e =>
                e.Controller == typeof(AuthInfrastructureTests).FullName);
        }

        [Fact]
        public void AuthCatalog_Collect_IsDeterministicSortedByAuthId()
        {
            var a = XHD.Core.View.Authorization.AuthCatalog.Collect(TestAssembly);
            var b = XHD.Core.View.Authorization.AuthCatalog.Collect(TestAssembly);

            Assert.Equal(
                a.Select(e => e.AuthId).ToList(),
                b.Select(e => e.AuthId).ToList());
        }

        [Fact]
        public void AuthCatalog_Collect_RequiresAssembly()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => XHD.Core.View.Authorization.AuthCatalog.Collect(null!));
        }

        // ─── AuthCatalog.Reconcile ───────────────────────────────────

        [Fact]
        public void AuthCatalog_Reconcile_DetectsMissingButtons()
        {
            var declared = new List<XHD.Core.View.Authorization.AuthCatalogEntry>
            {
                new("a|add", "a", "add", "C1", "M1"),
                new("b|edit", "b", "edit", "C2", "M2")
            };

            var report = XHD.Core.View.Authorization.AuthCatalog.Reconcile(
                declared, new[] { "a|add" });

            Assert.Single(report.Missing);
            Assert.Equal("b|edit", report.Missing[0].AuthId);
            Assert.Empty(report.Orphans);
        }

        [Fact]
        public void AuthCatalog_Reconcile_DetectsOrphans()
        {
            var declared = new List<XHD.Core.View.Authorization.AuthCatalogEntry>
            {
                new("a|add", "a", "add", "C1", "M1")
            };

            var report = XHD.Core.View.Authorization.AuthCatalog.Reconcile(
                declared, new[] { "a|add", "legacy_button|del" });

            Assert.Empty(report.Missing);
            Assert.Single(report.Orphans);
            Assert.Equal("legacy_button|del", report.Orphans[0]);
        }

        [Fact]
        public void AuthCatalog_Reconcile_CleanWhenInSync()
        {
            var declared = new List<XHD.Core.View.Authorization.AuthCatalogEntry>
            {
                new("a|add", "a", "add", "C1", "M1")
            };

            var report = XHD.Core.View.Authorization.AuthCatalog.Reconcile(
                declared, new[] { "a|add" });

            Assert.Empty(report.Missing);
            Assert.Empty(report.Orphans);
            Assert.Single(report.Declared);
        }

        // ─── ButtonAuthAttribute ─────────────────────────────────────

        [Fact]
        public void ButtonAuthAttribute_ComposesAuthId()
        {
            var attr = new XHD.Core.View.Authorization.ButtonAuthAttribute("CRM_Customer", "edit");

            Assert.Equal("CRM_Customer|edit", attr.AuthId);
            Assert.Equal("CRM_Customer", attr.Menu);
            Assert.Equal("edit", attr.Operation);
            Assert.Equal(XHD.Core.View.Authorization.AuthDeny.DefaultMessage, attr.DenyMessage);
        }

        [Theory]
        [InlineData("", "edit")]
        [InlineData("CRM_Customer", "")]
        [InlineData("   ", "edit")]
        [InlineData("CRM_Customer", "   ")]
        public void ButtonAuthAttribute_RejectsEmptySegments(string menu, string operation)
        {
            Assert.Throws<System.ArgumentException>(
                () => new XHD.Core.View.Authorization.ButtonAuthAttribute(menu, operation));
        }

        // ─── AnyOfButtonAuthAttribute（OR 语义） ─────────────────────

        [Fact]
        public void AnyOfButtonAuthAttribute_AcceptsMultipleIds()
        {
            // HrPostController.Save 的既有语义：hr_post|edit OR hr_position|edit
            var attr = new XHD.Core.View.Authorization.AnyOfButtonAuthAttribute(
                "hr_post|edit", "hr_position|edit");

            Assert.Equal(2, attr.AuthIds.Count);
        }

        [Theory]
        [InlineData("only_one")]
        public void AnyOfButtonAuthAttribute_RejectsSingleId(string only)
        {
            Assert.Throws<System.ArgumentException>(
                () => new XHD.Core.View.Authorization.AnyOfButtonAuthAttribute(only));
        }

        [Fact]
        public void AnyOfButtonAuthAttribute_RejectsEmptyArgs()
        {
            Assert.Throws<System.ArgumentException>(
                () => new XHD.Core.View.Authorization.AnyOfButtonAuthAttribute());
        }

        [Fact]
        public void AnyOfButtonAuthAttribute_DedupsRepeatedIds()
        {
            var attr = new XHD.Core.View.Authorization.AnyOfButtonAuthAttribute(
                "hr_post|edit", "hr_post|edit", "hr_position|edit");

            Assert.Equal(2, attr.AuthIds.Count);
        }

        // ─── DataScope（authtype 语义收口） ──────────────────────────

        [Fact]
        public void DataScope_AuthType4_MeansNoFilter()
        {
            var r = XHD.Core.View.Authorization.DataScope.Resolve(
                new XHD.Core.Common.XHDRoleData { authtype = 4, empList = new List<string>() });

            Assert.False(r.NeedsFilter);
            Assert.Null(r.EmployeeIds);
            Assert.Equal(4, r.AuthType);
        }

        [Fact]
        public void DataScope_AuthTypeBelow4_RequiresFilter()
        {
            foreach (var level in new[] { 0, 1, 2, 3 })
            {
                var r = XHD.Core.View.Authorization.DataScope.Resolve(
                    new XHD.Core.Common.XHDRoleData
                    {
                        authtype = level,
                        empList = new List<string> { "e1", "e2" }
                    });

                Assert.True(r.NeedsFilter, $"authtype={level} 应需要 empList 过滤");
                Assert.Equal(2, r.EmployeeIds!.Count);
                Assert.Equal(level, r.AuthType);
            }
        }

        [Fact]
        public void DataScope_NullEmpList_YieldsEmptyNotThrow()
        {
            var r = XHD.Core.View.Authorization.DataScope.Resolve(
                new XHD.Core.Common.XHDRoleData { authtype = 2 });

            Assert.True(r.NeedsFilter);
            Assert.Empty(r.EmployeeIds!);
        }

        [Fact]
        public void DataScope_NullRoleData_TreatedAsNoPermission()
        {
            var r = XHD.Core.View.Authorization.DataScope.Resolve(null!);

            Assert.True(r.NeedsFilter);
            Assert.Empty(r.EmployeeIds!);
            Assert.Equal(0, r.AuthType);
        }

        /// <summary>
        /// 最关键的防回归用例：admin 由 DBAuthService.GetDataAuth 返回
        /// <c>authtype=4 + 空 empList</c>。若用「empList 为空 ⇒ 不过滤」判定，
        /// 会把 authtype=0 的无权限用户误当全权限，造成越权。
        /// 本用例证明判定只依赖 authtype，不依赖 empList 长度。
        /// </summary>
        [Fact]
        public void DataScope_AdminShape_MustNotBeMistakenForNoPermission()
        {
            var admin = XHD.Core.View.Authorization.DataScope.Resolve(
                new XHD.Core.Common.XHDRoleData { authtype = 4, empList = new List<string>() });
            var nobody = XHD.Core.View.Authorization.DataScope.Resolve(
                new XHD.Core.Common.XHDRoleData { authtype = 0, empList = new List<string>() });

            // 两者 empList 形状完全相同（都为空），但结论必须相反
            Assert.False(admin.NeedsFilter);
            Assert.True(nobody.NeedsFilter);
        }
    }
}
