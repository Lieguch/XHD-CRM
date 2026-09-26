using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Security.Claims;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Common;
using XHD.Core.Models;
using XHD.Core.View.Configs;

namespace XHD.Core.View.Controllers
{
    /// <summary>
    /// SMS 短信控制器。
    /// Sprint 7 新增：#124 SMS.send / #125 SMS_Helper.getBalance（Send/GetBalance）。
    /// Sprint 8 新增：#157 SMS_Helper.getReport → QueryStatus。
    /// Sprint 10.26a 新增：Index/Add/Grid/Save/Delete/Report 完整 CRUD，
    /// 对齐 A 侧 CRM/Contact/sms.aspx + sms_add.aspx + sms_report.aspx。
    /// 保持既有 Send / GetBalance / QueryStatus 属性路由不变（向后兼容）。
    /// </summary>
    [Authorize]
    public class SMSController : Controller
    {
        private readonly ILogger<SMSController> _logger;
        private readonly ISMSService _service;
        private readonly ISMSRepository _smsRepo;
        private readonly Ihr_employeeService _empService;
        private readonly IDBAuthService _dBAuthService;

        public SMSController(
            ILogger<SMSController> logger,
            ISMSService service,
            ISMSRepository smsRepo,
            Ihr_employeeService empService,
            IDBAuthService dBAuthService)
        {
            _logger = logger;
            _service = service;
            _smsRepo = smsRepo;
            _empService = empService;
            _dBAuthService = dBAuthService;
        }

        // ========== Sprint 10.26a：新增 CRUD ==========

        /// <summary>
        /// Sprint 10.26a：短信列表主页。
        /// 对齐 A 侧 View/CRM/Contact/sms.aspx。
        /// </summary>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Sprint 10.26a：新增/编辑短信表单。
        /// 对齐 A 侧 View/CRM/Contact/sms_add.aspx。
        /// </summary>
        public IActionResult Add()
        {
            return View();
        }

        /// <summary>
        /// Sprint 10.26a：短信分页列表。
        /// 对齐 A 侧 SMS.grid.xhd（Server/SMS.cs:187 的 grid 端点）。
        /// 输出：XHDData&lt;JObject&gt;，每行含 create_name/check_name 富化字段。
        /// SMS 模型未配置员工导航属性，因此先取 SMS 分页，再批量查 hr_employee 拼装。
        /// </summary>
        public async Task<string> Grid(PageView<SMS> model)
        {
            Expression<Func<SMS, bool>> exp = a => true;

            // 可选：按标题模糊
            if (!string.IsNullOrWhiteSpace(Request.Query["serchtxt"]))
            {
                var kw = Request.Query["serchtxt"];
                exp = exp.And(a => a.sms_title.Contains(kw));
            }

            var page = model.Page > 0 ? model.Page : 1;
            var limit = model.Limit > 0 ? model.Limit : 15;
            var result = await _smsRepo.GridAsync(exp, page, limit, "a.create_time desc");

            // 批量拉取员工姓名映射
            var ids = new HashSet<string>();
            foreach (var s in result.data)
            {
                if (!string.IsNullOrWhiteSpace(s.create_id)) ids.Add(s.create_id);
                if (!string.IsNullOrWhiteSpace(s.check_id)) ids.Add(s.check_id);
            }

            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ids.Count > 0)
            {
                var emps = await _empService.GridAsync(a => ids.Contains(a.id));
                foreach (var e in emps.data)
                {
                    nameMap[e.id] = e.name ?? string.Empty;
                }
            }

            // 拼装为 XHDData<JObject>，保留前端 layuimini 表格列绑定字段
            var enriched = new List<JObject>();
            foreach (var s in result.data)
            {
                var o = JObject.FromObject(s);
                o["create_name"] = !string.IsNullOrWhiteSpace(s.create_id)
                    && nameMap.TryGetValue(s.create_id, out var cn)
                    ? cn : string.Empty;
                o["check_name"] = !string.IsNullOrWhiteSpace(s.check_id)
                    && nameMap.TryGetValue(s.check_id, out var ck)
                    ? ck : string.Empty;
                enriched.Add(o);
            }

            var dataObj = new XHDData<JObject>
            {
                code = 0,
                msg = "ok",
                count = result.count,
                data = enriched
            };

            return dataObj.ToString();
        }

