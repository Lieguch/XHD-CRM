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
    public class CRMFollowController : Controller
    {
        private readonly ILogger<CRMFollowController> _logger;
        private readonly ICRM_followService _service;
        private readonly ISys_logService _LogService;
        private readonly IDBAuthService _dBAuthService;
        private readonly SysLogExt<CRM_follow> logext = new SysLogExt<CRM_follow>();
        private readonly ICRM_CustomerService _customerService;
        

        public CRMFollowController(
            ILogger<CRMFollowController> logger,
            ICRM_followService service, 
            ISys_logService LogService, 
            IDBAuthService dBAuthService, 
            ICRM_CustomerService customerService
            )
        {
            _service = service;
            _logger = logger;
            _LogService = LogService;
            _dBAuthService = dBAuthService;
            _customerService = customerService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Add()
        {
            return View();
        }

        public async Task<string> Grid(PageView<CRM_follow> model)
        {
            Expression<Func<CRM_follow, bool>> exp = a => 1 == 1;            

            if (!string.IsNullOrWhiteSpace(Request.Query["customer_id"]))
            {
                exp = exp.And(a => a.customer_id == Request.Query["customer_id"]);
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["cus_name"]))
            {
                exp = exp.And(a => a.customer.cus_name.Contains(Request.Query["cus_name"]));
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                exp = exp.And(a => a.employee_id == Request.Query["emp_id"]);
            }

            if (PageValidate.IsDateTime(Request.Query["date1"]))
            {
                exp = exp.And(a => a.follow_time >= DateTime.Parse(Request.Query["date1"]));
            }

            if (PageValidate.IsDateTime(Request.Query["date2"]))
            {
                exp = exp.And(a => a.follow_time <= DateTime.Parse(Request.Query["date2"]));
            }

            //权限

            var roledata = await _dBAuthService.GetDataAuth(User.FindFirst(ClaimTypes.Sid).Value);

            if (roledata.authtype != 4)
            {
                exp = exp.And(a => roledata.empList.Contains(a.customer.emp_id));
            }

            var result = await _service.GridAsync(exp, model.Page, model.Limit, "a.Follow_time desc");

            return result.ToString();
        }

        public async Task<string> Save(CRM_follow model)
        {
            var result = 0;

            if (string.IsNullOrWhiteSpace(model.id))
            {
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.employee_id = User.FindFirst(ClaimTypes.Sid).Value;
                model.follow_time = DateTime.Now;

                //权限
                var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "CRM_Follow|add");

                if (authbtn)
                {
                    result = await _service.AddAsync(model);

                    await _customerService.LastFollow(model.customer_id);

                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }
            else
            {
                //权限
                var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "CRM_Follow|edit");

                if (authbtn)
                {
                    //日志
                    Expression<Func<CRM_follow, bool>> exp = a => a.id == model.id;
                    var checknulldata = await _service.GridAsync(exp, 1, 1);

                    if (checknulldata.count == 0)
                    {
                        return XHDResult.Error("找不到数据！").ToString();
                    }

                    result = await _service.UpdateAsync(model);

                    //对比实体差别
                    
                    var content = logext.LogContent(checknulldata.data[0], model);

                    if (content.Length > 0)
                    {
                        //添加修改日志
                        Sys_log logmodels = new Sys_log();

                        logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                        logmodels.EventType = "[跟进]修改";
                        logmodels.EventID = model.id;
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

            return XHDResult.Success().ToString();
        }

        public async Task<string> Delete(string id)
        {
            var result = 0;

            //权限
            var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "CRM_Follow|del");

            if (authbtn)
            {
                //判断是否有数据
                Expression<Func<CRM_follow, bool>> exp = a => a.id == id;
                var checkdata = await _service.GridAsync(exp, 1, 1);

                if (checkdata.count == 0)
                {
                    return XHDResult.Error("找不到此数据！").ToString();
                }

                result = await _service.DeleteAsync(id);

                //日志

                //先存储删除的实体记录，用日志形式
                logext.getEntityText(checkdata.data[0]);

                //记录日志
                Sys_log logmodels = new Sys_log();

                logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                logmodels.EventType = "[跟进]删除";
                logmodels.EventID = id;
                logmodels.EventTitle = checkdata.data[0].id;
                logmodels.UserID = User.FindFirst(ClaimTypes.Sid).Value;
                logmodels.UserName = User.FindFirst(ClaimTypes.Name).Value;
                logmodels.IPStreet = HttpContext.Connection.RemoteIpAddress.ToString();
                logmodels.EventDate = DateTime.Now;
                logmodels.Log_Content = checkdata.data[0].follow_content;

                
                await _LogService.DeleteLog(logmodels);
            }
            else
            {
                return XHDResult.Error("无权限！").ToString();
            }

            //var result = await _service.Delete(id);

            if (result == 0)
            {
                return XHDResult.Error("删除失败！").ToString();
            }

            return XHDResult.Success().ToString();
        }

        #region Sprint 3 跟进报表端点（#06-#09）

        /// <summary>
        /// Sprint 3 #06：跟进双月对比（按跟进类型）。
        /// 对应 A 侧 Server.CRM_follow.Compared_follow。
        /// </summary>
        [HttpGet("ComparedFollow")]
        public async Task<string> ComparedFollow(int year1, int month1, int year2, int month2)
        {
            if (month1 < 1 || month1 > 12 || month2 < 1 || month2 > 12)
            {
                return XHDResult.Error("月份必须在 1-12 之间").ToString();
            }

            var arr = await _service.ComparedFollowAsync(year1, month1, year2, month2);
            return XHDResult.Success(arr).ToString();
        }

        /// <summary>
        /// Sprint 3 #07：员工维度双月跟进对比。
        /// 对应 A 侧 Server.CRM_follow.Compared_empcusfollow。
        /// idlist 语义变更：A 侧为岗位 ID，B 侧为员工 ID 直传（规避 hr_post 依赖）。
        /// </summary>
        [HttpGet("ComparedEmpCusFollow")]
        public async Task<string> ComparedEmpCusFollow(
            [FromQuery] string idlist,
            int year1, int month1, int year2, int month2)
        {
            if (month1 < 1 || month1 > 12 || month2 < 1 || month2 > 12)
            {
                return XHDResult.Error("月份必须在 1-12 之间").ToString();
            }

            List<string> empIds = ParseEmpIds(idlist);

            var arr = await _service.ComparedEmpCusFollowAsync(year1, month1, year2, month2, empIds);
            return XHDResult.Success(arr).ToString();
        }

        /// <summary>
        /// Sprint 3 #08：员工月度跟进矩阵（跨月区间）。
        /// 对应 A 侧 Server.CRM_follow.emp_month_cusfollow。
        /// </summary>
        [HttpGet("EmpMonthCusFollow")]
        public async Task<string> EmpMonthCusFollow(
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

            var arr = await _service.ReportMonthEmpFollowAsync(start, end, empIds);
            return XHDResult.Success(arr).ToString();
        }

        /// <summary>
        /// Sprint 3 #09：员工年度跟进矩阵。
        /// 对应 A 侧 Server.CRM_follow.emp_cusfollow。
        /// </summary>
        [HttpGet("EmpCusFollow")]
        public async Task<string> EmpCusFollow(
            [FromQuery] string idlist,
            [FromQuery] int syear)
        {
            if (syear < 2000 || syear > 2100)
            {
                return XHDResult.Error("年份无效").ToString();
            }

            List<string> empIds = ParseEmpIds(idlist);

            var arr = await _service.ReportEmpFollowAsync(syear, empIds);
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
