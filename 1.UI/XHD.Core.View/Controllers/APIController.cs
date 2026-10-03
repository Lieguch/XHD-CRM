
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Configuration;
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using XHD.Core.Common;
using XHD.Core.Common.DEncrypt;
using XHD.Core.IRepository;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.View.Configs;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class APIController : Controller
    {
        private hr_employee employee = null;
        private readonly Ihr_employeeService _empservice;
        private readonly ICRM_CustomerService _customerservice;
        private readonly ICRM_followService _followservice;
        private readonly ISale_orderService _OrderService;
        private readonly ISale_order_detailsService _OrderDetailsService;
        private readonly ICRM_ContactService _contactservice;
        private readonly ISale_contractService _contractservice;
        private readonly ISale_contract_attaService _contractattaservice;
        private readonly IFinance_ReceiveService _ReceiveService;
        private readonly ISys_ParamService _SysParamService;
        private readonly IProductService _productservice;
        private readonly IDBAuthService _dBAuthService;
        private readonly ISys_logService _LogService;
        private readonly IFreeSql _fsql;

        public APIController(Ihr_employeeService empservice, ICRM_CustomerService customerservice, IDBAuthService dBAuthService, ICRM_followService followservice, ISale_orderService orderService, ISale_contractService contractservice, IFinance_ReceiveService receiveService, ICRM_ContactService contactservice, ISys_ParamService sysParamService, ISys_logService logService, IProductService productservice, ISale_order_detailsService orderDetailsService, ISale_contract_attaService contractattaservice, IFreeSql fsql)
        {
            _empservice = empservice;
            _customerservice = customerservice;
            _dBAuthService = dBAuthService;
            _followservice = followservice;
            _OrderService = orderService;
            _contractservice = contractservice;
            _ReceiveService = receiveService;
            _contactservice = contactservice;
            _SysParamService = sysParamService;
            _LogService = logService;
            _productservice = productservice;
            _OrderDetailsService = orderDetailsService;
            _contractattaservice = contractattaservice;
            _fsql = fsql;
        }

        #region 登录认证
        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="uid">手机号</param>
        /// <param name="pwd">密码</param>
        /// <returns></returns>
        [HttpGet]
        public async Task<string> Login(string uid, string? pwd)
        {
            // [Sprint 10.38 P1-7] 移动端协议：客户端发送 MD5(pwd).ToUpper()，服务端拿到的就是规范密钥。
            // 校验改在内存做（PBKDF2 无法下推 SQL），存量无盐 MD5 记录由 Verify 兼容并透明升级。
            string secret = (pwd ?? string.Empty).ToUpperInvariant();

            Expression<Func<hr_employee, bool>> expwhere = a => a.uid == uid;

            if (uid != "admin")
            {
                expwhere = expwhere.And(a => a.status == 1);
            }

            var list = await _empservice.GridAsync(expwhere);

            if (list.count > 0)
            {
                var l = list.data[0];

                if (!PasswordHasher.Verify(l.pwd, secret))
                {
                    return XHDResult.Error("账号密码不匹配！").ToString();
                }

                // 透明升级存量无盐 MD5 密码为 PBKDF2
                if (PasswordHasher.NeedsRehash(l.pwd))
                {
                    string upgraded = PasswordHasher.Hash(secret);
                    await _empservice.UpdateAsync(
                        a => new hr_employee { pwd = upgraded },
                        a => a.id == l.id);
                }

                string id = l.id;
                var outtime = DateTime.Now.AddMonths(1).ToString("yyyy-MM-dd hh:mm:ss");

                string tokentext = $"{id},{outtime}";
                string encrypttoken = DESEncrypt.Encrypt(tokentext);

                //返回Token
                JObject obj = new JObject();
                obj.Add("token", encrypttoken);
                obj.Add("id", id);
                obj.Add("outtime", outtime);
                obj.Add("RealName", l.name);
                //obj.Add("password", l.pwd);
                obj.Add("phone", l.tel);

                return XHDResult.Success(obj).ToString();

            }
            return XHDResult.Error("账号密码不匹配！").ToString();
        }

        /// <summary>
        /// check token
        /// </summary>
        /// <returns></returns>
        public JObject checkToken()
        {
            // 获取所有请求头
            var allHeaders = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

            // 获取特定请求头（例如Authorization）
            if (Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
            {
                var authValue = (string)authorizationHeader;

                if (string.IsNullOrWhiteSpace(authValue))
                {
                    return XHDResult.Error(-9, "Token错误！");
                }

                var _token = authValue.Replace("Bearer ", "").Replace(" ", "");

                string encrypttoken = PageValidate.InputText(_token, 150);

                string decrypttoken = DESEncrypt.Decrypt(encrypttoken);

                string[] tokenitems = decrypttoken.Split(',');

                string id = "";
                string tokentime = "";

                if (tokenitems.Length >= 2)
                {
                    id = tokenitems[0];
                    tokentime = tokenitems[1];
                }

                DateTime limittime = DateTime.Parse(tokentime);

                if (limittime <= DateTime.Now)
                {
                    return XHDResult.Error(-9, "身份验证过期，请重新登录！");
                }


                Expression<Func<hr_employee, bool>> expwhere = a => a.id == id;

                var list = _empservice.Grid(expwhere);

                if (list.count == 0)
                {
                    return XHDResult.Error(-9, "找不到此用户！");
                }

                employee = list.data[0];

                return XHDResult.Success();

            }

            // 获取用户代理
            var userAgent = Request.Headers.UserAgent;

            return XHDResult.Error(-9, "认证失败！");


        }

        public async Task<string> ModifyPWD(string oldpwd, string pwd)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            // 校验原密码（移动端此接口按明文发送，服务端求规范密钥——沿用 A 版原始契约）
            var empData = await _empservice.GridAsync(a => a.id == employee.id);
            var emp = empData.data.FirstOrDefault();
            string oldSecret = PasswordHasher.CanonicalSecret(oldpwd);
            if (emp == null || !PasswordHasher.Verify(emp.pwd, oldSecret))
            {
                return XHDResult.Error("原密码不正确").ToString();
            }

            Expression<Func<hr_employee, hr_employee>> exppwd = a => new hr_employee { pwd = PasswordHasher.Hash(PasswordHasher.CanonicalSecret(pwd)) };
            Expression<Func<hr_employee, bool>> expwhere = a => a.id == employee.id;
            var result = await _empservice.UpdateAsync(exppwd, expwhere);
            if (result <= 0)
            {
                return XHDResult.Error("修改失败").ToString();
            }
            return XHDResult.Success("修改成功").ToString();
        }

        #endregion

        #region 客户
        /// <summary>
        /// 
        /// </summary>
        /// <param name="serchtxt"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public async Task<string> CustomerList(string? serchtxt, int page, int limit)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            //查询数据
            Expression<Func<CRM_Customer, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(serchtxt))
            {
                exp = exp.And(a => a.cus_name.Contains(serchtxt) || a.cus_tel.Contains(serchtxt));
            }


            //权限

            var roledata = await _dBAuthService.GetDataAuth(employee.id);

            if (roledata.authtype != 4)
            {
                exp = exp.And(a => roledata.empList.Contains(a.emp_id) || a.isPrivate == 1);
            }



            var result = await _customerservice.GridAsync(exp, page, limit, "a.create_time desc");

            return result.ToString();

        }

        public async Task<string> CustomerSave([FromBody] CRM_Customer model)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            var result = 0;

            if (string.IsNullOrWhiteSpace(model.id))
            {
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                var ids = model.id.Split("-");
                var sn = $"{DateTime.Now.ToString("yyyyMMddHHmmss")}{ids[2]}";

                model.sn = sn;
                model.isDelete = 0;
                model.create_id = employee.id;
                model.create_time = DateTime.Now;

                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "CRM_Customer|add");

                if (authbtn)
                {
                    result = await _customerservice.AddAsync(model);
                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }
            else
            {
                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "CRM_Customer|edit");

                if (authbtn)
                {
                    //日志
                    Expression<Func<CRM_Customer, bool>> exp = a => a.id == model.id;
                    var checknulldata = await _customerservice.GridAsync(exp, 1, 1);

                    if (checknulldata.count == 0)
                    {
                        return XHDResult.Error("找不到数据！").ToString();
                    }

                    result = await _customerservice.UpdateAsync(model);

                    //对比实体差别

                    SysLogExt<CRM_Customer> logext = new SysLogExt<CRM_Customer>();

                    var content = logext.LogContent(checknulldata.data[0], model);

                    if (content.Length > 0)
                    {
                        //添加修改日志
                        Sys_log logmodels = new Sys_log();

                        logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                        logmodels.EventType = "[客户]修改";
                        logmodels.EventID = model.id;
                        //logmodels.EventTitle = model.id;
                        logmodels.UserID = employee.id;
                        logmodels.UserName = employee.name;
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

        #endregion

        #region 联系人

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serchtxt"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public async Task<string> ContactList(string? serchtxt, string? customer_id, int page = 1, int limit = 10)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            //查询数据
            Expression<Func<CRM_Contact, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(serchtxt))
            {
                exp = exp.And(a => a.C_name.Contains(serchtxt));
            }

            if (!string.IsNullOrWhiteSpace(customer_id))
            {
                exp = exp.And(a => a.customer_id == customer_id);
            }

            //权限

            var roledata = await _dBAuthService.GetDataAuth(employee.id);

            if (roledata.authtype != 4)
            {
                exp = exp.And(a => roledata.empList.Contains(a.create_id));
            }

            var result = await _contactservice.GridAsync(exp, page, limit, "a.create_time desc");

            return result.ToString();

        }

        public async Task<string> ContactSave([FromBody] CRM_Contact model)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            var result = 0;

            if (string.IsNullOrWhiteSpace(model.id))
            {
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.create_id = employee.id;
                model.create_time = DateTime.Now;

                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "crm_contact|add");

                if (authbtn)
                {
                    result = await _contactservice.AddAsync(model);
                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }
            else
            {
                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "crm_contact|edit");

                if (authbtn)
                {
                    //日志
                    Expression<Func<CRM_Contact, bool>> exp = a => a.id == model.id;
                    var checknulldata = await _contactservice.GridAsync(exp, 1, 1);

                    if (checknulldata.count == 0)
                    {
                        return XHDResult.Error("找不到数据！").ToString();
                    }

                    result = await _contactservice.UpdateAsync(model);

                    //对比实体差别

                    SysLogExt<CRM_Contact> logext = new SysLogExt<CRM_Contact>();

                    var content = logext.LogContent(checknulldata.data[0], model);

                    if (content.Length > 0)
                    {
                        //添加修改日志
                        Sys_log logmodels = new Sys_log();

                        logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                        logmodels.EventType = "[联系人]修改";
                        logmodels.EventID = model.id;
                        logmodels.EventTitle = model.C_name;
                        logmodels.UserID = employee.id;
                        logmodels.UserName = employee.name;
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
        #endregion

        #region 跟进
        /// <summary>
        /// 
        /// </summary>
        /// <param name="serchtxt"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public async Task<string> FollowList(string? serchtxt, string? customer_id, int page, int limit)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            //查询数据
            Expression<Func<CRM_follow, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(serchtxt))
            {
                exp = exp.And(a => a.customer.cus_name.Contains(serchtxt));
            }

            if (!string.IsNullOrWhiteSpace(customer_id))
            {
                exp = exp.And(a => a.customer_id == customer_id);
            }


            //权限

            var roledata = await _dBAuthService.GetDataAuth(employee.id);

            if (roledata.authtype != 4)
            {
                exp = exp.And(a => roledata.empList.Contains(a.employee_id));
            }



            var result = await _followservice.GridAsync(exp, page, limit, "a.Follow_time desc");

            return result.ToString();

        }

        public async Task<string> FollowSave([FromBody] CRM_follow model)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            var result = 0;

            if (string.IsNullOrWhiteSpace(model.id))
            {
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.employee_id = employee.id;
                model.follow_time = DateTime.Now;

                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "CRM_Follow|add");

                if (authbtn)
                {
                    result = await _followservice.AddAsync(model);

                    await _customerservice.LastFollow(model.customer_id);

                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }
            else
            {
                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "CRM_Follow|edit");

                if (authbtn)
                {
                    //日志
                    Expression<Func<CRM_follow, bool>> exp = a => a.id == model.id;
                    var checknulldata = await _followservice.GridAsync(exp, 1, 1);

                    if (checknulldata.count == 0)
                    {
                        return XHDResult.Error("找不到数据！").ToString();
                    }

                    result = await _followservice.UpdateAsync(model);

                    //对比实体差别

                    SysLogExt<CRM_follow> logext = new SysLogExt<CRM_follow>();

                    var content = logext.LogContent(checknulldata.data[0], model);

                    if (content.Length > 0)
                    {
                        //添加修改日志
                        Sys_log logmodels = new Sys_log();

                        logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                        logmodels.EventType = "[跟进]修改";
                        logmodels.EventID = model.id;
                        //logmodels.EventTitle = model.id;
                        logmodels.UserID = employee.id;
                        logmodels.UserName = employee.name;
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

        #endregion

        #region 订单
        /// <summary>
        /// 
        /// </summary>
        /// <param name="serchtxt"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public async Task<string> OrderList(string? serchtxt, string? customer_id, int page, int limit)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            //查询数据
            Expression<Func<Sale_order, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(serchtxt))
            {
                exp = exp.And(a => a.customer.cus_name.Contains(serchtxt));
            }

            if (!string.IsNullOrWhiteSpace(customer_id))
            {
                exp = exp.And(a => a.customer_id == customer_id);
            }


            //权限

            var roledata = await _dBAuthService.GetDataAuth(employee.id);

            if (roledata.authtype != 4)
            {
                exp = exp.And(a => roledata.empList.Contains(a.create_id));
            }



            var result = await _OrderService.GridAsync(exp, page, limit, "a.create_time desc");

            // APP 按 A 版 Model 契约读取编号字段（Serialnumber），B 实体字段名为 sn —— 在 API 边界补别名
            return ProjectGrid(result, o => WithSerialnumber(SerializeItem(o), o.sn)).ToString();
        }

        /// <summary>
        /// 订单详情（单条）。移动端约定 res.data 为对象本身。
        /// </summary>
        /// <param name="id">订单 id</param>
        public async Task<string> OrderInfo(string id)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            if (string.IsNullOrWhiteSpace(id) || !PageValidate.checkID(id))
            {
                return XHDResult.Error("参数错误").ToString();
            }

            Expression<Func<Sale_order, bool>> exp = a => a.id == id;

            var result = await _OrderService.GridAsync(exp, 1, 1);

            if (result.count == 0 || result.data == null || result.data.Count == 0)
            {
                return XHDResult.Error("数据不存在").ToString();
            }

            return SingleResult(WithSerialnumber(SerializeItem(result.data[0]), result.data[0].sn)).ToString();
        }

        public async Task<string> OrderDetails(string order_id)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            Expression<Func<Sale_order_details, bool>> exp = a => a.order_id == order_id;

            var result = await _OrderDetailsService.GridAsync(exp);

            return result.ToString();
        }

        public async Task<string> OrderSave([FromBody] Sale_order model)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            var result = 0;

            var details = model.Order_details;

            model.Order_details = "";


            if (model.Order_amount < 0)
            {
                model.Order_amount = 0;
            }

            if (model.discount_amount < 0)
            {
                model.discount_amount = 0;
            }

            model.total_amount = model.Order_amount - model.discount_amount;

            if (model.total_amount < 0)
            {
                model.total_amount = 0;
            }


            if (string.IsNullOrWhiteSpace(model.id))
            {
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.create_id = employee.id;
                model.create_time = DateTime.Now;

                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "Sale_Menu_order|add");

                if (authbtn)
                {
                    result = await _OrderService.AddAsync(model);
                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }
            else
            {
                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "Sale_Menu_order|edit");

                if (authbtn)
                {
                    //日志
                    Expression<Func<Sale_order, bool>> exp = a => a.id == model.id;
                    var checknulldata = await _OrderService.GridAsync(exp, 1, 1);

                    if (checknulldata.count == 0)
                    {
                        return XHDResult.Error("找不到数据！").ToString();
                    }

                    result = await _OrderService.UpdateAsync(model);

                    //对比实体差别

                    SysLogExt<Sale_order> logext = new SysLogExt<Sale_order>();

                    var content = logext.LogContent(checknulldata.data[0], model);

                    if (content.Length > 0)
                    {
                        //添加修改日志
                        Sys_log logmodels = new Sys_log();

                        logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                        logmodels.EventType = "[订单]修改";
                        logmodels.EventID = model.id;
                        logmodels.EventTitle = model.sn;
                        logmodels.UserID = employee.id;
                        logmodels.UserName = employee.name;
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

            //先删除详情
            Expression<Func<Sale_order_details, bool>> expdetails = a => a.order_id == model.id;
            await _OrderDetailsService.DeleteAsync(expdetails);

            JArray arr = JArray.Parse(details);

            Sale_order_details modelsdetail = new Sale_order_details();
            modelsdetail.order_id = model.id;

            foreach (JObject item in arr)
            {
                modelsdetail.product_id = item.Value<string>("product_id");
                modelsdetail.price = item.Value<decimal>("price");
                modelsdetail.quantity = item.Value<int>("quantity");
                modelsdetail.amount = item.Value<decimal>("amount");

                await _OrderDetailsService.AddAsync(modelsdetail);
            }

            return XHDResult.Success().ToString();
        }

        #endregion

        #region 合同

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serchtxt"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public async Task<string> ContractList(string? serchtxt, string? customer_id, int page, int limit)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            //查询数据
            Expression<Func<Sale_contract, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(serchtxt))
            {
                exp = exp.And(a => a.customer.cus_name.Contains(serchtxt));
            }

            if (!string.IsNullOrWhiteSpace(customer_id))
            {
                exp = exp.And(a => a.customer_id == customer_id);
            }

            //权限
            var roledata = await _dBAuthService.GetDataAuth(employee.id);

            if (roledata.authtype != 4)
            {
                exp = exp.And(a => roledata.empList.Contains(a.create_id));
            }

            var result = await _contractservice.GridAsync(exp, page, limit, "a.create_time desc");

            // APP 按 A 版 Model 契约读取编号字段（Serialnumber），B 实体字段名为 sn —— 在 API 边界补别名
            return ProjectGrid(result, o => WithSerialnumber(SerializeItem(o), o.sn)).ToString();
        }

        /// <summary>
        /// 合同详情（单条）。移动端约定 res.data 为对象本身。
        /// </summary>
        /// <param name="id">合同 id</param>
        public async Task<string> ContractInfo(string id)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            if (string.IsNullOrWhiteSpace(id) || !PageValidate.checkID(id))
            {
                return XHDResult.Error("参数错误").ToString();
            }

            Expression<Func<Sale_contract, bool>> exp = a => a.id == id;

            var result = await _contractservice.GridAsync(exp, 1, 1);

            if (result.count == 0 || result.data == null || result.data.Count == 0)
            {
                return XHDResult.Error("数据不存在").ToString();
            }

            return SingleResult(WithSerialnumber(SerializeItem(result.data[0]), result.data[0].sn)).ToString();
        }

        public async Task<string> ContractAtta(string contract_id)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            Expression<Func<Sale_contract_atta, bool>> exp = a => a.contract_id == contract_id;

            var result = await _contractattaservice.GridAsync(exp);

            return result.ToString();
        }

        public async Task<string> ContractSave([FromBody] Sale_contract model)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            var result = 0;

            if (string.IsNullOrWhiteSpace(model.id))
            {
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.Our_Contractor_id = employee.id;
                model.create_time = DateTime.Now;

                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "Sale_Contract|add");

                if (authbtn)
                {
                    result = await _contractservice.AddAsync(model);

                    await _customerservice.LastFollow(model.customer_id);

                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }
            else
            {
                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "Sale_Contract|edit");

                if (authbtn)
                {
                    //日志
                    Expression<Func<Sale_contract, bool>> exp = a => a.id == model.id;
                    var checknulldata = await _contractservice.GridAsync(exp, 1, 1);

                    if (checknulldata.count == 0)
                    {
                        return XHDResult.Error("找不到数据！").ToString();
                    }

                    result = await _contractservice.UpdateAsync(model);

                    //对比实体差别

                    SysLogExt<Sale_contract> logext = new SysLogExt<Sale_contract>();

                    var content = logext.LogContent(checknulldata.data[0], model);

                    if (content.Length > 0)
                    {
                        //添加修改日志
                        Sys_log logmodels = new Sys_log();

                        logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                        logmodels.EventType = "[合同]修改";
                        logmodels.EventID = model.id;
                        //logmodels.EventTitle = model.id;
                        logmodels.UserID = employee.id;
                        logmodels.UserName = employee.name;
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

        #endregion

        #region 收款

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serchtxt"></param>
        /// <param name="page"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public async Task<string> ReceiveList(string? serchtxt, string? customer_id, int page, int limit)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            //查询数据
            Expression<Func<Finance_Receive, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(serchtxt))
            {
                exp = exp.And(a => a.Order.customer.cus_name.Contains(serchtxt));
            }

            if (!string.IsNullOrWhiteSpace(customer_id))
            {
                exp = exp.And(a => a.Order.customer.id == customer_id);
            }

            //权限
            var roledata = await _dBAuthService.GetDataAuth(employee.id);

            if (roledata.authtype != 4)
            {
                exp = exp.And(a => roledata.empList.Contains(a.create_id));
            }



            var result = await _ReceiveService.GridAsync(exp, page, limit, "a.create_time desc");

            return result.ToString();

        }

        public async Task<string> ReceiveSave([FromBody] Finance_Receive model)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            var result = 0;

            if (string.IsNullOrWhiteSpace(model.id))
            {
                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.create_id = employee.id;
                model.create_time = DateTime.Now;

                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "Finance_Receive|add");

                if (authbtn)
                {
                    result = await _ReceiveService.AddAsync(model);
                }
                else
                {
                    return XHDResult.Error("无权限！").ToString();
                }
            }
            else
            {
                //权限
                var authbtn = await _dBAuthService.GetAuth(employee.id, "Finance_Receive|edit");

                if (authbtn)
                {
                    //日志
                    Expression<Func<Finance_Receive, bool>> exp = a => a.id == model.id;
                    var checknulldata = await _ReceiveService.GridAsync(exp, 1, 1);

                    if (checknulldata.count == 0)
                    {
                        return XHDResult.Error("找不到数据！").ToString();
                    }

                    result = await _ReceiveService.UpdateAsync(model);

                    //对比实体差别

                    SysLogExt<Finance_Receive> logext = new SysLogExt<Finance_Receive>();

                    var content = logext.LogContent(checknulldata.data[0], model);

                    if (content.Length > 0)
                    {
                        //添加修改日志
                        Sys_log logmodels = new Sys_log();

                        logmodels.id = UUIDNext.Uuid.NewSequential().ToString();
                        logmodels.EventType = "[收款]修改";
                        logmodels.EventID = model.id;
                        //logmodels.EventTitle = model.id;
                        logmodels.UserID = employee.id;
                        logmodels.UserName = employee.name;
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


        #endregion

        #region 参数、产品

        public async Task<string> CountData()
        {
            await Task.CompletedTask; // Sprint 10.30: 保留 async 签名；主体为同步 FreeSql Count
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            //权限
            // A 版口径：客户=负责人(emp_id)、跟进=跟进人(employee_id)、订单=业务员(emp_id)、合同=我方签约人(Our_Contractor_id)。
            // A 版 CRM_contract 无 emp_id 字段（DAL/CRM_contract.cs:37），员工维度只能是 Our_Contractor_id；
            // B 版同口径见 SaleContractController.Grid (Our_Contractor_id) 与 HrEmployeeController 删除前合同检查。
            _fsql.Select<CRM_Customer>().Where(a => a.emp_id == employee.id).Count(out var cuscount).Page(1, 1);
            _fsql.Select<CRM_follow>().Where(a => a.employee_id == employee.id).Count(out var followcount).Page(1, 1);
            _fsql.Select<Sale_order>().Where(a => a.emp_id == employee.id).Count(out var ordercount).Page(1, 1);
            _fsql.Select<Sale_contract>().Where(a => a.Our_Contractor_id == employee.id).Count(out var contractcount).Page(1, 1);
            //_fsql.Select<Finance_Receive>().Where(a => 1 == 1).Count(out var receivecount).Page(1, 1);

            JObject obj=new JObject();

            obj.Add("cuscount", cuscount);
            obj.Add("followcount", followcount);
            obj.Add("ordercount", ordercount);
            obj.Add("contractcount", contractcount);

            return XHDResult.Success(obj).ToString();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public async Task<string> paramsCombo(string type)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            Expression<Func<Sys_Param, bool>> exp = a => a.params_type == type;

            var result = await _SysParamService.GridAsync(exp, "params_order");

            return result.ToString();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public async Task<string> ProductList(string? serchtxt, int page = 1, int limit = 10)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            //查询数据
            Expression<Func<Product, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(serchtxt))
            {
                exp = exp.And(a => a.product_name.Contains(serchtxt));
            }

            var result = await _productservice.GridAsync(exp, page, limit, "a.create_time desc");

            return result.ToString();
        }

        public async Task<string> EmployeeList(string? serchtxt, int page = 1, int limit = 10)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            // 查询数据
            Expression<Func<hr_employee, bool>> exp = a => 1 == 1;

            if (!string.IsNullOrWhiteSpace(serchtxt))
            {
                exp = exp.And(a => a.name.Contains(serchtxt));
            }

            //权限
            var roledata = await _dBAuthService.GetDataAuth(employee.id);

            if (roledata.authtype != 4)
            {
                exp = exp.And(a => roledata.empList.Contains(a.id));
            }

            var result = await _empservice.GridAsync(exp, "a.create_time desc");

            return result.ToString();
        }

        #endregion

        #region 收款详情（移动端）

        /// <summary>
        /// 收款详情（单条）。移动端约定 res.data 为对象本身。
        /// Finance_Receive 实体字段与 APP（A 版 Model）契约一致，无需别名。
        /// </summary>
        /// <param name="id">收款单 id</param>
        public async Task<string> ReceiveInfo(string id)
        {
            //身份验证
            var userresult = checkToken();

            if (userresult.Value<int>("code") != 0)
            {
                return userresult.ToString();
            }

            if (string.IsNullOrWhiteSpace(id) || !PageValidate.checkID(id))
            {
                return XHDResult.Error("参数错误").ToString();
            }

            Expression<Func<Finance_Receive, bool>> exp = a => a.id == id;

            var result = await _ReceiveService.GridAsync(exp, 1, 1);

            if (result.count == 0 || result.data == null || result.data.Count == 0)
            {
                return XHDResult.Error("数据不存在").ToString();
            }

            return SingleResult(SerializeItem(result.data[0])).ToString();
        }


        #endregion

        #region 移动端字段适配（API 边界别名）

        /// <summary>
        /// APP 按 A 版 Model 契约读取编号（Serialnumber），B 实体字段名为 sn。
        /// 仅在此 API 边界补别名，Web 前端与实体本身不受影响。
        /// </summary>
        private static JObject WithSerialnumber(JObject obj, string sn)
        {
            if (obj != null)
            {
                obj["Serialnumber"] = sn ?? string.Empty;
            }
            return obj;
        }

        /// <summary>
        /// 实体序列化为 JObject，日期格式与 XHDData.ToString 一致（yyyy-MM-dd HH:mm:ss）。
        /// </summary>
        private static JObject SerializeItem<T>(T item)
            => JObject.Parse(JsonConvert.SerializeObject(item, new IsoDateTimeConverter { DateTimeFormat = "yyyy-MM-dd HH:mm:ss" }));

        /// <summary>
        /// 单条结果：{ code, msg, data: 对象本身, count: 1 }。移动端详情页约定 res.data 即单对象。
        /// </summary>
        private static JObject SingleResult(JObject data)
        {
            JObject obj = new JObject();
            obj.Add("code", 0);
            obj.Add("msg", "");
            obj.Add("data", data);
            obj.Add("count", 1);
            obj.Add("rettime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            return obj;
        }

        /// <summary>
        /// 分页结果逐行投影（保持 XHDData 的 code/msg/data/count 结构，只替换行形状）。
        /// </summary>
        private static XHDData<JObject> ProjectGrid<T>(XHDData<T> grid, Func<T, JObject> map)
        {
            return new XHDData<JObject>
            {
                code = grid.code,
                msg = grid.msg,
                count = grid.count,
                data = grid.data != null ? grid.data.Select(map).ToList() : new List<JObject>()
            };
        }

        #endregion
    }
}
