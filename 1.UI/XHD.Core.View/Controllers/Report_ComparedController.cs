using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class Report_ComparedController : Controller
    {
        private readonly ILogger<Report_ComparedController> _logger;

        public Report_ComparedController(ILogger<Report_ComparedController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }
    }
}
