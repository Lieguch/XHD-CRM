using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace XHD.Core.IRepository
{
    public interface ISale_orderRepository: IXHDBaseRepository<Sale_order>
    {
        void UpdateOrderInvoice(string order_id);

        void UpdateOrderReceive(string order_id);

        Task<JArray> ReportYear(Expression<Func<Sale_order, bool>> expWhere);

        Task<JArray> ReportStatus(Expression<Func<Sale_order, bool>> expWhere);

        Task<JArray> ReportPayType(Expression<Func<Sale_order, bool>> expWhere);

        Task<JArray> ReportYearSum(Expression<Func<Sale_order, bool>> expWhere);

        /// <summary>
        /// 订单收款汇总重算（Wave 3b #12）：
        /// 按 order_id 汇总 Finance_Receivable.received_amount 之和，
        /// 更新 Sale_order.receive_money 和 arrears_money
        /// </summary>
        /// <param name="orderId">订单 ID</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdateReceiveAsync(string orderId);

        /// <summary>
        /// Sprint 4 #07：员工双月订单对比（返回员工名 × dt1/dt2）。
        /// empIds 为 null 表示统计全部员工。
        /// </summary>
        Task<JArray> ComparedEmpCusOrderAsync(int year1, int month1, int year2, int month2, List<string> empIds);

        /// <summary>
        /// Sprint 4 #08：员工月度订单矩阵（跨月区间，返回 name/yy/m1..m12）。
        /// </summary>
        Task<JArray> ReportMonthEmpOrderAsync(DateTime start, DateTime end, List<string> empIds);

        /// <summary>
        /// Sprint 4 #09：员工年度订单矩阵（返回 name/yy/m1..m12）。
        /// </summary>
        Task<JArray> ReportEmpOrderAsync(int year, List<string> empIds);
    }
}
