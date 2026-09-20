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
using XHD.Core.View.Configs;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class SaleOrderController : Controller
    {
        private readonly ILogger<SaleOrderController> _logger;
        private readonly ISale_orderService _service;
        private readonly ISale_order_detailsService _detailservice;
        private readonly IFinance_InvoiceService _InvoiceService;
        private readonly IFinance_ReceiveService _ReceiveService;

        private readonly ISys_logService _LogService;
        private readonly IDBAuthService _dBAuthService;
        private readonly SysLogExt<Sale_order> logext = new SysLogExt<Sale_order>();  //日志

        public SaleOrderController(
            ILogger<SaleOrderController> logger, 
            ISale_orderService service, 
            ISale_order_detailsService detailservice,
            IFinance_InvoiceService InvoiceService,
            IFinance_ReceiveService ReceiveService, ISys_logService LogService, IDBAuthService dBAuthService)
        {
            _service = service;
            _logger = logger;
            _detailservice = detailservice;
            _InvoiceService = InvoiceService;
            _ReceiveService = ReceiveService;


            _LogService = LogService;
            _dBAuthService = dBAuthService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Add()
        {
            return View();
        }

        public async Task<string> Grid(PageView<Sale_order> model)
        {
            Expression<Func<Sale_order, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(Request.Query["cus_name"]))
            {
                exp = exp.And(a => a.customer.cus_name.Contains(Request.Query["cus_name"]));
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["customer_id"]))
            {
                exp = exp.And(a => a.customer_id == Request.Query["customer_id"]);
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                exp = exp.And(a => a.emp_id == Request.Query["emp_id"]);
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["Order_status_id"]))
            {
                exp = exp.And(a => a.Order_status_id == Request.Query["Order_status_id"]);
            }

            if (PageValidate.IsDateTime(Request.Query["date1"]))
            {
                exp = exp.And(a => a.Order_date >= DateTime.Parse(Request.Query["date1"]));
            }

            if (PageValidate.IsDateTime(Request.Query["date2"]))
            {
                exp = exp.And(a => a.Order_date <= DateTime.Parse(Request.Query["date2"]));
            }

            //权限
            var roledata = await _dBAuthService.GetDataAuth(User.FindFirst(ClaimTypes.Sid).Value);

            if (roledata.authtype != 4)
            {
                exp = exp.And(a => roledata.empList.Contains(a.customer.emp_id));
            }

            var result = await _service.GridAsync(exp, model.Page, model.Limit, "a.create_time desc");

            return result.ToString();
        }

        public async Task<string> Save(Sale_order model)
        {
            var result = 0;

            if (string.IsNullOrWhiteSpace(model.id))
            {
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                var ids = model.id.Split("-");
                var sn = $"OR-{DateTime.Now.ToString("yyyyMMddHHmmss")}-{ids[2]}";
                model.sn = sn;
                //model.sn = "DD-" + DateTime.Now.ToString("yyyy-MM-dd-") + DateTime.Now.GetHashCode().ToString().Replace("-", "");
                model.create_id = User.FindFirst(ClaimTypes.Sid).Value;
                model.create_time = DateTime.Now;

                //权限
                var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "Sale_Order|add");

                if (authbtn)
                {
                    result = await _service.AddAsync(model);
                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }
            else
            {
                //权限
                var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid)?.Value, "Sale_Order|edit");

                if (authbtn)
                {
                    //日志
                    Expression<Func<Sale_order, bool>> exp = a => a.id == model.id;
                    var checknulldata = await _service.GridAsync(exp, 1, 1);

                    if (checknulldata.count == 0)
                    {
                        return XHDResult.Error("找不到数据！").ToString();
                    }

                    result = await _service.UpdateAsync(model);
                    await _service.UpdateArrearsMoney(model.id);
                     _service.UpdateOrderInvoice(model.id);
                    //对比实体差别

                    var content = logext.LogContent(checknulldata.data[0], model);

                    if (content.Length > 0)
                    {
                        //添加修改日志
                        Sys_log logmodels = new Sys_log();

                        logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                        logmodels.EventType = "[订单]修改";
                        logmodels.EventID = model.id;
                        logmodels.EventTitle = model.sn;
                        logmodels.UserID = User.FindFirst(ClaimTypes.Sid).Value;
                        logmodels.UserName = User.FindFirst(ClaimTypes.Name).Value;
                        logmodels.IPStreet = HttpContext.Connection.RemoteIpAddress.ToString();
                        logmodels.EventDate = DateTime.Now;
                        logmodels.Log_Content = content;
                        
                        await _LogService.UpdateLog(logmodels);
                    }
                    
                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }

            

            if (result == 0)
            {
                return XHDResult.Error("操作失败，系统错误！").ToString();
            }

            //更新详情
            //先删除详情
            Expression<Func<Sale_order_details, bool>> expdetails = a => a.order_id == model.id;
            await _detailservice.DeleteAsync(expdetails);

            JArray arr = JArray.Parse(Request.Form["T_data"]);

            Sale_order_details modelsdetail = new Sale_order_details();
            modelsdetail.order_id = model.id;

            foreach (JObject item in arr)
            {
                modelsdetail.product_id=item.Value<string>("product_id");
                modelsdetail.price = item.Value<decimal>("price");
                modelsdetail.quantity = item.Value<int>("quantity");
                modelsdetail.amount = item.Value<decimal>("amount");

                await _detailservice.AddAsync(modelsdetail);
            }

            return XHDResult.Success().ToString();
        }

        public async Task<string> Delete(string id)
        {
            //判断是否有发票
            Expression<Func<Finance_Invoice, bool>> expinvoice = a => a.Order.id == id;
            var invoicelist = await _InvoiceService.GridAsync(expinvoice);

            if (invoicelist.count > 0)
            {
                return XHDResult.Error("此订单下含有发票，不能删除！").ToString();
            }


            //判断是否有收款
            Expression<Func<Finance_Receive, bool>> expreceive = a => a.Order.id == id;
            var receivelist = await _ReceiveService.GridAsync(expreceive);

            if (receivelist.count > 0)
            {
                return XHDResult.Error("此订单下含有收款，不能删除！").ToString();
            }


            var result = 0;

            //权限
            var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "Sale_Order|del");

            if (authbtn)
            {
                //判断是否有数据
                Expression<Func<Sale_order, bool>> exp = a => a.id == id;
                var checkdata = await _service.GridAsync(exp, 1, 1);

                if (checkdata.count == 0)
                {
                    return XHDResult.Error("找不到此数据！").ToString();
                }

                result = await _service.DeleteAsync(id);

                //先存储删除的实体记录，用日志形式
                logext.getEntityText(checkdata.data[0]);

                //记录日志
                Sys_log logmodels = new Sys_log();

                logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                logmodels.EventType = "[订单]删除";
                logmodels.EventID = id;
                logmodels.EventTitle = checkdata.data[0].sn;
                logmodels.UserID = User.FindFirst(ClaimTypes.Sid).Value;
                logmodels.UserName = User.FindFirst(ClaimTypes.Name).Value;
                logmodels.IPStreet = HttpContext.Connection.RemoteIpAddress.ToString();
                logmodels.EventDate = DateTime.Now;
                //logmodels.Log_Content = checkdata.data[0].follow_content;

                
                await _LogService.DeleteLog(logmodels);
            }
            else
            {
                return XHDResult.Error("无权限！").ToString();
            }

            if (result == 0)
            {
                return XHDResult.Error("删除失败！").ToString();
            }

            //删除详情项
            Expression<Func<Sale_order_details, bool>> expdetails = a => a.order_id == id;
            var detailslist = await _detailservice.GridAsync(expdetails);

            await _detailservice.DeleteAsync(expdetails);

            SysLogExt<Sale_order_details> logdetails = new SysLogExt<Sale_order_details>();  //日志
            foreach (Sale_order_details model in detailslist.data)
            {
                //先存储删除的实体记录，用日志形式
                logdetails.getEntityText(model);
            }

            return XHDResult.Success().ToString();
        }

        #region Sprint 4 Wave 1a：销售订单报表端点（#03 #07 #08 #09）

        /// <summary>
        /// Sprint 4 #03：按客户查询订单（客户详情页）。
        /// 对应 A 侧 Server.Sale_order.gridbycustomerid。
        /// </summary>
        /// <param name="customerid">客户 ID（必须为 GUID）</param>
        /// <param name="page">页码，默认 1</param>
        /// <param name="limit">每页条数，默认 30</param>
        [HttpGet("gridbycustomerid")]
        public async Task<string> GridByCustomerId(string customerid, int page = 1, int limit = 30)
        {
            if (!PageValidate.checkID(customerid))
            {
                return XHDResult.Error("客户ID无效").ToString();
            }

            Expression<Func<Sale_order, bool>> exp = a => a.customer_id == customerid;

            // 数据权限过滤：参考 CRMFollowController.Grid 模式
            var roledata = await _dBAuthService.GetDataAuth(User.FindFirst(ClaimTypes.Sid).Value);
            if (roledata.authtype != 4)
            {
                exp = exp.And(a => roledata.empList.Contains(a.customer.emp_id));
            }

            var result = await _service.GridAsync(exp, page, limit, "a.Order_date desc");
            return result.ToString();
        }

        /// <summary>
        /// Sprint 4 #07：员工双月订单对比。
        /// 对应 A 侧 Server.Sale_order.Compared_empcusorder。
        /// idlist 语义变更：A 侧为岗位 ID，B 侧为员工 ID 直传（规避 hr_post 依赖）。
        /// </summary>
        [HttpGet("Compared_empcusorder")]
        public async Task<string> ComparedEmpCusOrder(
            [FromQuery] string idlist,
            int year1, int month1, int year2, int month2)
        {
            if (month1 < 1 || month1 > 12 || month2 < 1 || month2 > 12)
            {
                return XHDResult.Error("月份必须在 1-12 之间").ToString();
            }

            List<string> empIds = ParseEmpIds(idlist);

            var arr = await _service.ComparedEmpCusOrderAsync(year1, month1, year2, month2, empIds);
            return XHDResult.Success(arr).ToString();
        }

        /// <summary>
        /// Sprint 4 #08：员工月度订单矩阵（跨月区间）。
        /// 对应 A 侧 Server.Sale_order.emp_month_cusorder。
        /// </summary>
        [HttpGet("emp_month_cusorder")]
        public async Task<string> EmpMonthCusOrder(
            [FromQuery] string idlist,
            [FromQuery] string sstart,
            [FromQuery] string sdend)
        {
            DateTime start = PageValidate.IsDateTime(sstart) ? DateTime.Parse(sstart) : DateTime.MinValue;
            DateTime end = PageValidate.IsDateTime(sdend)
                ? DateTime.Parse(sdend).AddDays(1).AddTicks(-1)
                : DateTime.MaxValue;

            if (start > end)
            {
                return XHDResult.Error("开始时间不能晚于结束时间").ToString();
            }

            List<string> empIds = ParseEmpIds(idlist);

            var arr = await _service.ReportMonthEmpOrderAsync(start, end, empIds);
            return XHDResult.Success(arr).ToString();
        }

        /// <summary>
        /// Sprint 4 #09：员工年度订单矩阵。
        /// 对应 A 侧 Server.Sale_order.emp_cusorder。
        /// </summary>
        [HttpGet("emp_cusorder")]
        public async Task<string> EmpCusOrder(
            [FromQuery] string idlist,
            [FromQuery] int syear)
        {
            if (syear < 2000 || syear > 2100)
            {
                return XHDResult.Error("年份无效").ToString();
            }

            List<string> empIds = ParseEmpIds(idlist);

            var arr = await _service.ReportEmpOrderAsync(syear, empIds);
            return XHDResult.Success(arr).ToString();
        }

        /// <summary>
        /// 解析 idlist 参数为 empIds 白名单。
        /// 输入为 ';' 分隔字符串，每个 ID 必须通过 GUID 格式校验（防 SQL 注入）。
        /// null 或空表示全部员工。
        /// </summary>
        private static List<string> ParseEmpIds(string idlist)
        {
            if (string.IsNullOrWhiteSpace(idlist))
            {
                return null;
            }

            var raw = idlist.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var valid = new List<string>(raw.Length);
            foreach (var item in raw)
            {
                var trimmed = item.Trim();
                if (PageValidate.checkID(trimmed))
                {
                    valid.Add(trimmed);
                }
            }

            return valid.Count > 0 ? valid : null;
        }

        #endregion
    }
}
