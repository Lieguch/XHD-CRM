using System.Reflection;
using XHD.Core.Models;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.38 P2-9/10：移动端 API 契约回归守卫。
    ///
    /// 背景：uni-app 客户端按 A 版 Model（`Model/Finance_Receive.cs` 等）的字段名编写，
    /// B 版实体重命名过部分字段（订单/合同编号 `Serialnumber` → `sn`），曾在 API 边界补别名修复。
    /// 收款实体 `Finance_Receive` 与 A 版 Model 同名同字段，是 APP 能直接工作的前提。
    ///
    /// 这些测试用反射断言「APP 依赖的字段确实存在」，防止后续重构再次悄悄改名导致移动端白屏。
    /// </summary>
    public class MobileApiContractTests
    {
        [Fact]
        public void Sale_Order_Has_Sn_For_Serialnumber_Alias()
        {
            // APIController.WithSerialnumber 在 API 边界把 Serialnumber 别名指向 sn
            PropertyInfo? prop = typeof(Sale_order).GetProperty("sn");
            Assert.NotNull(prop);
            Assert.Equal(typeof(string), prop!.PropertyType);
        }

        [Fact]
        public void Sale_Contract_Has_Sn_For_Serialnumber_Alias()
        {
            PropertyInfo? prop = typeof(Sale_contract).GetProperty("sn");
            Assert.NotNull(prop);
            Assert.Equal(typeof(string), prop!.PropertyType);
        }

        [Theory]
        [InlineData("Receive_num", typeof(string))]
        [InlineData("Receive_amount", typeof(decimal?))]
        [InlineData("Receive_date", typeof(DateTime?))]
        [InlineData("Pay_type_id", typeof(string))]
        [InlineData("Payee_id", typeof(string))]
        [InlineData("order_id", typeof(string))]
        [InlineData("Remarks", typeof(string))]
        public void Finance_Receive_Field_Names_Match_Legacy_App_Contract(string name, Type type)
        {
            // APP（pages/Receive/*）直接绑定这些字段名，服务端零映射
            PropertyInfo? prop = typeof(Finance_Receive).GetProperty(name);
            Assert.NotNull(prop);
            Assert.Equal(type, prop!.PropertyType);
        }

        [Theory]
        [InlineData("PayType", typeof(Sys_Param))]   // 支付方式（params_type = pay_type）
        [InlineData("Payee", typeof(hr_employee))]   // 收款人
        [InlineData("Order", typeof(Sale_order))]    // 订单（含 customer.cus_name / sn / total_amount）
        public void Finance_Receive_Navigations_Match_Legacy_App_Contract(string name, Type type)
        {
            PropertyInfo? prop = typeof(Finance_Receive).GetProperty(name);
            Assert.NotNull(prop);
            Assert.Equal(type, prop!.PropertyType);
        }
    }
}
