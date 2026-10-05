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
using XHD.Core.Models;
using XHD.Core.View.Authorization;
using System.Security.Claims;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Common;
using XHD.Core.View;

using System.Linq.Expressions;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class SysButtonController : Controller
    {
        private readonly ILogger<SysButtonController> _logger;
        private readonly ISys_ButtonService _service;
        private readonly ISys_ButtonRepository _repository;
        private readonly IDBAuthService _dBAuthService;

        public SysButtonController(ILogger<SysButtonController> logger, ISys_ButtonService service, ISys_ButtonRepository repository, IDBAuthService dBAuthService)
        {
            _service = service;
            _repository = repository;
            _logger = logger;
            _dBAuthService = dBAuthService;
        }

        public async Task<string> Grid()
        {
            var sid = User.FindFirst(ClaimTypes.Sid)?.Value;
            if (string.IsNullOrWhiteSpace(sid))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var roledata = await _dBAuthService.GetDataAuth(sid);
            if (roledata.authtype != DataScope.ScopeAll)
            {
                return XHDResult.Error("无操作权限").ToString();
            }

            Expression<Func<Sys_Button, bool>> exp = a => true;
            var result = await _service.GridAsync(exp);

            return result.ToString();
        }
    }
}
