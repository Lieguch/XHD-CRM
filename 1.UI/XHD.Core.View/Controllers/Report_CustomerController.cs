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
    public class Report_CustomerController : Controller
    {
        private readonly ILogger<Report_CustomerController> _logger;
        private readonly ICRM_CustomerService _service;
        private readonly ICRM_followService _followservice;
        private readonly IDBAuthService _dBAuthService;

        private readonly SysLogExt<CRM_Customer> logext = new SysLogExt<CRM_Customer>();

        public Report_CustomerController(
            ILogger<Report_CustomerController> logger,
            ICRM_CustomerService service,
            ICRM_followService followservice,
            IDBAuthService dBAuthService
            )
        {
            _service = service;
            _followservice=followservice;
            _logger = logger;  
            _dBAuthService = dBAuthService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<string> ReportYear()
        {
            Expression<Func<CRM_Customer, bool>> exp = a => true;

            // 年份处理：提取当前年份变量，避免表达式缓存
            int currentYear = DateTime.Now.Year;
            if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                int queryYear = int.Parse(Request.Query["year"]);
                exp = exp.And(a => a.create_time.Value.Year == queryYear);
            }
            else
            {
                exp = exp.And(a => a.create_time.Value.Year == currentYear);
            }

            // emp_id IN 查询
            string empIdStr = Request.Query["emp_id"];
            if (!string.IsNullOrWhiteSpace(empIdStr))
            {
                string[] empArray = empIdStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                // 必须判断数组非空，防止 IN() 语法错误
                if (empArray.Length > 0)
                {
                    // 关键点：直接使用数组变量，不要用本地List
                    exp = exp.And(a => empArray.Contains(a.emp_id));
                }
            }

            var result = await _service.ReportYear(exp);

            JArray arr=new JArray();
            for (int i = 1; i <= 12; i++)
            {
                JObject obj = new JObject();
                obj.Add("xmonth", i);

                var sdata= result.Where(a => a.Value<int>("xmonth") == i).FirstOrDefault();
                
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
            Expression<Func<CRM_follow, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                exp = exp.And(a => a.follow_time.Value.Year == Request.Query["year"]);
            }
            else
            {
                exp = exp.And(a => a.follow_time.Value.Year == DateTime.Now.Year);
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] emplist = Request.Query["emp_id"].ToString().Split(',');

                if (emplist.Length > 0)
                {
                    var list = new List<string>();

                    foreach (var emp in emplist)
                    {
                        list.Add(emp.ToString());
                    }
                    exp = exp.And(a => list.Contains(a.employee_id));
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

        public async Task<string> ReportIndustry()
        {
            Expression<Func<CRM_Customer, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                exp = exp.And(a => a.create_time.Value.Year == Request.Query["year"]);
            }
            else
            {
                exp = exp.And(a => a.create_time.Value.Year == DateTime.Now.Year);
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] emplist = Request.Query["emp_id"].ToString().Split(',');

                if (emplist.Length > 0)
                {
                    var list = new List<string>();

                    foreach (var emp in emplist)
                    {
                        list.Add(emp.ToString());
                    }
                    exp = exp.And(a => list.Contains(a.emp_id));
                }
            }            

            var result = await _service.ReportIndustry(exp);

            JArray arr = new JArray();
            for (int i = 0; i < result.Count; i++)
            {
                JObject obj = new JObject();
                obj.Add("value", result[i].Value<int>("count"));
                obj.Add("name", result[i].Value<string>("xmonth"));

                arr.Add(obj);
            }

            return arr.ToString();
        }

        public async Task<string> ReportType()
        {
            Expression<Func<CRM_Customer, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                exp = exp.And(a => a.create_time.Value.Year == Request.Query["year"]);
            }
            else
            {
                exp = exp.And(a => a.create_time.Value.Year == DateTime.Now.Year);
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] emplist = Request.Query["emp_id"].ToString().Split(',');

                if (emplist.Length > 0)
                {
                    var list = new List<string>();

                    foreach (var emp in emplist)
                    {
                        list.Add(emp.ToString());
                    }
                    exp = exp.And(a => list.Contains(a.emp_id));
                }
            }


            var result = await _service.ReportType(exp);

            JArray arr = new JArray();
            for (int i = 0; i < result.Count; i++)
            {
                JObject obj = new JObject();
                obj.Add("value", result[i].Value<int>("count"));
                obj.Add("name", result[i].Value<string>("xmonth"));

                arr.Add(obj);
            }

            return arr.ToString();
        }

        public async Task<string> ReportLevel()
        {
            Expression<Func<CRM_Customer, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                exp = exp.And(a => a.create_time.Value.Year == Request.Query["year"]);
            }
            else
            {
                exp = exp.And(a => a.create_time.Value.Year == DateTime.Now.Year);
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] emplist = Request.Query["emp_id"].ToString().Split(',');

                if (emplist.Length > 0)
                {
                    var list = new List<string>();

                    foreach (var emp in emplist)
                    {
                        list.Add(emp.ToString());
                    }
                    exp = exp.And(a => list.Contains(a.emp_id));
                }
            }


            var result = await _service.ReportLevel(exp);

            JArray arr = new JArray();
            for (int i = 0; i < result.Count; i++)
            {
                JObject obj = new JObject();
                obj.Add("value", result[i].Value<int>("count"));
                obj.Add("name", result[i].Value<string>("xmonth"));

                arr.Add(obj);
            }

            return arr.ToString();
        }

        public async Task<string> ReportSource()
        {
            Expression<Func<CRM_Customer, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                exp = exp.And(a => a.create_time.Value.Year == Request.Query["year"]);
            }
            else
            {
                exp = exp.And(a => a.create_time.Value.Year == DateTime.Now.Year);
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] emplist = Request.Query["emp_id"].ToString().Split(',');

                if (emplist.Length > 0)
                {
                    var list = new List<string>();

                    foreach (var emp in emplist)
                    {
                        list.Add(emp.ToString());
                    }
                    exp = exp.And(a => list.Contains(a.emp_id));
                }
            }


            var result = await _service.ReportSource(exp);

            JArray arr = new JArray();
            for (int i = 0; i < result.Count; i++)
            {
                JObject obj = new JObject();
                obj.Add("value", result[i].Value<int>("count"));
                obj.Add("name", result[i].Value<string>("xmonth"));

                arr.Add(obj);
            }

            return arr.ToString();
        }

        public async Task<string> ReportProvinces()
        {
            Expression<Func<CRM_Customer, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                exp = exp.And(a => a.create_time.Value.Year == Request.Query["year"]);
            }
            //else
            //{
            //    exp = exp.And(a => a.create_time.Value.Year == DateTime.Now.Year);
            //}

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] emplist = Request.Query["emp_id"].ToString().Split(',');

                if (emplist.Length > 0)
                {
                    var list = new List<string>();

                    foreach (var emp in emplist)
                    {
                        list.Add(emp.ToString());
                    }
                    exp = exp.And(a => list.Contains(a.emp_id));
                }
            }

            var result = await _service.ReportProvinces(exp);

            JArray arr = new JArray();
            for (int i = 0; i < result.Count; i++)
            {
                JObject obj = new JObject();
                obj.Add("value", result[i].Value<int>("count"));
                obj.Add("name", result[i].Value<string>("xmonth"));

                arr.Add(obj);
            }

            return arr.ToString();
        }

        public async Task<string> ReportCity()
        {
            Expression<Func<CRM_Customer, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(Request.Query["year"]))
            {
                exp = exp.And(a => a.create_time.Value.Year == Request.Query["year"]);
            }
            //else
            //{
            //    exp = exp.And(a => a.create_time.Value.Year == DateTime.Now.Year);
            //}

            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                string[] emplist = Request.Query["emp_id"].ToString().Split(',');

                if (emplist.Length > 0)
                {
                    var list = new List<string>();

                    foreach (var emp in emplist)
                    {
                        list.Add(emp.ToString());
                    }
                    exp = exp.And(a => list.Contains(a.emp_id));
                }
            }

            var result = await _service.ReportCity(exp);

            JArray arr = new JArray();
            for (int i = 0; i < result.Count; i++)
            {
                JObject obj = new JObject();
                obj.Add("value", result[i].Value<int>("count"));
                obj.Add("name", result[i].Value<string>("xmonth"));

                arr.Add(obj);
            }

            return arr.ToString();
        }

        /// <summary>
        /// 客户转化漏斗：按客户类型（cus_type_id 关联 Sys_Param）统计年度客户数
        /// </summary>
        [HttpGet("Funnel")]
        public async Task<string> Funnel(int? year)
        {
            var data = await _service.FunnelAsync(year);
            return data.ToString();
        }

        /// <summary>
        /// 员工年度客户新增报表（Wave 3b #13）：
        /// 按年份 × 12 个月 × 员工维度输出新增客户数
        /// </summary>
        /// <param name="year">统计年份，缺省为当前年份</param>
        /// <param name="empIds">参与统计的员工 ID 列表，逗号分隔</param>
        [HttpGet("ReportEmpCus")]
        public async Task<string> ReportEmpCus(int? year, [FromQuery] List<string> empIds = null)
        {
            int y = year ?? DateTime.Now.Year;
            var data = await _service.ReportEmpCusAsync(y, empIds);
            return data.ToString();
        }

        /// <summary>
        /// 员工月度客户新增报表（Wave 3b #14）：
        /// 按时间区间 × 12 个月 × 员工维度输出新增客户数
        /// </summary>
        /// <param name="start">起始时间（含），例如 2024-01-01</param>
        /// <param name="end">结束时间（含），例如 2024-12-31</param>
        /// <param name="empIds">参与统计的员工 ID 列表，逗号分隔</param>
        [HttpGet("ReportMonthEmpCus")]
        public async Task<string> ReportMonthEmpCus(
            DateTime? start,
            DateTime? end,
            [FromQuery] List<string> empIds = null)
        {
            var data = await _service.ReportMonthEmpCusAsync(start, end, empIds);
            return data.ToString();
        }

        /// <summary>
        /// 员工维度双月客户新增对比（Wave 3b #15）：
        /// 输出每员工 startMonth 与 endMonth 的新增数及差值
        /// </summary>
        /// <param name="year">统计年份，缺省为当前年份</param>
        /// <param name="startMonth">起始月份（1-12），缺省取年首（1 月）</param>
        /// <param name="endMonth">结束月份（1-12），缺省取年末（12 月）</param>
        /// <param name="empIds">参与统计的员工 ID 列表，逗号分隔</param>
        [HttpGet("ComparedEmpCusAdd")]
        public async Task<string> ComparedEmpCusAdd(
            int? year,
            int? startMonth,
            int? endMonth,
            [FromQuery] List<string> empIds = null)
        {
            int y = year ?? DateTime.Now.Year;
            int? sm = startMonth ?? 1;
            int? em = endMonth ?? 12;
            var data = await _service.ComparedEmpCusAddAsync(sm, em, y, empIds);
            return data.ToString();
        }

    }
}
