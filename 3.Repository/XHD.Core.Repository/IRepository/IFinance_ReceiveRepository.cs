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
    public interface IFinance_ReceiveRepository: IXHDBaseRepository<Finance_Receive>
    {
        Task<JArray> ReportYear(Expression<Func<Finance_Receive, bool>> expWhere);

        Task<JArray> ReportPayType(Expression<Func<Finance_Receive, bool>> expWhere);

        Task<JArray> ReportYearSum(Expression<Func<Finance_Receive, bool>> expWhere);

        /// <summary>
        /// 收款成功后三向联动更新（Wave 3b #11）：
        /// 事务内依次更新 Finance_Receivable.received_amount、arrears_amount，
        /// 以及关联 Sale_order.receive_money、arrears_money
        /// </summary>
        /// <param name="receivableId">应收单 ID</param>
        /// <param name="orderId">订单 ID</param>
        /// <returns>是否全部更新成功</returns>
        Task<bool> UpdateReceiveAsync(string receivableId, string orderId);
    }
}
