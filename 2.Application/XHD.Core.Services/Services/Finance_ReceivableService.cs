using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;
using System.Threading.Tasks;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

namespace XHD.Core.Services
{
    internal class Finance_ReceivableService : BaseService<Finance_Receivable>, IFinance_ReceivableService
    {
        IFinance_ReceivableRepository irepositorySelf;
        IFinance_ReceiveRepository _receiveRepository;
        ISale_orderRepository _orderRepository;

        public Finance_ReceivableService(
            IFinance_ReceivableRepository repository,
            IFinance_ReceiveRepository receiveRepository,
            ISale_orderRepository orderRepository)
        {
            _irepository = repository;
            irepositorySelf = repository;
            _receiveRepository = receiveRepository;
            _orderRepository = orderRepository;
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
            return await irepositorySelf.UpdateReceiveAsync(orderId);
        }
    }
}
