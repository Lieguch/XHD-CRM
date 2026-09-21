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
    public class Finance_ReceivableController : Controller
    {
        private readonly ILogger<Finance_ReceivableController> _logger;
        private readonly IFinance_ReceivableService _service;
        private readonly ISys_logService _LogService;
        private readonly IDBAuthService _dBAuthService;
        private readonly SysLogExt<Finance_Receivable> logext = new SysLogExt<Finance_Receivable>();

        public Finance_ReceivableController(
            ILogger<Finance_ReceivableController> logger,
            IFinance_ReceivableService service,
            ISys_logService LogService,
            IDBAuthService dBAuthService)
        {
            _service = service;
            _logger = logger;
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

        /// <summary>
        /// 应收单分页列表
        /// </summary>
        /// <param name="model">分页参数</param>
        /// <returns>JSON字符串</returns>
        public async Task<string> Grid(PageView<Finance_Receivable> model)
        {
            Expression<Func<Finance_Receivable, bool>> exp = a => a.isDelete == 0;

            // 应收单号模糊查询
            if (!string.IsNullOrWhiteSpace(Request.Query["receivable_no"]))
            {
                exp = exp.And(a => a.receivable_no.Contains(Request.Query["receivable_no"]));
            }

            // 订单ID精确查询
            if (!string.IsNullOrWhiteSpace(Request.Query["order_id"]))
            {
                exp = exp.And(a => a.order_id == Request.Query["order_id"]);
            }

            // 客户名称模糊查询（通过订单关联）
            if (!string.IsNullOrWhiteSpace(Request.Query["cus_name"]))
            {
                exp = exp.And(a => a.Order.customer.cus_name.Contains(Request.Query["cus_name"]));
            }

            // 业务员ID查询
            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                exp = exp.And(a => a.create_id == Request.Query["emp_id"]);
            }

            // 应收时间范围查询
            if (PageValidate.IsDateTime(Request.Query["date1"]))
            {
                exp = exp.And(a => a.receivable_time >= DateTime.Parse(Request.Query["date1"]));
            }

            if (PageValidate.IsDateTime(Request.Query["date2"]))
            {
                exp = exp.And(a => a.receivable_time <= DateTime.Parse(Request.Query["date2"]));
            }

            // 最小应收金额查询
            if (decimal.TryParse(Request.Query["min_amount"], out decimal minAmount))
            {
                exp = exp.And(a => a.receivable_amount >= minAmount);
            }

            // 权限过滤：非全员可见角色只能查看自己创建的数据
            var roledata = await _dBAuthService.GetDataAuth(User.FindFirst(ClaimTypes.Sid).Value);

            if (roledata.authtype != 4)
            {
                exp = exp.And(a => roledata.empList.Contains(a.create_id));
            }

            var result = await _service.GridAsync(exp, model.Page, model.Limit, "a.create_time desc");

            return result.ToString();
        }

        /// <summary>
        /// 新增或编辑应收单
        /// </summary>
        /// <param name="model">应收单实体</param>
        /// <returns>操作结果</returns>
        public async Task<string> Save(Finance_Receivable model)
        {
            var result = 0;

            if (string.IsNullOrWhiteSpace(model.id))
            {
                // 新增
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.create_id = User.FindFirst(ClaimTypes.Sid).Value;
                model.create_time = DateTime.Now;

                var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "Finance_Receivable|add");

                if (authbtn)
                {
                    result = await _service.AddAsync(model);

                    // 审计日志：新增应收单
                    Sys_log logmodels = new Sys_log();
                    logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                    logmodels.EventType = "[应收单]新增";
                    logmodels.EventID = model.id;
                    logmodels.EventTitle = model.receivable_no;
                    logmodels.UserID = User.FindFirst(ClaimTypes.Sid).Value;
                    logmodels.UserName = User.FindFirst(ClaimTypes.Name).Value;
                    logmodels.IPStreet = HttpContext.Connection.RemoteIpAddress.ToString();
                    logmodels.EventDate = DateTime.Now;
                    logmodels.Log_Content = $"新增应收单成功，单号：{model.receivable_no}，金额：{model.receivable_amount}";

                    await _LogService.UpdateLog(logmodels);
                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }
            else
            {
                // 编辑
                var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "Finance_Receivable|edit");

                if (authbtn)
                {
                    Expression<Func<Finance_Receivable, bool>> exp = a => a.id == model.id && a.isDelete == 0;
                    var checknulldata = await _service.GridAsync(exp, 1, 1);

                    if (checknulldata.count == 0)
                    {
                        return XHDResult.Error("找不到数据！").ToString();
                    }

                    result = await _service.UpdateAsync(model);

                    // 日志记录
                    var content = logext.LogContent(checknulldata.data[0], model);

                    if (content.Length > 0)
                    {
                        Sys_log logmodels = new Sys_log();

                        logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                        logmodels.EventType = "[应收单]修改";
                        logmodels.EventID = model.id;
                        logmodels.EventTitle = model.receivable_no;
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

            // 重算订单收款状态
            if (!string.IsNullOrWhiteSpace(model.order_id))
            {
                await _service.UpdateReceiveAsync(model.order_id);
            }

            return XHDResult.Success().ToString();
        }

        /// <summary>
        /// 删除应收单
        /// </summary>
        /// <param name="id">应收单ID</param>
        /// <returns>操作结果</returns>
        public async Task<string> Delete(string id)
        {
            // 先查询信息
            Expression<Func<Finance_Receivable, bool>> exp = a => a.id == id && a.isDelete == 0;
            var receiveInfo = await _service.GridAsync(exp);

            if (receiveInfo.count == 0)
            {
                return XHDResult.Error("找不到数据！").ToString();
            }

            var result = 0;

            var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "Finance_Receivable|del");

            if (authbtn)
            {
                // 软删除：设置 isDelete=1, Delete_time, Delete_id
                result = await _service.UpdateAsync(
                    a => new Finance_Receivable
                    {
                        isDelete = 1,
                        Delete_time = DateTime.Now,
                        Delete_id = User.FindFirst(ClaimTypes.Sid).Value
                    },
                    a => a.id == id);

                // 日志记录
                logext.getEntityText(receiveInfo.data[0]);

                Sys_log logmodels = new Sys_log();

                logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                logmodels.EventType = "[应收单]删除";
                logmodels.EventID = id;
                logmodels.EventTitle = receiveInfo.data[0].receivable_no;
                logmodels.UserID = User.FindFirst(ClaimTypes.Sid).Value;
                logmodels.UserName = User.FindFirst(ClaimTypes.Name).Value;
                logmodels.IPStreet = HttpContext.Connection.RemoteIpAddress.ToString();
                logmodels.EventDate = DateTime.Now;

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

            // 重算订单收款状态
            if (!string.IsNullOrWhiteSpace(receiveInfo.data[0].order_id))
            {
                await _service.UpdateReceiveAsync(receiveInfo.data[0].order_id);
            }

            return XHDResult.Success().ToString();
        }

        /// <summary>
        /// 查询应收状态汇总
        /// 按状态分类统计（已收齐/部分收款/未收款）
        /// </summary>
        /// <param name="orderId">订单ID（可选，为空则查询全部）</param>
        /// <returns>状态汇总JSON</returns>
        public async Task<string> Status(string orderId)
        {
            Expression<Func<Finance_Receivable, bool>> exp = a => a.isDelete == 0;

            if (!string.IsNullOrWhiteSpace(orderId))
            {
                exp = exp.And(a => a.order_id == orderId);
            }

            var data = await _service.GridAsync(exp);

            // 按状态分类统计
            int fullyReceived = 0;
            int partiallyReceived = 0;
            int notReceived = 0;
            decimal totalReceivable = 0m;
            decimal totalReceived = 0m;
            decimal totalArrears = 0m;

            foreach (var item in data.data)
            {
                decimal receivable = item.receivable_amount ?? 0m;
                decimal received = item.received_amount ?? 0m;
                decimal arrears = item.arrears_amount ?? 0m;

                totalReceivable += receivable;
                totalReceived += received;
                totalArrears += arrears;

                if (receivable > 0 && received >= receivable)
                {
                    fullyReceived++;
                }
                else if (received > 0)
                {
                    partiallyReceived++;
                }
                else
                {
                    notReceived++;
                }
            }

            JArray arr = new JArray();

            JObject obj = new JObject();
            obj["status"] = "已收齐";
            obj["count"] = fullyReceived;
            arr.Add(obj);

            obj = new JObject();
            obj["status"] = "部分收款";
            obj["count"] = partiallyReceived;
            arr.Add(obj);

            obj = new JObject();
            obj["status"] = "未收款";
            obj["count"] = notReceived;
            arr.Add(obj);

            obj = new JObject();
            obj["totalReceivable"] = totalReceivable;
            obj["totalReceived"] = totalReceived;
            obj["totalArrears"] = totalArrears;
            arr.Add(obj);

            return arr.ToString();
        }

        /// <summary>
        /// Sprint 8 #137a m_receivable.list：移动端应收列表（分页 + 可选客户过滤）。
        /// 对应 A 侧 Server/m_receivable.list（Server/m_receivable.cs:40）。
        /// P34：A 侧源码有 SQL 拼接 bug（CRM_Customer.id 应为 join 客户表），
        /// B 侧沿用**语义修正版**：按 Finance_Receivable.Order.customer.id 过滤。
        /// </summary>
        [HttpGet("Mobile/list")]
        public async Task<string> MobileList(
            int pageindex = 1,
            int pagesize = 10,
            string sortname = null,
            string sortorder = null,
            string customer_id = null)
        {
            if (pageindex < 1) pageindex = 1;
            if (pagesize < 1 || pagesize > 100) pagesize = 10;

            Expression<Func<Finance_Receivable, bool>> exp = a => a.isDelete == 0;

            if (!string.IsNullOrWhiteSpace(customer_id))
            {
                exp = exp.And(a => a.Order.customer.id == customer_id);
            }

            string orderby;
            if (!string.IsNullOrWhiteSpace(sortname))
            {
                var order = string.IsNullOrWhiteSpace(sortorder) ? "desc" : sortorder;
                orderby = $"a.{sortname} {order}";
            }
            else
            {
                orderby = "a.create_time desc";
            }

            var result = await _service.GridAsync(exp, pageindex, pagesize, orderby);
            return result.ToString();
        }

        /// <summary>
        /// Sprint 8 #137b m_receivable.form：移动端应收表单（单条查询）。
        /// 对应 A 侧 Server/m_receivable.form（Server/m_receivable.cs:72）。
        /// id 校验走 PageValidate.checkID（GUID 格式）；无数据返回空对象。
        /// </summary>
        [HttpGet("Mobile/form")]
        public async Task<string> MobileForm(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !PageValidate.checkID(id))
            {
                return XHDResult.Success(new JObject()).ToString();
            }

            Expression<Func<Finance_Receivable, bool>> exp = a => a.id == id && a.isDelete == 0;
            var data = await _service.GridAsync(exp);

            if (data.count == 0 || data.data == null || data.data.Count == 0)
            {
                return XHDResult.Success(new JObject()).ToString();
            }

            var arr = new JArray();
            foreach (var item in data.data)
            {
                arr.Add(JObject.FromObject(item));
            }

            var obj = new JObject
            {
                { "data", arr },
                { "count", 1 }
            };

            return XHDResult.Success(obj).ToString();
        }

        /// <summary>
        /// 触发收款重算
        /// 根据订单下所有应收单的已收金额，重算订单收款状态
        /// </summary>
        /// <param name="orderId">订单ID</param>
        /// <returns>操作结果</returns>
        public async Task<string> Receive(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                return XHDResult.Error("请指定订单ID").ToString();
            }

            var result = await _service.UpdateReceiveAsync(orderId);

            if (result)
            {
                // 审计日志：收款重算（写操作，同步更新订单/应收单的已收金额）
                Sys_log logmodels = new Sys_log();
                logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                logmodels.EventType = "[应收单]收款重算";
                logmodels.EventID = orderId;
                logmodels.EventTitle = $"订单 {orderId} 收款状态重算";
                logmodels.UserID = User.FindFirst(ClaimTypes.Sid).Value;
                logmodels.UserName = User.FindFirst(ClaimTypes.Name).Value;
                logmodels.IPStreet = HttpContext.Connection.RemoteIpAddress.ToString();
                logmodels.EventDate = DateTime.Now;
                logmodels.Log_Content = $"对订单 {orderId} 执行收款状态重算，结果：成功";

                await _LogService.UpdateLog(logmodels);

                return XHDResult.Success("订单收款状态已更新").ToString();
            }
            else
            {
                return XHDResult.Error("更新失败，订单不存在或无关联应收单").ToString();
            }
        }
    }
}
