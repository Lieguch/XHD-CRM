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
using XHD.Core.View.Authorization;

using System.Linq.Expressions;
using Newtonsoft.Json.Converters;
using XHD.Core.View.Configs;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class HrEmployeeController : Controller
    {
        private readonly ILogger<HrEmployeeController> _logger;
        private readonly Ihr_employeeService _service;
        private readonly ICRM_CustomerService _customerservice;
        private readonly ICRM_followService _followservice;
        private readonly ISale_orderService _orderservice;
        private readonly ISale_contractService _contractservice;
        private readonly IFinance_ReceiveService _receiveservice;
        private readonly IFinance_InvoiceService _invoiceservice;
        private readonly ISys_logService _LogService;
        private readonly IDBAuthService _dBAuthService;
        private readonly SysLogExt<hr_employee> logext = new SysLogExt<hr_employee>();  //日志

        public HrEmployeeController(
            ILogger<HrEmployeeController> logger,
            Ihr_employeeService service,
            ICRM_CustomerService customerservice,
            ICRM_followService followservice,
            ISale_orderService orderservice,
            ISale_contractService contractservice,
            IFinance_ReceiveService receiveservice,
            IFinance_InvoiceService invoiceservice,
            ISys_logService LogService,
            IDBAuthService dBAuthService
            )
        {
            _service = service;
            _logger = logger;
            _customerservice = customerservice;
            _followservice = followservice;
            _orderservice = orderservice;
            _contractservice = contractservice;
            _receiveservice = receiveservice;
            _invoiceservice = invoiceservice;

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

        public IActionResult Me()
        {
            return View();
        }

        public IActionResult password()
        {
            return View();
        }

        public IActionResult syspwd()
        {
            return View();
        }

        /// <summary>
        /// 员工选择器列表（被 8 处前端 tableSelect 调用）。
        /// 数据权限：非全部权限（authtype&lt;5）只能选到权限范围内员工；对应 A 版
        /// Getemp_Auth 语义（A 版 Server/hr_employee.cs:80-96 服务端过滤已被 A 版自己整体注释失效，
        /// B 版以 DataScope 统一收口，不复活 A 版注释逻辑）。admin（authtype=5）不过滤。
        /// </summary>
        public async Task<string> Grid(PageView<hr_employee> model)
        {
            Expression<Func<hr_employee, bool>> exp = a => a.id != "admin";

            // 数据权限收口：非全部权限（authtype<5）只能选到 empList 范围内员工。
            // 注意：不能用 EmployeeIds.Count>0 守卫——authtype=0（无权限）时 empList 为空，
            // 那样会跳过过滤变成全可见（越权）。DataScope.Resolve 保证 NeedsFilter=true 时
            // EmployeeIds 非空（null 归一为空列表），空列表 Contains 即空集，与既有 21 个控制器口径一致。
            var role = await _dBAuthService.GetDataAuth(GetUserId());
            var scope = DataScope.Resolve(role);
            if (scope.NeedsFilter)
            {
                exp = exp.And(a => scope.EmployeeIds.Contains(a.id));
            }

            if (Request.Query["id"].Equals("me"))
            {
                exp = a => a.id == User.FindFirst(ClaimTypes.Sid).Value;
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["T_name"]))
            {
                exp = exp.And(a => a.name.Contains(Request.Query["T_name"]));
            }

            if (!string.IsNullOrWhiteSpace(Request.Query["keyword"]))
            {
                exp = exp.And(a => a.name.Contains(Request.Query["keyword"]));
            }

            

            var result = await _service.GridAsync(exp, model.Page, model.Limit, "sort");

            return result.ToString();
        }

        public async Task<string> Save(hr_employee model)
        {
            var result = 0;

            if (string.IsNullOrWhiteSpace(model.id))
            {
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                //权限
                var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "hr_employee|add");

                if (authbtn)
                {
                    result = await _service.AddAsync(model);
                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }
            else
            {
                //权限
                var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "hr_employee|edit");

                if (authbtn)
                {
                    //日志
                    Expression<Func<hr_employee, bool>> exp = a => a.id == model.id;
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
                        logmodels.EventType = "[员工]修改";
                        logmodels.EventID = model.id;
                        logmodels.EventTitle = model.name;
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



            //Expression<Func<Sys_role_emp, bool>> exproleemp = a => a.empID == model.id;

            //await _role_empService.DeleteAsync(exproleemp);

            //Sys_role_emp modelroleemp = new Sys_role_emp();

            //modelroleemp.empID = model.id;

            //JArray arr = JArray.Parse(Request.Form["T_data"]);

            //}


            return XHDResult.Success().ToString();
        }

        /// <summary>
        /// 员工自助修改个人资料（对应 A 版 <c>Server/hr_employee.cs:321 PersonalUpdate</c>）。
        /// </summary>
        /// <remarks>
        /// <para>根因设计（复刻 A 版安全语义）：更新目标固定为当前登录用户，<b>忽略表单传入的任何 id</b>
        /// —— A 版 Server 用 <c>model.id = emp_id</c> 强制自作用域，B 版同语义取 <c>ClaimTypes.Sid</c>，
        /// 从根上杜绝「提交他人 id 改他人资料」的越权路径。</para>
        /// <para>只更新个人资料白名单字段（对齐 A 版 <c>DAL/hr_employee.cs:578-588</c> 的 UPDATE 列集，
        /// B 版 <c>headimg</c> 替代 A 版 <c>title</c>）；uid / 部门 / 职务 / 岗位 / 状态 / 能否登录 /
        /// 排序 / 密码 / 入职日期等管理字段一律不动。</para>
        /// <para>权限：登录即可自助修改本人资料（A 版 PersonalUpdate 无按钮授权，个人主页入口在顶栏
        /// 用户下拉而非菜单），故不走 <c>[ButtonAuth]</c>，仅 controller 级 <c>[Authorize]</c>。</para>
        /// <para>更新方式：FreeSql <c>Set(表达式)</c> 只更新被赋值的列（官方文档实证），避免全列更新
        /// 冲掉未提交的管理字段。</para>
        /// </remarks>
        /// <param name="model">表单提交的个人资料（仅白名单字段生效）</param>
        /// <returns>XHDResult JSON</returns>
        [HttpPost]
        public async Task<string> PersonalUpdate(hr_employee model)
        {
            // 自作用域：以登录态为准，忽略表单 id（A 版 model.id = emp_id 同语义）
            var empId = GetUserId();
            if (string.IsNullOrWhiteSpace(empId))
            {
                return XHDResult.Error("未登录！").ToString();
            }

            // 必填校验（与 Me.cshtml 的 lay-verify="required" 口径一致，服务端兜底）
            if (string.IsNullOrWhiteSpace(model.name))
            {
                return XHDResult.Error("姓名不能为空！").ToString();
            }
            if (string.IsNullOrWhiteSpace(model.tel))
            {
                return XHDResult.Error("电话不能为空！").ToString();
            }

            // 取旧实体：既用于变更日志 diff，也确认目标记录存在
            Expression<Func<hr_employee, bool>> exp = a => a.id == empId;
            var oldData = await _service.GridAsync(exp, 1, 1);
            if (oldData.count == 0)
            {
                return XHDResult.Error("找不到数据！").ToString();
            }
            var old = oldData.data[0];

            // 列白名单更新（FreeSql Set(表达式) 只更新被赋值的列，未列字段保持原值）
            var result = await _service.UpdateAsync(
                a => new hr_employee
                {
                    name = model.name,
                    idcard = model.idcard,
                    birthday = model.birthday,
                    email = model.email,
                    sex = model.sex,
                    tel = model.tel,
                    address = model.address,
                    education = model.education,
                    professional = model.professional,
                    schools = model.schools,
                    headimg = model.headimg
                },
                a => a.id == empId);

            if (result == 0)
            {
                return XHDResult.Error("操作失败，系统错误！").ToString();
            }

            // 变更日志：构造「旧值 + 白名单新值」的 after 副本，使 logext 只 diff 白名单字段
            // （非白名单字段沿用旧值，不会产生假 diff；pwd 保持旧值，避免把密码哈希写进日志）
            var after = new hr_employee
            {
                id = old.id,
                uid = old.uid,
                dep_id = old.dep_id,
                position_id = old.position_id,
                post_id = old.post_id,
                role_id = old.role_id,
                status = old.status,
                canlogin = old.canlogin,
                sort = old.sort,
                default_city = old.default_city,
                EntryDate = old.EntryDate,
                pwd = old.pwd,
                create_id = old.create_id,
                create_time = old.create_time,
                remarks = old.remarks,
                Delete_id = old.Delete_id,
                name = model.name,
                idcard = model.idcard,
                birthday = model.birthday,
                email = model.email,
                sex = model.sex,
                tel = model.tel,
                address = model.address,
                education = model.education,
                professional = model.professional,
                schools = model.schools,
                headimg = model.headimg
            };

            var content = logext.LogContent(old, after);
            if (content.Length > 0)
            {
                Sys_log logmodel = new Sys_log();
                logmodel.id = UUIDNext.Uuid.NewSequential().ToString();
                logmodel.EventType = "个人信息修改";
                logmodel.EventID = empId;
                logmodel.EventTitle = model.name;
                logmodel.UserID = empId;
                logmodel.UserName = User.FindFirst(ClaimTypes.Name)?.Value;
                logmodel.IPStreet = HttpContext.Connection.RemoteIpAddress.ToString();
                logmodel.EventDate = DateTime.Now;
                logmodel.Log_Content = content;

                await _LogService.UpdateLog(logmodel);
            }

            return XHDResult.Success().ToString();
        }

        public async Task<string> Delete(string id)
        {
            if (id.Equals("admin"))
            {
                //删管理员，还是算了吧
                return XHDResult.Error("管理员不能删除！").ToString();
            }

            //客户
            Expression<Func<CRM_Customer, bool>> expcustomer = a => a.emp_id == id;

            var resultcustomer = await _customerservice.GridAsync(expcustomer, 1, 1);

            if (resultcustomer.count > 0)
            {
                return XHDResult.Error("此员工下有客户，不能删除！").ToString();
            }

            //跟进
            Expression<Func<CRM_follow, bool>> expfollow = a => a.employee_id == id;

            var resultfollow = await _followservice.GridAsync(expfollow, 1, 1);

            if (resultfollow.count > 0)
            {
                return XHDResult.Error("此员工下有跟进，不能删除！").ToString();
            }

            //订单
            Expression<Func<Sale_order, bool>> exporder = a => a.emp_id == id;

            var resultorder = await _orderservice.GridAsync(exporder, 1, 1);

            if (resultorder.count > 0)
            {
                return XHDResult.Error("此员工下有订单，不能删除！").ToString();
            }

            //合同
            Expression<Func<Sale_contract, bool>> expcontract = a => a.Our_Contractor_id == id;

            var resultcontract = await _contractservice.GridAsync(expcontract, 1, 1);

            if (resultcontract.count > 0)
            {
                return XHDResult.Error("此员工下有合同，不能删除！").ToString();
            }

            //收款
            Expression<Func<Finance_Receive, bool>> expreceive = a => a.Payee_id == id;

            var resultreceive = await _receiveservice.GridAsync(expreceive, 1, 1);

            if (resultreceive.count > 0)
            {
                return XHDResult.Error("此员工下有收款，不能删除！").ToString();
            }

            //发票
            Expression<Func<Finance_Invoice, bool>> expinvoice = a => a.emp_id == id;

            var resultinvoice = await _invoiceservice.GridAsync(expinvoice, 1, 1);

            if (resultinvoice.count > 0)
            {
                return XHDResult.Error("此员工下有发票，不能删除！").ToString();
            }

            var result = 0;

            //权限
            var authbtn = await _dBAuthService.GetAuth(User.FindFirst(ClaimTypes.Sid).Value, "hr_employee|del");

            if (authbtn)
            {
                //判断是否有数据
                Expression<Func<hr_employee, bool>> exp = a => a.id == id;
                var checkdata = await _service.GridAsync(exp, 1, 1);

                if (checkdata.count == 0)
                {
                    return XHDResult.Error("找不到此数据！").ToString();
                }

                result = await _service.DeleteAsync(id);

                //先存储删除的实体记录，用日志形式
                logext.getEntityText(checkdata.data[0]);

                //记录日志
                Sys_log logmodels = new Sys_log();

                logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                logmodels.EventType = "[员工]删除";
                logmodels.EventID = id;
                logmodels.EventTitle = checkdata.data[0].name;
                logmodels.UserID = User.FindFirst(ClaimTypes.Sid).Value;
                logmodels.UserName = User.FindFirst(ClaimTypes.Name).Value;
                logmodels.IPStreet = HttpContext.Connection.RemoteIpAddress.ToString();
                logmodels.EventDate = DateTime.Now;
                //logmodels.Log_Content = checkdata.data[0].follow_content;


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

            return XHDResult.Success().ToString();
        }

        public async Task<string> modifyPWD()
        {
            var oldpassword = Request.Form["oldpassword"];

            Expression<Func<hr_employee, bool>> expwhere = a => a.id == User.FindFirst(ClaimTypes.Sid).Value;

            var data = await _service.GridAsync(expwhere);

            if (data.count == 0)
            {
                return XHDResult.Error("系统错误，找不到此用户！").ToString();
            }

            // [Sprint 10.38 P1-7] 规范密钥 = MD5(明文).ToUpper()，Verify 兼容存量无盐 MD5
            var checkpwd = Common.DEncrypt.PasswordHasher.CanonicalSecret(oldpassword);

            if (!Common.DEncrypt.PasswordHasher.Verify(data.data[0].pwd, checkpwd))
            {
                return XHDResult.Error("原密码不正确！").ToString();
            }

            var password = Request.Form["password"];

            Expression<Func<hr_employee, hr_employee>> exppwd = a => new hr_employee { pwd = Common.DEncrypt.PasswordHasher.Hash(Common.DEncrypt.PasswordHasher.CanonicalSecret(password)) };


            await _service.UpdateAsync(exppwd, expwhere);

            return XHDResult.Success("修改成功！").ToString();
        }

        public async Task<string> PWD()
        {
            var password = Request.Form["password"];
            var id = Request.Form["id"];

            // 权限检查：需要 hr_employee|edit 权限
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.Sid).Value;
            if (!await _dBAuthService.GetAuth(userId, "hr_employee|edit"))
            {
                return XHDResult.Error("无操作权限").ToString();
            }

            // 只能重置自己的密码（admin 可重置任何人）
            var currentEmpData = await _service.GridAsync(a => a.id == userId);
            if (currentEmpData.count == 0)
            {
                return XHDResult.Error("找不到当前用户").ToString();
            }
            var currentEmp = currentEmpData.data[0];
            if (currentEmp.id != "admin" && id != userId)
            {
                return XHDResult.Error("只能修改自己的密码").ToString();
            }

            Expression<Func<hr_employee, hr_employee>> exppwd = a => new hr_employee { pwd = Common.DEncrypt.PasswordHasher.Hash(Common.DEncrypt.PasswordHasher.CanonicalSecret(password)) };
            Expression<Func<hr_employee, bool>> expwhere = a => a.id == id;

            var result = await _service.UpdateAsync(exppwd, expwhere);
            if (result <= 0)
            {
                return XHDResult.Error("修改失败").ToString();
            }

            return XHDResult.Success("修改成功！").ToString();
        }

        /// <summary>
        /// 取当前登录用户 ID（ClaimTypes.Sid）。仅用于业务逻辑；
        /// Sys_log.UserName 场景请使用 User.FindFirst(ClaimTypes.Name)?.Value。
        /// </summary>
        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.Sid)?.Value;
        }

        /// <summary>
        /// Sprint 4 Wave 1b #10：员工唯一性校验。
        /// 对应 A 侧 Server.hr_employee.Exist：校验 uid / name 是否已被他人占用。
        /// id 非空时排除自身（编辑场景，A 侧 id&lt;&gt;'{model.id}' 语义）。
        /// 返回裸字符串 "true"/"false"（与 A 侧契约一致，前端直接比对字符串，不走 XHDResult 包装）。
        /// 参数化执行（FreeSql 表达式），禁止字符串拼接 SQL。
        /// </summary>
        /// <param name="field">校验字段，取值 uid 或 name</param>
        /// <param name="value">待校验的值</param>
        /// <param name="id">当前员工 ID（可选）；非空时排除自身</param>
        /// <returns>"true" 表示已被占用；"false" 表示可用；参数非法时返回 XHDResult 错误 JSON</returns>
        [HttpGet("Exist")]
        public async Task<string> Exist(string field, string value, string id = null)
        {
            if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(value))
            {
                return XHDResult.Error("field 和 value 不能同时为空").ToString();
            }

            Expression<Func<hr_employee, bool>> exp;

            if (field == "uid")
            {
                exp = a => a.uid == value;
            }
            else if (field == "name")
            {
                exp = a => a.name == value;
            }
            else
            {
                return XHDResult.Error("field 只能是 uid 或 name").ToString();
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                exp = exp.And(a => a.id != id);
            }

            var count = await _service.ExistsAsync(exp);
            return count > 0 ? "true" : "false";
        }

        /// <summary>
        /// Sprint 4 Wave 1b #11：读取当前登录员工的默认城市。
        /// 对应 A 侧 Server.hr_employee.getDefaultCity。
        /// 勘误 C3：字段名是 default_city（不是 default_city_id），已存在，无需 schema 补强。
        /// </summary>
        /// <returns>标准 XHDResult 字符串，data[0] 承载默认城市值</returns>
        [HttpGet("getDefaultCity")]
        public async Task<string> getDefaultCity()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return XHDResult.Error("登录状态已过期").ToString();
            }

            var city = await _service.GetDefaultCityAsync(userId);
            if (city == null)
            {
                return XHDResult.Error("员工不存在").ToString();
            }

            return XHDResult.Success(city).ToString();
        }

        /// <summary>
        /// Sprint 4 Wave 1b #12：更新员工默认城市。
        /// 对应 A 侧 Server.hr_employee.updateDefaultCity。
        /// id 为空时默认更新当前登录员工（保持 A 侧 emp_id=this.emp_id 语义）；
        /// id 非空时更新指定员工（供管理员代设场景使用）。
        /// 注意：不用 BaseService.UpdateAsync(model)，因其 IgnoreColumns 明确忽略 default_city 字段。
        /// </summary>
        /// <param name="city">目标城市值，不能为空</param>
        /// <param name="id">目标员工 ID（可选）；为空时使用当前登录用户</param>
        /// <returns>标准 XHDResult 字符串</returns>
        [HttpPost("updateDefaultCity")]
        public async Task<string> updateDefaultCity(string city, string id = null)
        {
            if (string.IsNullOrWhiteSpace(city))
            {
                return XHDResult.Error("城市不能为空").ToString();
            }

            var empId = string.IsNullOrWhiteSpace(id) ? GetUserId() : id;
            if (string.IsNullOrWhiteSpace(empId))
            {
                return XHDResult.Error("员工ID无效").ToString();
            }

            var ok = await _service.UpdateDefaultCityAsync(empId, city);
            return ok ? XHDResult.Success("更新成功").ToString() : XHDResult.Error("更新失败").ToString();
        }
    }
}
