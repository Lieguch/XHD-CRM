using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// 对比报表页（Views/Report_Compared/Index.cshtml）。
    ///
    /// [Sprint 10.38 P2-12] 清理 3 个死 action（全树搜索 0 引用，视图实际调用的是
    /// /Report_Customer/* 与 /Report_Order/* 的同名端点）：
    ///     Grid             —— 无任何视图/JS 引用；分组逻辑与 Report_Order/Grid 重复
    ///     ComparedFollow   —— 视图调的是 /Report_Customer/ComparedFollow
    ///     ComparedEmpFollow—— 同上，Report_Customer 侧有等价实现
    /// 保留 Index：渲染对比报表页本身。
    /// </summary>
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
