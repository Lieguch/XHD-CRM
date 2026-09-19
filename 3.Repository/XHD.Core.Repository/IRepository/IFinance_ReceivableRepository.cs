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
    public interface IFinance_ReceivableRepository : IXHDBaseRepository<Finance_Receivable>
    {
        /// <summary>
        /// 按订单ID重算收款状态（更新 Sale_order.receive_money 和 arrears_money）
        /// </summary>
        /// <param name="orderId">订单ID</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdateReceiveAsync(string orderId);
    }
}
