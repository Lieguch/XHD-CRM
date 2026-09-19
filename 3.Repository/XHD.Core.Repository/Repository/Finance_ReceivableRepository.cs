using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using FreeSql;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;
using Newtonsoft.Json.Linq;

namespace XHD.Core.Repository
{
    public class Finance_ReceivableRepository : BaseRepository<Finance_Receivable>, IFinance_ReceivableRepository
    {
        public Finance_ReceivableRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 更新
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async new Task<int> UpdateAsync(Finance_Receivable model)
        {
            var result = await _fsql.Update<Finance_Receivable>()
                .SetSource(model)
                .IgnoreColumns(
                    a => new
                    {
                        a.create_id,
                        a.create_time
                    })
                .ExecuteAffrowsAsync();

            return result;
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="Page"></param>
        /// <param name="Limit"></param>
        /// <returns></returns>
        public async new Task<XHDData<Finance_Receivable>> GridAsync(Expression<Func<Finance_Receivable, bool>> expWhere, int Page, int Limit)
        {
            var data = await _fsql.Select<Finance_Receivable>()
                .LeftJoin(a => a.Order.id == a.order_id)
                .LeftJoin(a => a.Order.customer.id == a.Order.customer_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                .Where(expWhere)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync(true);

            XHDData<Finance_Receivable> result = new XHDData<Finance_Receivable>()
            {
                data = data,
                count = total
            };

            return result;
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="Page"></param>
        /// <param name="Limit"></param>
        /// <param name="orderby"></param>
        /// <returns></returns>
        public async new Task<XHDData<Finance_Receivable>> GridAsync(Expression<Func<Finance_Receivable, bool>> expWhere, int Page, int Limit, string orderby)
        {
            if (string.IsNullOrWhiteSpace(orderby))
            {
                return await GridAsync(expWhere, Page, Limit);
            }

            var data = await _fsql.Select<Finance_Receivable>()
                .LeftJoin(a => a.Order.id == a.order_id)
                .LeftJoin(a => a.Order.customer.id == a.Order.customer_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                .Where(expWhere)
                .OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync(true);

            XHDData<Finance_Receivable> result = new XHDData<Finance_Receivable>()
            {
                data = data,
                count = total
            };

            return result;
        }

        /// <summary>
        /// 普通条件查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <returns></returns>
        public async new Task<List<Finance_Receivable>> GridAsync(Expression<Func<Finance_Receivable, bool>> expWhere)
        {
            var data = await _fsql.Select<Finance_Receivable>()
                .LeftJoin(a => a.Order.id == a.order_id)
                .LeftJoin(a => a.Order.customer.id == a.Order.customer_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                .Where(expWhere)
                .ToListAsync(true);

            return data;
        }

        /// <summary>
        /// 普通条件查询带排序
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="OrderBy"></param>
        /// <returns></returns>
        public async new Task<List<Finance_Receivable>> GridAsync(Expression<Func<Finance_Receivable, bool>> expWhere, string OrderBy)
        {
            if (string.IsNullOrWhiteSpace(OrderBy))
            {
                return await GridAsync(expWhere);
            }

            var data = await _fsql.Select<Finance_Receivable>()
                .LeftJoin(a => a.Order.id == a.order_id)
                .LeftJoin(a => a.Order.customer.id == a.Order.customer_id)
                .LeftJoin(a => a.Creater.id == a.create_id)
                .Where(expWhere)
                .OrderBy(OrderBy)
                .ToListAsync(true);

            return data;
        }

        /// <summary>
        /// 按订单ID重算收款状态
        /// 更新 Sale_order.receive_money 和 arrears_money
        /// 基于该订单下所有未删除的 Finance_Receivable 的 received_amount 之和
        /// </summary>
        /// <param name="orderId">订单ID</param>
        /// <returns>是否更新成功</returns>
        public async Task<bool> UpdateReceiveAsync(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                return false;
            }

            var order = await _fsql.Select<Sale_order>()
                .Where(a => a.id == orderId)
                .FirstAsync();

            if (order == null)
            {
                return false;
            }

            // 查询该订单下所有未删除的应收单
            var receivables = await _fsql.Select<Finance_Receivable>()
                .Where(a => a.order_id == orderId && a.isDelete == 0)
                .ToListAsync();

            // 计算已收金额总和
            decimal sum = 0m;
            foreach (var r in receivables)
            {
                if (r.received_amount.HasValue)
                {
                    sum += r.received_amount.Value;
                }
            }

            decimal orderAmount = order.Order_amount ?? 0m;

            // 更新订单收款金额和未收金额
            int rows = await _fsql.Update<Sale_order>()
                .Set(a => a.receive_money == sum)
                .Set(a => a.arrears_money == orderAmount - sum)
                .Where(a => a.id == orderId)
                .ExecuteAffrowsAsync();

            return rows > 0;
        }
    }
}
