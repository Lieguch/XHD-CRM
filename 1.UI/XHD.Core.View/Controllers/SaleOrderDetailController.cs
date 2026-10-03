using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using System.Security.Claims;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Common;
using XHD.Core.Models;

using System.Linq.Expressions;
using Newtonsoft.Json.Converters;
using System.Collections;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class SaleOrderDetailController : Controller
    {
        private readonly ILogger<SaleOrderDetailController> _logger;
        private readonly ISale_order_detailsService _service;
        private readonly ISale_orderService _orderService;
        private readonly IDBAuthService _dBAuthService;

        public SaleOrderDetailController(ILogger<SaleOrderDetailController> logger, ISale_order_detailsService service, ISale_orderService orderService, IDBAuthService dBAuthService)
        {
            _service = service;
            _orderService = orderService;
            _dBAuthService = dBAuthService;
            _logger = logger;
        }

        public async Task<string> Grid(PageView<Sale_order_details> model)
        {
            var sid = User.FindFirst(ClaimTypes.Sid)?.Value;
            if (string.IsNullOrWhiteSpace(sid))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var roledata = await _dBAuthService.GetDataAuth(sid);

            Expression<Func<Sale_order_details, bool>> exp;

            // 统一在分支外取 id 并判空：admin 与非 admin 分支口径一致，
            // 避免 admin 传空 id 时静默返回空结果（原来 a.order_id == "" 会空查）。
            var targetOrderId = Request.Query["id"].ToString();
            if (string.IsNullOrWhiteSpace(targetOrderId))
            {
                return XHDResult.Error("参数错误！").ToString();
            }

            if (roledata.authtype != 4)
            {
                // 非全量权限：目标主单必须属于当前用户可见范围（口径同 SaleOrderController / APIController：
                // roledata.empList.Contains(sale_order.emp_id)）。
                // 只查目标 order_id 这一条，避免把当前用户全部主单实体加载进内存。
                var targetOrder = await _orderService.GridAsync(
                    a => a.id == targetOrderId && roledata.empList.Contains(a.emp_id), 1, 1);

                if (targetOrder.count == 0)
                {
                    return XHDResult.Error("无操作权限").ToString();
                }
            }

            exp = a => a.order_id == targetOrderId;

            var result = await _service.GridAsync(exp);

            return result.ToString();
        }
    }
}