        /// <summary>
        /// Sprint 10.26a：新增/编辑短信。
        /// 对齐 A 侧 SMS.save.xhd。
        /// id 空 = 新增，非空 = 编辑（仅编辑标题/内容/联系人/手机号，不改发送状态）。
        /// 校验：标题、内容、手机号不能为空；手机号按逗号分割后逐个校验 11 位。
        /// </summary>
        public async Task<string> Save(SMS model)
        {
            if (model == null)
            {
                return XHDResult.Error("参数无效").ToString();
            }

            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            if (string.IsNullOrWhiteSpace(model.sms_title))
            {
                return XHDResult.Error("短信主题不能为空").ToString();
            }
            if (string.IsNullOrWhiteSpace(model.sms_content))
            {
                return XHDResult.Error("短信内容不能为空").ToString();
            }
            if (string.IsNullOrWhiteSpace(model.sms_mobiles))
            {
                return XHDResult.Error("请至少添加一个手机号").ToString();
            }

            // 校验手机号格式（对齐 A 侧 sms_add.aspx 中 /^(1\d{10})$/ 校验）
            var mobiles = model.sms_mobiles
                .Split(new[] { ',', ';', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(m => m.Trim())
                .Where(m => m.Length > 0)
                .ToList();
            if (mobiles.Count == 0)
            {
                return XHDResult.Error("请至少添加一个手机号").ToString();
            }
            foreach (var m in mobiles)
            {
                if (m.Length != 11 || !m.StartsWith("1"))
                {
                    return XHDResult.Error($"手机号格式不正确：{m}").ToString();
                }
            }
            model.sms_mobiles = string.Join(",", mobiles);

            if (string.IsNullOrWhiteSpace(model.id))
            {
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.create_id = userId;
                model.create_time = DateTime.Now;
                if (model.isSend == null) model.isSend = 0;

                var r = await _smsRepo.AddAsync(model);
                return r > 0
                    ? XHDResult.Success().ToString()
                    : XHDResult.Error("保存失败").ToString();
            }
            else
            {
                // 编辑：不允许修改 isSend/sendtime/check_id，只更新内容三字段
                var r = await _smsRepo.UpdateAsync(
                    a => new SMS
                    {
                        sms_title = model.sms_title,
                        sms_content = model.sms_content,
                        contact_ids = model.contact_ids,
                        sms_mobiles = model.sms_mobiles
                    },
                    a => a.id == model.id);

                return r > 0
                    ? XHDResult.Success().ToString()
                    : XHDResult.Error("短信不存在或保存失败").ToString();
            }
        }

        /// <summary>
        /// Sprint 10.26a：删除短信（软删除走 isSend + delete 语义）。
        /// 对齐 A 侧 SMS.del.xhd 逻辑：isSend==1 时拒绝删除。
        /// </summary>
        public async Task<string> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return XHDResult.Error("短信ID无效").ToString();
            }

            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var sms = await _smsRepo.GetByIdAsync(id);
            if (sms == null)
            {
                return XHDResult.Error("短信不存在").ToString();
            }

            if (sms.isSend == 1)
            {
                return XHDResult.Error("此短信已发送，不能删除！").ToString();
            }

            var r = await _smsRepo.DeleteAsync(id);
            return r > 0
                ? XHDResult.Success().ToString()
                : XHDResult.Error("删除失败").ToString();
        }

        /// <summary>
        /// Sprint 10.26a：发送状态报告页。
        /// 对齐 A 侧 View/CRM/Contact/sms_report.aspx。
        /// </summary>
        public IActionResult Report(string id)
        {
            ViewData["smsid"] = id ?? string.Empty;
            return View();
        }

        // ========== Sprint 7 / Sprint 8：既有属性路由（保持兼容） ==========

        /// <summary>
        /// Sprint 7 #124 SMS.send：审核并发送短信（更新 isSend/sendtime/check_id）。
        /// 对应 A 侧 Server.SMS.send（Server/SMS.cs:187）。
        /// </summary>
        [HttpPost("send")]
        public async Task<string> Send(string id)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var result = await _service.SendAsync(id ?? string.Empty, userId);
            return result;
        }

        /// <summary>
        /// Sprint 7 #125 SMS_Helper.getBalance：查询短信余额。
        /// 未配置 SMS 或异常时返回 0（不抛异常）。
        /// </summary>
        [HttpGet("getBalance")]
        public async Task<string> GetBalance()
        {
            var balance = await _service.GetBalanceAsync();
            var obj = new JObject { { "balance", balance } };
            return XHDResult.Success(obj).ToString();
        }

        /// <summary>
        /// Sprint 8 #157 SMS_Helper.getReport：查询短信状态报告（回执）。
        /// 未配置 SMS 或异常时返回空 JArray。
        /// 对应 A 侧 SMS/SMSHelper.cs:167 getReport。
        /// </summary>
        [HttpGet("queryStatus")]
        public async Task<string> QueryStatus()
        {
            var arr = await _service.QueryStatusAsync();
            return XHDResult.Success(arr).ToString();
        }

        /// <summary>
        /// 取当前登录用户 ID（ClaimTypes.Sid）。
        /// </summary>
        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.Sid)?.Value;
        }
    }
}
