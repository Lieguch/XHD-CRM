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
    public class Report_ComparedController : Controller
    {
        private readonly ILogger<Report_ComparedController> _logger;
        private readonly ICRM_followService _followservice;
        private readonly IDBAuthService _dBAuthService;

        public Report_ComparedController(
            ILogger<Report_ComparedController> logger,
            ICRM_followService followservice,
            IDBAuthService dBAuthService
            )
        {
            _logger = logger;
            _followservice = followservice;
            _dBAuthService = dBAuthService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<string> ComparedFollow()
        {
            int year1 = DateTime.Now.Year;
            int month1 = DateTime.Now.Month;
            int year2 = DateTime.Now.Year;
            int month2 = DateTime.Now.Month;

            if (!string.IsNullOrWhiteSpace(Request.Query["year1"]))
            {
                if (int.TryParse(Request.Query["year1"], out var y1) && y1 >= 2000 && y1 <= 2100)
                {
                    year1 = y1;
                }
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["month1"]))
            {
                if (int.TryParse(Request.Query["month1"], out var m1) && m1 >= 1 && m1 <= 12)
                {
                    month1 = m1;
                }
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["year2"]))
            {
                if (int.TryParse(Request.Query["year2"], out var y2) && y2 >= 2000 && y2 <= 2100)
                {
                    year2 = y2;
                }
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["month2"]))
            {
                if (int.TryParse(Request.Query["month2"], out var m2) && m2 >= 1 && m2 <= 12)
                {
                    month2 = m2;
                }
            }

            var result = await _followservice.ComparedFollowAsync(year1, month1, year2, month2);
            return result.ToString();
        }

        public async Task<string> ComparedEmpFollow()
        {
            int year1 = DateTime.Now.Year;
            int month1 = DateTime.Now.Month;
            int year2 = DateTime.Now.Year;
            int month2 = DateTime.Now.Month;

            if (!string.IsNullOrWhiteSpace(Request.Query["year1"]))
            {
                if (int.TryParse(Request.Query["year1"], out var y1) && y1 >= 2000 && y1 <= 2100)
                {
                    year1 = y1;
                }
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["month1"]))
            {
                if (int.TryParse(Request.Query["month1"], out var m1) && m1 >= 1 && m1 <= 12)
                {
                    month1 = m1;
                }
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["year2"]))
            {
                if (int.TryParse(Request.Query["year2"], out var y2) && y2 >= 2000 && y2 <= 2100)
                {
                    year2 = y2;
                }
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["month2"]))
            {
                if (int.TryParse(Request.Query["month2"], out var m2) && m2 >= 1 && m2 <= 12)
                {
                    month2 = m2;
                }
            }

            List<string> empIds = null;

            var roledata = await _dBAuthService.GetDataAuth(User.FindFirst(ClaimTypes.Sid).Value);
            if (roledata.authtype != 4 && roledata.empList != null && roledata.empList.Count > 0)
            {
                empIds = new List<string>(roledata.empList);
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
                    if (empIds == null)
                    {
                        empIds = new List<string>();
                    }
                    empIds.AddRange(emplist);
                }
            }

            var result = await _followservice.ComparedEmpCusFollowAsync(year1, month1, year2, month2, empIds);
            return result.ToString();
        }

        public async Task<string> Grid()
        {
            int year = DateTime.Now.Year;
            int month = DateTime.Now.Month;

            if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                if (int.TryParse(Request.Query["year"], out var y) && y >= 2000 && y <= 2100)
                {
                    year = y;
                }
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["month"]))
            {
                if (int.TryParse(Request.Query["month"], out var m) && m >= 1 && m <= 12)
                {
                    month = m;
                }
            }

            Expression<Func<CRM_follow, bool>> exp = a => true;
            exp = exp.And(a => a.follow_time.Value.Year == year);
            exp = exp.And(a => a.follow_time.Value.Month == month);

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] empArray = Request.Query["emp_id"].Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (empArray.Length > 0)
                {
                    exp = exp.And(a => empArray.Contains(a.employee_id));
                }
            }

            var data = await _followservice.GridAsync(exp);

            var grouped = (data.data ?? new List<CRM_follow>())
                .GroupBy(a => a.employee_id)
                .Select(g => new { employee_id = g.Key, count = g.Count() })
                .ToList();

            JArray arr = new JArray();
            foreach (var item in grouped)
            {
                JObject obj = new JObject();
                obj.Add("employee_id", item.employee_id);
                obj.Add("count", item.count);
                arr.Add(obj);
            }

            return arr.ToString();
        }
    }
}
