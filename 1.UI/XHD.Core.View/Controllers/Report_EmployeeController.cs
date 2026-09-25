using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class Report_EmployeeController : Controller
    {
        private readonly ILogger<Report_EmployeeController> _logger;

        public Report_EmployeeController(ILogger<Report_EmployeeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }
    }
}
