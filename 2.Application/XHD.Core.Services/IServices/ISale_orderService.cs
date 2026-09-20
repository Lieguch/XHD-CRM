using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq.Expressions;

namespace XHD.Core.IServices
{
    public interface ISale_orderService:IBaseService<Sale_order>
    {
        Task<int> UpdateArrearsMoney(string id);

        void UpdateOrderInvoice(string order_id);

        void UpdateOrderReceive(string order_id);

        Task<JArray> ReportYear(Expression<Func<Sale_order, bool>> expWhere);

        Task<JArray> ReportStatus(Expression<Func<Sale_order, bool>> expWhere);

        Task<JArray> ReportPayType(Expression<Func<Sale_order, bool>> expWhere);

        Task<JArray> ReportYearSum(Expression<Func<Sale_order, bool>> expWhere);

        /// <summary>
        /// Sprint 4 #07：员工双月订单对比。
        /// </summary>
        Task<JArray> ComparedEmpCusOrderAsync(int year1, int month1, int year2, int month2, List<string> empIds);

        /// <summary>
        /// Sprint 4 #08：员工月度订单矩阵（跨月区间）。
        /// </summary>
        Task<JArray> ReportMonthEmpOrderAsync(DateTime start, DateTime end, List<string> empIds);

        /// <summary>
        /// Sprint 4 #09：员工年度订单矩阵。
        /// </summary>
        Task<JArray> ReportEmpOrderAsync(int year, List<string> empIds);
    }
}
