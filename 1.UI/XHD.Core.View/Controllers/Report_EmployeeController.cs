using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using System.Linq.Expressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XHD.Core.IServices;
using XHD.Core.Common;
using XHD.Core.Models;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class Report_EmployeeController : Controller
    {
        private readonly ILogger<Report_EmployeeController> _logger;
        private readonly Ihr_employeeService _empservice;
        private readonly ICRM_followService _followservice;
        private readonly IDBAuthService _dBAuthService;

        public Report_EmployeeController(
            ILogger<Report_EmployeeController> logger,
            Ihr_employeeService empservice,
            ICRM_followService followservice,
            IDBAuthService dBAuthService
            )
        {
            _logger = logger;
            _empservice = empservice;
            _followservice = followservice;
            _dBAuthService = dBAuthService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<string> Grid(PageView<hr_employee> model)
        {
            Expression<Func<hr_employee, bool>> exp = a => true;

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] empArray = Request.Query["emp_id"].Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (empArray.Length > 0)
                {
                    exp = exp.And(a => empArray.Contains(a.id));
                }
            }

            var result = await _empservice.GridAsync(exp, model.Page, model.Limit, "a.create_time desc");
            return result.ToString();
        }

        public async Task<string> ReportYear()
        {
            Expression<Func<CRM_follow, bool>> exp = a => true;

            int currentYear = DateTime.Now.Year;
            if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                int queryYear = int.Parse(Request.Query["year"]);
                exp = exp.And(a => a.follow_time.Value.Year == queryYear);
            }
            else
            {
                exp = exp.And(a => a.follow_time.Value.Year == currentYear);
            }

            string empIdStr = Request.Query["emp_id"];
            if (!string.IsNullOrWhiteSpace(empIdStr))
            {
                string[] empArray = empIdStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (empArray.Length > 0)
                {
                    exp = exp.And(a => empArray.Contains(a.employee_id));
                }
            }

            var result = await _followservice.ReportYear(exp);

            JArray arr = new JArray();
            for (int i = 1; i <= 12; i++)
            {
                JObject obj = new JObject();
                obj.Add("xmonth", i);

                var sdata = result.Where(a => a.Value<int>("xmonth") == i).FirstOrDefault();

                if (sdata == null)
                {
                    obj.Add("count", 0);
                }
                else
                {
                    obj.Add("count", sdata.Value<int>("count"));
                }

                arr.Add(obj);
            }

            return arr.ToString();
        }

        public async Task<string> ReportFollowYear()
        {
            int syear = DateTime.Now.Year;
            if (!string.IsNullOrWhiteSpace(Request.Query["syear"]))
            {
                if (int.TryParse(Request.Query["syear"], out var parsedYear) && parsedYear >= 2000 && parsedYear <= 2100)
                {
                    syear = parsedYear;
                }
            }
            else if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                if (int.TryParse(Request.Query["year"], out var parsedYear) && parsedYear >= 2000 && parsedYear <= 2100)
                {
                    syear = parsedYear;
                }
            }

            string items = string.IsNullOrWhiteSpace(Request.Query["items"]) ? "Follow_Type" : Request.Query["items"];
            var allowedItems = new[] { "Follow_Type", "Follow_aim" };
            if (!allowedItems.Contains(items))
            {
                return XHDResult.Error($"items 必须为 {string.Join("/", allowedItems)} 之一").ToString();
            }

            Expression<Func<CRM_follow, bool>> exp = a => true;

            var roledata = await _dBAuthService.GetDataAuth(User.FindFirst(ClaimTypes.Sid).Value);
            if (roledata.authtype != 4 && roledata.empList != null && roledata.empList.Count > 0)
            {
                exp = exp.And(a => roledata.empList.Contains(a.employee_id));
            }
            else if (roledata.authtype != 4)
            {
                return XHDResult.Error("权限不足！").ToString();
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] emplist = Request.Query["emp_id"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (emplist.Length > 0)
                {
                    var list = new List<string>(emplist);
                    exp = exp.And(a => list.Contains(a.employee_id));
                }
            }

            var result = await _followservice.ReportsYearAsync(items, syear, exp);

            return result.ToString();
        }

        public async Task<string> ReportMonth()
        {
            DateTime start = DateTime.Now.AddMonths(-1);
            DateTime end = DateTime.Now;

            if (PageValidate.IsDateTime(Request.Query["start"]))
            {
                start = DateTime.Parse(Request.Query["start"]);
            }
            if (PageValidate.IsDateTime(Request.Query["end"]))
            {
                end = DateTime.Parse(Request.Query["end"]);
            }

            List<string> empIds = null;
            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] emplist = Request.Query["emp_id"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (emplist.Length > 0)
                {
                    empIds = new List<string>(emplist);
                }
            }

            var result = await _followservice.ReportMonthEmpFollowAsync(start, end, empIds);
            return result.ToString();
        }

        public async Task<string> ReportEmpFollow()
        {
            int year = DateTime.Now.Year;
            if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                if (int.TryParse(Request.Query["year"], out var parsedYear) && parsedYear >= 2000 && parsedYear <= 2100)
                {
                    year = parsedYear;
                }
            }

            List<string> empIds = null;
            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] emplist = Request.Query["emp_id"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (emplist.Length > 0)
                {
                    empIds = new List<string>(emplist);
                }
            }

            var result = await _followservice.ReportEmpFollowAsync(year, empIds);
            return result.ToString();
        }
    }
}
