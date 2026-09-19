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
    public class Finance_ReceiveRepository : BaseRepository<Finance_Receive>, IFinance_ReceiveRepository
    {
        public Finance_ReceiveRepository(IFreeSql freesql)
        {
            _fsql = freesql;
        }

        /// <summary>
        /// 更新
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public async new Task<int> UpdateAsync(Finance_Receive model)
        {
            var result = await _fsql.Update<Finance_Receive>()
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
        /// <param name="expOrder"></param>
        /// <returns></returns>
        public async new Task<XHDData<Finance_Receive>> GridAsync(Expression<Func<Finance_Receive, bool>> expWhere, int Page, int Limit)
        {
            var data = await _fsql.Select<Finance_Receive>()
                .LeftJoin(a => a.PayType.id == a.Pay_type_id && a.PayType.params_type == "pay_type")
                .LeftJoin(a => a.Order.id == a.order_id)
                .LeftJoin(a => a.Order.customer.id == a.Order.customer_id)
                .LeftJoin(a => a.Payee.id == a.Payee_id)
                .LeftJoin(a => a.creater.id == a.create_id)
                .Where(expWhere)
                //.OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync(true);

            //构建返回数据
            XHDData<Finance_Receive> result = new XHDData<Finance_Receive>()
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
        /// <param name="expOrder"></param>
        /// <returns></returns>
        public async new Task<XHDData<Finance_Receive>> GridAsync(Expression<Func<Finance_Receive, bool>> expWhere, int Page, int Limit, string orderby)
        {
            if (string.IsNullOrWhiteSpace(orderby))
            {
                return await GridAsync(expWhere, Page, Limit);
            }

            var data = await _fsql.Select<Finance_Receive>()
                .LeftJoin(a => a.PayType.id == a.Pay_type_id && a.PayType.params_type == "pay_type")
                .LeftJoin(a => a.Order.id == a.order_id)
                .LeftJoin(a => a.Order.customer.id == a.Order.customer_id)
                .LeftJoin(a => a.Payee.id == a.Payee_id)
                .LeftJoin(a => a.creater.id == a.create_id)
                .Where(expWhere)
                .OrderBy(orderby)
                .Page(Page, Limit)
                .Count(out var total)
                .ToListAsync(true);

            //构建返回数据
            XHDData<Finance_Receive> result = new XHDData<Finance_Receive>()
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
        public async new Task<List<Finance_Receive>> GridAsync(Expression<Func<Finance_Receive, bool>> expWhere)
        {
            var data = await _fsql.Select<Finance_Receive>()
                .LeftJoin(a => a.PayType.id == a.Pay_type_id && a.PayType.params_type == "pay_type")
                .LeftJoin(a => a.Order.id == a.order_id)
                .LeftJoin(a => a.Order.customer.id == a.Order.customer_id)
                .LeftJoin(a => a.Payee.id == a.Payee_id)
                .LeftJoin(a => a.creater.id == a.create_id)
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
        public async new Task<List<Finance_Receive>> GridAsync(Expression<Func<Finance_Receive, bool>> expWhere, string OrderBy)
        {
            if (string.IsNullOrWhiteSpace(OrderBy))
            {
                return await GridAsync(expWhere);
            }

            var data = await _fsql.Select<Finance_Receive>()
                .LeftJoin(a => a.PayType.id == a.Pay_type_id && a.PayType.params_type == "pay_type")
                .LeftJoin(a => a.Order.id == a.order_id)
                .LeftJoin(a => a.Order.customer.id == a.Order.customer_id)
                .LeftJoin(a => a.Payee.id == a.Payee_id)
                .LeftJoin(a => a.creater.id == a.create_id)
                .Where(expWhere)
                .OrderBy(OrderBy)
                .ToListAsync(true);

            return data;
        }

        public async Task<JArray> ReportYear(Expression<Func<Finance_Receive, bool>> expWhere)
        {
            var data = await _fsql.Select<Finance_Receive>()
                .Where(expWhere)
                .GroupBy(a => new { xmonth = a.Receive_date.Value.ToString("MM") })
                .ToListAsync(a => new { a.Key.xmonth, count = a.Count() });

            //return data.Select(a => (dynamic)a).ToList();
            JArray arr = new JArray();
            foreach (var item in data)
            {
                JObject obj = new JObject();
                obj.Add("xmonth", item.xmonth);
                obj.Add("count", item.count);

                arr.Add(obj);
            }

            return arr;
        }

        public async Task<JArray> ReportYearSum(Expression<Func<Finance_Receive, bool>> expWhere)
        {
            var data = await _fsql.Select<Finance_Receive>()
                .Where(expWhere)
                .GroupBy(a => new { xmonth = a.Receive_date.Value.ToString("MM") })
                .ToListAsync(a => new { a.Key.xmonth, count = a.Sum(a.Value.Receive_amount) });

            //return data.Select(a => (dynamic)a).ToList();
            JArray arr = new JArray();
            foreach (var item in data)
            {
                JObject obj = new JObject();
                obj.Add("xmonth", item.xmonth);
                obj.Add("count", item.count);

                arr.Add(obj);
            }

            return arr;
        }

        public async Task<JArray> ReportPayType(Expression<Func<Finance_Receive, bool>> expWhere)
        {
            var data = await _fsql.Select<Finance_Receive>()
                .LeftJoin(a => a.PayType.id == a.Pay_type_id && a.PayType.params_type == "pay_type")
                .Where(expWhere)
                .GroupBy(a => new { xmonth = a.PayType.params_name })
                .ToListAsync(a => new { a.Key.xmonth, count = a.Count() });

            //return data.Select(a => (dynamic)a).ToList();
            JArray arr = new JArray();
            foreach (var item in data)
            {
                JObject obj = new JObject();

                if (string.IsNullOrWhiteSpace(item.xmonth))
                {
                    obj.Add("xmonth", "未分类");
                }
                else
                {
                    obj.Add("xmonth", item.xmonth);
                }


                obj.Add("count", item.count);

                arr.Add(obj);
            }

            return arr;
        }

        /// <summary>
        /// 收款成功后三向联动更新（Wave 3b #11）
        /// 事务内依次完成四步：
        ///   ① SUM(Finance_Receive.Receive_amount) 按 receivableId 汇总
        ///   ② 更新 Finance_Receivable.received_amount + arrears_amount
        ///   ③ SUM(Finance_Receivable.received_amount) 按 orderId 汇总
        ///   ④ 更新 Sale_order.receive_money + arrears_money
        /// 任何一步失败或应收单不存在，整个事务回滚。
        /// </summary>
        /// <param name="receivableId">应收单 ID</param>
        /// <param name="orderId">订单 ID</param>
        /// <returns>是否全部更新成功</returns>
        public async Task<bool> UpdateReceiveAsync(string receivableId, string orderId)
        {
            if (string.IsNullOrWhiteSpace(receivableId) || string.IsNullOrWhiteSpace(orderId))
            {
                return false;
            }

            using (var conn = _fsql.Ado.MasterPool.Get())
            using (var tx = conn.Value.BeginTransaction())
            {
                try
                {
                    // ① 计算该应收单下所有已收金额
                    decimal sumReceive = await _fsql.Select<Finance_Receive>()
                        .WithTransaction(tx)
                        .Where(r => r.Receivable_id == receivableId)
                        .SumAsync(r => r.Receive_amount ?? 0);

                    // ② 更新 Finance_Receivable.received_amount + arrears_amount
                    int r1 = await _fsql.Update<Finance_Receivable>()
                        .WithTransaction(tx)
                        .Set(a => a.received_amount == sumReceive)
                        .Set(a => a.arrears_amount == (a.receivable_amount ?? 0m) - sumReceive)
                        .Where(a => a.id == receivableId)
                        .ExecuteAffrowsAsync();

                    if (r1 == 0)
                    {
                        // 应收单不存在，回滚事务
                        return false;
                    }

                    // ③ 计算该订单下所有应收单已收金额之和
                    decimal orderSum = await _fsql.Select<Finance_Receivable>()
                        .WithTransaction(tx)
                        .Where(a => a.order_id == orderId)
                        .SumAsync(a => a.received_amount ?? 0);

                    // ④ 更新 Sale_order.receive_money + arrears_money
                    int r2 = await _fsql.Update<Sale_order>()
                        .WithTransaction(tx)
                        .Set(a => a.receive_money == orderSum)
                        .Set(a => a.arrears_money == (a.Order_amount ?? 0m) - orderSum)
                        .Where(a => a.id == orderId)
                        .ExecuteAffrowsAsync();

                    if (r2 > 0)
                    {
                        tx.Commit();
                        return true;
                    }
                    return false;
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }
    }
}
