using Azure.Core;
using FreeSql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;
using MiniExcelLibs.Attributes;
using MiniExcelLibs.OpenXml;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;
using XHD.Core.Common;
using XHD.Core.Common.Excel;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.View.Configs;
using XHD.Core.View.Helpers;
using XHD.Core.View.Models.Dtos;

namespace XHD.Core.View.Controllers
{
    [Authorize]
    public class CustomerController : Controller
    {
        private readonly ICRM_CustomerService _service;
        private readonly ICRM_ContactService _contactService;
        private readonly ICRM_followService _followService;
        private readonly ISale_orderService _orderService;
        private readonly ISale_contractService _contractService;
        private readonly IDBAuthService _dBAuthService;
        private readonly ISys_ParamService _paramService;
        private readonly ISys_Param_ProvincesService _provincesService;
        private readonly ISys_logService _logService;
        private readonly ISys_infoService _infoService;
        private readonly IFreeSql _fsql;
        private readonly SysLogExt<CRM_Customer> _logExt = new();

        public CustomerController(
            ICRM_CustomerService service,
            ICRM_ContactService contactService,
            ICRM_followService followService,
            ISale_orderService orderService,
            ISale_contractService contractService,
            IDBAuthService dBAuthService,
            ISys_ParamService paramService,
            ISys_Param_ProvincesService provincesService,
            ISys_logService logService,
            ISys_infoService infoService,
            IFreeSql fsql = null)
        {
            _service = service;
            _contactService = contactService;
            _followService = followService;
            _orderService = orderService;
            _contractService = contractService;
            _dBAuthService = dBAuthService;
            _paramService = paramService;
            _provincesService = provincesService;
            _logService = logService;
            _infoService = infoService;
            _fsql = fsql;
        }

        public IActionResult Info()
        {
            return View();
        }

        public IActionResult Index()
        {
            var allParams = _paramService.Grid(p => true).data;
            var cus_industry = allParams.Where(p => p.params_type == "cus_industry").ToDictionary(p => p.id, p => p.params_name);
            var cus_type = allParams.Where(p => p.params_type == "cus_type").ToDictionary(p => p.id, p => p.params_name);
            var cus_level = allParams.Where(p => p.params_type == "cus_level").ToDictionary(p => p.id, p => p.params_name);
            var cus_source = allParams.Where(p => p.params_type == "cus_source").ToDictionary(p => p.id, p => p.params_name);

            var provinces = _provincesService.Grid(p => true).data.ToDictionary(p => p.id, p => p.Provinces);

            ViewData["cus_industry"] = cus_industry;
            ViewData["cus_type"] = cus_type;
            ViewData["cus_level"] = cus_level;
            ViewData["cus_source"] = cus_source;
            ViewData["Province"] = provinces;

            return View();
        }

        public IActionResult Add()
        {
            return View();
        }

        public IActionResult CustomerMap()
        {
            var mapKey = _infoService.Grid(i => i.sys_key == "map_key").data.FirstOrDefault()?.sys_value;
            ViewData["map_key"] = mapKey;
            return View();
        }

        /// <summary>
        /// 客户公海列表页面（state=1）。与 Index 同参数字典供前端下拉渲染使用。
        /// </summary>
        public IActionResult Pool()
        {
            FillCustomerViewData();
            return View();
        }

        /// <summary>
        /// 意向客户列表页面（state=3）。
        /// </summary>
        public IActionResult Intention()
        {
            FillCustomerViewData();
            return View();
        }

        /// <summary>
        /// 高意向客户列表页面（state=2）。
        /// </summary>
        public IActionResult HighIntention()
        {
            FillCustomerViewData();
            return View();
        }

        /// <summary>
        /// 批量设置意向状态（state 在 2 与 3 之间互切）。
        /// 用于「设为高意向」「降级意向」按钮。
        /// </summary>
        /// <param name="payload">请求体 JSON：{ ids: string[], state: int }</param>
        /// <returns>JObject { code, msg, data }</returns>
        [HttpPost]
        public async Task<string> SetIntention([FromBody] JObject payload)
        {
            var resp = new JObject();
            if (payload == null)
            {
                resp["code"] = 1; resp["msg"] = "参数为空"; resp["data"] = null;
                return resp.ToString();
            }

            var idsToken = payload["ids"] as JArray;
            if (idsToken == null || idsToken.Count == 0)
            {
                resp["code"] = 1; resp["msg"] = "参数为空"; resp["data"] = null;
                return resp.ToString();
            }
            if (payload["state"] == null || payload["state"].Type == JTokenType.Null)
            {
                resp["code"] = 1; resp["msg"] = "参数为空"; resp["data"] = null;
                return resp.ToString();
            }

            int targetState;
            if (!int.TryParse(payload["state"].ToString(), out targetState) ||
                (targetState != 2 && targetState != 3))
            {
                resp["code"] = 1; resp["msg"] = "目标状态非法"; resp["data"] = null;
                return resp.ToString();
            }

            var ids = new List<string>(idsToken.Count);
            foreach (var t in idsToken)
            {
                var s = t?.ToString();
                if (!string.IsNullOrWhiteSpace(s)) ids.Add(s);
            }
            if (ids.Count == 0)
            {
                resp["code"] = 1; resp["msg"] = "参数为空"; resp["data"] = null;
                return resp.ToString();
            }

            // 按钮权限：复用 CRM_Customer|edit
            if (!await _dBAuthService.GetAuth(GetUserId(), "CRM_Customer|edit"))
            {
                resp["code"] = 1; resp["msg"] = "无权限！"; resp["data"] = null;
                return resp.ToString();
            }

            var roledata = await _dBAuthService.GetDataAuth(GetUserId());
            if (roledata.authtype == 0)
            {
                resp["code"] = 1; resp["msg"] = "无权限！"; resp["data"] = null;
                return resp.ToString();
            }

            int updated = 0;
            foreach (var id in ids)
            {
                var c = (await _service.GridAsync(x => x.id == id, 1, 1)).data.FirstOrDefault();
                if (c == null) continue;
                if (roledata.authtype != 4 && !roledata.empList.Contains(c.emp_id))
                {
                    resp["code"] = 1; resp["msg"] = "无权限！"; resp["data"] = null;
                    return resp.ToString();
                }
                // 仅允许意向(3)↔高意向(2) 之间切换
                int current = c.state ?? 0;
                if (current != 2 && current != 3) continue;
                c.state = targetState;
                if (await _service.UpdateAppAsync(c)) updated++;
            }

            resp["code"] = 0;
            resp["msg"] = "操作成功";
            resp["data"] = updated;
            return resp.ToString();
        }

        /// <summary>
        /// 为 Index/Pool/Intention/HighIntention 页面填充参数字典 ViewData。
        /// </summary>
        private void FillCustomerViewData()
        {
            var allParams = _paramService.Grid(p => true).data;
            var cus_industry = allParams.Where(p => p.params_type == "cus_industry").ToDictionary(p => p.id, p => p.params_name);
            var cus_type = allParams.Where(p => p.params_type == "cus_type").ToDictionary(p => p.id, p => p.params_name);
            var cus_level = allParams.Where(p => p.params_type == "cus_level").ToDictionary(p => p.id, p => p.params_name);
            var cus_source = allParams.Where(p => p.params_type == "cus_source").ToDictionary(p => p.id, p => p.params_name);
            var provinces = _provincesService.Grid(p => true).data.ToDictionary(p => p.id, p => p.Provinces);

            ViewData["cus_industry"] = cus_industry;
            ViewData["cus_type"] = cus_type;
            ViewData["cus_level"] = cus_level;
            ViewData["cus_source"] = cus_source;
            ViewData["Province"] = provinces;
        }

        public IActionResult mapmark()
        {
            var mapKey = _infoService.Grid(i => i.sys_key == "map_key").data.FirstOrDefault()?.sys_value;
            ViewData["map_key"] = mapKey;
            return View();
        }

        public async Task<string> Grid(PageView<CRM_Customer> model)
        {
            var exp = await BuildCustomerQueryExpression();
            var sortText = "a.create_time desc";

            // Sprint 10.28：重复客户查询（对应 A 侧 CRM_Customer.grid.xhd?type=repeat）
            // 语义：找出未删除客户中，cus_name 出现 2+ 次的记录，按 cus_name, id DESC 排序
            var type = Request.Query["type"].FirstOrDefault() ?? "";
            if (type == "repeat")
            {
                // 在数据权限过滤范围内取回所有未删除客户的 cus_name，再 LINQ GroupBy 找重复名
                // 使用 BuildCustomerQueryExpression 已包含的数据权限 + isPrivate 过滤，与列表视图口径一致
                var dupBaseExp = exp.And(c => c.isDelete == 0
                    && c.cus_name != null && c.cus_name != "");
                var dupNameList = (await _service.GridAsync(dupBaseExp)).data;
                var nameCount = new Dictionary<string, int>();
                foreach (var cust in dupNameList)
                {
                    var nm = cust.cus_name;
                    if (nameCount.ContainsKey(nm))
                    {
                        nameCount[nm]++;
                    }
                    else
                    {
                        nameCount[nm] = 1;
                    }
                }
                var dupSet = new List<string>();
                foreach (var kv in nameCount)
                {
                    if (kv.Value >= 2)
                    {
                        dupSet.Add(kv.Key);
                    }
                }

                if (dupSet.Count == 0)
                {
                    // 无重复：直接返回空结果集（保持排序口径一致）
                    exp = exp.And(c => false);
                }
                else
                {
                    exp = exp.And(c => dupSet.Contains(c.cus_name) && c.isDelete == 0);
                }
                sortText = "cus_name desc, id desc";
            }

            var result = await _service.GridAsync(exp, model.Page, model.Limit, sortText);
            return result.ToString();
        }

        public async Task<string> GetPoint()
        {
            //获取基础查询条件
            var exp = await BuildCustomerQueryExpression(includePrivate: true);
            //追加坐标过滤条件，数据库直接过滤，不要内存Where
            exp = exp.And(c => c.x > 0 && c.y > 0);

            //直接投影，只拿需要的5个字段，不要加载完整实体
            var CustomerList = await _service.GridAsync(exp);

            var List = CustomerList.data.Select(c => new
            {
                cus_name = c.cus_name,
                cus_add = c.cus_add,
                cus_tel = c.cus_tel,
                x = c.x,
                y = c.y
            }).ToList();

            

            JArray jArray = JArray.FromObject(List);
            
            var result = XHDResult.Success(jArray);
            //直接序列化对象，避免手动构造JArray再ToString的双重开销
            return result.ToString();
        }

        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.Sid)?.Value;
        }

        private async Task<bool> CheckAuthAsync(string operation)
        {
            // 直接将常量 "CRM_Customer" 写在这里
            return await _dBAuthService.GetAuth(GetUserId(), $"CRM_Customer|{operation}");
        }

        public async Task<string> Save(CRM_Customer model)
        {
            if (string.IsNullOrWhiteSpace(model.id))
            {
                // 新增
                if (!await CheckAuthAsync("add"))
                {
                    return XHDResult.Error("无权限！").ToString();
                }

                model.id = UUIDNext.Uuid.NewSequential().ToString();
                model.sn = $"CU-{DateTime.Now:yyyyMMdd}-{model.id.Split('-')[2]}";
                model.create_id = GetUserId();
                model.create_time = DateTime.Now;
                model.isDelete = 0;

                var result = await _service.AddAsync(model);
                if (result == 0)
                {
                    return XHDResult.Error("操作失败，系统错误！").ToString();
                }

                // 审计日志：新增客户
                await _logService.UpdateLog(new Sys_log
                {
                    id = UUIDNext.Uuid.NewSequential().ToString(),
                    EventType = "[客户]新增",
                    EventID = model.id,
                    cus_id = model.id,
                    EventTitle = model.cus_name,
                    UserID = GetUserId(),
                    UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                    IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    EventDate = DateTime.Now,
                    Log_Content = $"新增客户成功，客户编号：{model.sn}，客户名称：{model.cus_name}"
                });
            }
            else
            {
                // 编辑
                if (!await CheckAuthAsync("edit"))
                {
                    return XHDResult.Error("无权限！").ToString();
                }

                var old = (await _service.GridAsync(c => c.id == model.id, 1, 1)).data.FirstOrDefault();
                if (old == null)
                {
                    return XHDResult.Error("找不到数据！").ToString();
                }

                var result = await _service.UpdateAsync(model);
                if (result == 0)
                {
                    return XHDResult.Error("操作失败，系统错误！").ToString();
                }

                var logContent = _logExt.LogContent(old, model);
                if (!string.IsNullOrEmpty(logContent))
                {
                    await _logService.UpdateLog(new Sys_log
                    {
                        id = UUIDNext.Uuid.NewSequential().ToString(),
                        EventType = "[客户]修改",   // 直接写字符串，不再使用常量
                        EventID = model.id,
                        cus_id = model.id,
                        EventTitle = model.cus_name,
                        UserID = GetUserId(),
                        UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                        IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString(),
                        EventDate = DateTime.Now,
                        Log_Content = logContent
                    });
                }
            }

            return XHDResult.Success().ToString();
        }

        public async Task<string> Excute()
        {
            var cusName = Request.Form["cus_name"].ToString();
            var currentId = Request.Form["id"].ToString();

            var exists = await _service.GridAsync(c => c.cus_name == cusName && c.id != currentId);
            if (exists.count > 0)
            {
                return XHDResult.Success().ToString();
            }
            return XHDResult.Error("").ToString();
        }

        /// <summary>
        /// 批量认领客户（[HttpPost] Ajax）
        /// 语义：state 1（公共池） → 0（正常/已分配员工），并设置 emp_id=当前登录用户
        /// 返回：JObject { code: 0|1, msg: string, data: count|null }
        /// </summary>
        /// <param name="ids">客户 ID 数组，客户端以 JSON 数组形式提交</param>
        /// <returns>JSON 字符串，供前端 jq/ajax 解析</returns>
        [HttpPost]
        public async Task<string> Claimlist([FromBody] JArray ids)
        {
            var resp = new JObject();
            if (ids == null || ids.Count == 0)
            {
                resp["code"] = 1;
                resp["msg"] = "认领失败或参数为空";
                resp["data"] = null;
                return resp.ToString();
            }

            var empId = GetUserId();
            if (string.IsNullOrWhiteSpace(empId))
            {
                resp["code"] = 1;
                resp["msg"] = "登录状态已过期，请重新登录";
                resp["data"] = null;
                return resp.ToString();
            }

            var list = new List<string>(ids.Count);
            foreach (var token in ids)
            {
                var s = token?.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s);
                }
            }

            if (list.Count == 0)
            {
                resp["code"] = 1;
                resp["msg"] = "认领失败或参数为空";
                resp["data"] = null;
                return resp.ToString();
            }

            var ok = await _service.Claimlist(list, empId);
            if (!ok)
            {
                resp["code"] = 1;
                resp["msg"] = "认领失败或参数为空";
                resp["data"] = null;
                return resp.ToString();
            }

            // 审计日志：批量认领客户
            await _logService.UpdateLog(new Sys_log
            {
                id = UUIDNext.Uuid.NewSequential().ToString(),
                EventType = "[客户]认领",
                EventID = string.Join(",", list),
                cus_id = string.Join(",", list),
                EventTitle = $"批量认领 {list.Count} 个客户",
                UserID = GetUserId(),
                UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString(),
                EventDate = DateTime.Now,
                Log_Content = $"认领成功，客户ID列表：{string.Join(",", list)}"
            });

            resp["code"] = 0;
            resp["msg"] = "认领成功";
            resp["data"] = list.Count;
            return resp.ToString();
        }

        /// <summary>
        /// 批量放弃客户（[HttpPost] Ajax）
        /// 语义：state 0（正常） → 1（回公共客户池），emp_id 保留原归属供审计
        /// 返回：JObject { code: 0|1, msg: string, data: count|null }
        /// </summary>
        /// <param name="ids">客户 ID 数组</param>
        /// <returns>JSON 字符串</returns>
        [HttpPost]
        public async Task<string> AbanDon([FromBody] JArray ids)
        {
            var resp = new JObject();
            if (ids == null || ids.Count == 0)
            {
                resp["code"] = 1;
                resp["msg"] = "放弃失败或参数为空";
                resp["data"] = null;
                return resp.ToString();
            }

            var empId = GetUserId();
            if (string.IsNullOrWhiteSpace(empId))
            {
                resp["code"] = 1;
                resp["msg"] = "登录状态已过期，请重新登录";
                resp["data"] = null;
                return resp.ToString();
            }

            var list = new List<string>(ids.Count);
            foreach (var token in ids)
            {
                var s = token?.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s);
                }
            }

            if (list.Count == 0)
            {
                resp["code"] = 1;
                resp["msg"] = "放弃失败或参数为空";
                resp["data"] = null;
                return resp.ToString();
            }

            var ok = await _service.AbanDon(list);
            if (!ok)
            {
                resp["code"] = 1;
                resp["msg"] = "放弃失败或参数为空";
                resp["data"] = null;
                return resp.ToString();
            }

            // 审计日志：批量放弃客户
            await _logService.UpdateLog(new Sys_log
            {
                id = UUIDNext.Uuid.NewSequential().ToString(),
                EventType = "[客户]放弃",
                EventID = string.Join(",", list),
                cus_id = string.Join(",", list),
                EventTitle = $"批量放弃 {list.Count} 个客户",
                UserID = GetUserId(),
                UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString(),
                EventDate = DateTime.Now,
                Log_Content = $"放弃成功，客户ID列表：{string.Join(",", list)}"
            });

            resp["code"] = 0;
            resp["msg"] = "放弃成功";
            resp["data"] = list.Count;
            return resp.ToString();
        }

        /// <summary>
        /// 公共客户池 Grid（state=1）
        /// 语义：所有已放弃或从未认领的客户；员工可在本列表点「认领」按钮把客户划归自己
        /// 数据权限已过滤：经 BuildCustomerQueryExpression 按 authtype（0/1/2/3/4）过滤，
        /// authtype=0 直接返回空；authtype=4 不追加过滤；1/2/3 按 roledata.empList 过滤
        /// </summary>
        [HttpGet("Poolgrid")]
        public async Task<string> Poolgrid(PageView<CRM_Customer> model)
        {
            var exp = await BuildCustomerQueryExpression();
            exp = exp.And(c => c.state == 1);
            var result = await _service.GridAsync(exp, model.Page, model.Limit, "a.lastfollow desc");
            return result.ToString();
        }

        /// <summary>
        /// 意向客户 Grid（state=3）
        /// 数据权限已过滤：经 BuildCustomerQueryExpression 按 authtype（0/1/2/3/4）过滤，
        /// authtype=0 直接返回空；authtype=4 不追加过滤；1/2/3 按 roledata.empList 过滤
        /// </summary>
        [HttpGet("Intentiongrid")]
        public async Task<string> Intentiongrid(PageView<CRM_Customer> model)
        {
            var exp = await BuildCustomerQueryExpression();
            exp = exp.And(c => c.state == 3);
            var result = await _service.GridAsync(exp, model.Page, model.Limit, "a.lastfollow desc");
            return result.ToString();
        }

        /// <summary>
        /// 高意向客户 Grid（state=2）
        /// 数据权限已过滤：经 BuildCustomerQueryExpression 按 authtype（0/1/2/3/4）过滤，
        /// authtype=0 直接返回空；authtype=4 不追加过滤；1/2/3 按 roledata.empList 过滤
        /// </summary>
        [HttpGet("HighIntentiongrid")]
        public async Task<string> HighIntentiongrid(PageView<CRM_Customer> model)
        {
            var exp = await BuildCustomerQueryExpression();
            exp = exp.And(c => c.state == 2);
            var result = await _service.GridAsync(exp, model.Page, model.Limit, "a.lastfollow desc");
            return result.ToString();
        }

        public async Task<string> Delete(string id)
        {
            // 检查关联数据
            if ((await _contactService.GridAsync(c => c.customer_id == id, 1, 1)).count > 0)
            {
                return XHDResult.Error("此客户下含有联系人，不能删除！").ToString();
            }
            if ((await _followService.GridAsync(f => f.customer_id == id, 1, 1)).count > 0)
            {
                return XHDResult.Error("此客户下含有跟进，不能删除！").ToString();
            }
            if ((await _orderService.GridAsync(o => o.customer_id == id, 1, 1)).count > 0)
            {
                return XHDResult.Error("此客户下含有订单，不能删除！").ToString();
            }
            if ((await _contractService.GridAsync(c => c.customer_id == id, 1, 1)).count > 0)
            {
                return XHDResult.Error("此客户下含有合同，不能删除！").ToString();
            }

            if (!await _dBAuthService.GetAuth(GetUserId(), "CRM_Customer|del"))
            {
                return XHDResult.Error("无权限！").ToString();
            }

            var customer = (await _service.GridAsync(c => c.id == id, 1, 1)).data.FirstOrDefault();
            if (customer == null)
            {
                return XHDResult.Error("找不到此数据！").ToString();
            }

            var result = await _service.DeleteAsync(id);
            if (result == 0)
            {
                return XHDResult.Error("删除失败！").ToString();
            }

            _logExt.getEntityText(customer);
            await _logService.DeleteLog(new Sys_log
            {
                id = Guid.NewGuid().ToString(),
                EventType = "[客户]删除",
                EventID = id,
                cus_id = id,
                EventTitle = customer.cus_name,
                UserID = GetUserId(),
                UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString(),
                EventDate = DateTime.Now
            });

            return XHDResult.Success().ToString();
        }

        /// <summary>
        /// Sprint 3 #12：客户总数 KPI。
        /// 对应 A 侧 Server.CRM_Customer.c_count。
        /// 所有过滤参数化，返回符合 isDelete=0 且匹配所有非空参数的客户总数。
        /// </summary>
        [HttpGet("Count")]
        public async Task<string> Count([FromQuery] CustomerCountQuery q)
        {
            Expression<Func<CRM_Customer, bool>> exp = a => a.isDelete == 0;

            if (!string.IsNullOrWhiteSpace(q.emp_id))
            {
                exp = exp.And(a => a.emp_id == q.emp_id);
            }
            if (!string.IsNullOrWhiteSpace(q.industry_val))
            {
                exp = exp.And(a => a.cus_industry_id == q.industry_val);
            }
            if (!string.IsNullOrWhiteSpace(q.cus_type_id))
            {
                exp = exp.And(a => a.cus_type_id == q.cus_type_id);
            }
            if (!string.IsNullOrWhiteSpace(q.cus_level_id))
            {
                exp = exp.And(a => a.cus_level_id == q.cus_level_id);
            }
            if (!string.IsNullOrWhiteSpace(q.cus_source_id))
            {
                exp = exp.And(a => a.cus_source_id == q.cus_source_id);
            }
            if (PageValidate.IsDateTime(q.startdate))
            {
                DateTime sd = DateTime.Parse(q.startdate);
                exp = exp.And(a => a.create_time != null && a.create_time.Value >= sd);
            }
            if (PageValidate.IsDateTime(q.enddate))
            {
                DateTime ed = DateTime.Parse(q.enddate).AddDays(1).AddTicks(-1);
                exp = exp.And(a => a.create_time != null && a.create_time.Value <= ed);
            }
            if (PageValidate.IsDateTime(q.startfollow))
            {
                DateTime sf = DateTime.Parse(q.startfollow);
                exp = exp.And(a => a.lastfollow != null && a.lastfollow.Value >= sf);
            }
            if (PageValidate.IsDateTime(q.endfollow))
            {
                DateTime ef = DateTime.Parse(q.endfollow).AddDays(1).AddTicks(-1);
                exp = exp.And(a => a.lastfollow != null && a.lastfollow.Value <= ef);
            }
            if (!string.IsNullOrWhiteSpace(q.Provinces_val))
            {
                exp = exp.And(a => a.Provinces_id == q.Provinces_val);
            }
            if (!string.IsNullOrWhiteSpace(q.City_val))
            {
                exp = exp.And(a => a.City_id == q.City_val);
            }
            if (PageValidate.IsNumber(q.isPrivate))
            {
                int isPrivate = int.Parse(q.isPrivate);
                exp = exp.And(a => a.isPrivate == isPrivate);
            }

            int total = await _service.CountAsync(exp);

            // 返回标准 XHDResult 包装：count 字段承载总数
            return XHDResult.Result(0, "", new JArray(), total).ToString();
        }

        /// <summary>
        /// Sprint 3 Wave 2 #14：客户预删除（软删）。
        /// 与 <see cref="Delete"/> 的区别：预删除不阻止关联数据存在，仅统计后写入 Sys_log 供回收站还原参考。
        /// </summary>
        /// <param name="id">客户 ID</param>
        /// <returns>标准 XHDResult 字符串</returns>
        [HttpPost("AdvanceDelete")]
        public async Task<string> AdvanceDelete(string id)
        {
            // 参数校验
            if (string.IsNullOrWhiteSpace(id))
            {
                return XHDResult.Error("系统错误，找不到数据！").ToString();
            }

            // 存在性检查（仅看未删除的记录）
            var customer = (await _service.GridAsync(c => c.id == id && c.isDelete == 0, 1, 1)).data.FirstOrDefault();
            if (customer == null)
            {
                return XHDResult.Error("系统错误，找不到数据！").ToString();
            }

            // 按钮权限校验（与 Delete 使用同一按钮键，保证业务口径一致）
            if (!await _dBAuthService.GetAuth(GetUserId(), "CRM_Customer|del"))
            {
                return XHDResult.Error("无权限！").ToString();
            }

            // 数据权限校验（预删除属于数据级操作，需检查 authtype 是否允许触及该客户的员工归属）
            var roledata = await _dBAuthService.GetDataAuth(GetUserId());
            if (roledata.authtype == 0)
            {
                return XHDResult.Error("无权限！").ToString();
            }
            if (roledata.authtype != 4 && !roledata.empList.Contains(customer.emp_id))
            {
                return XHDResult.Error("无权限！").ToString();
            }

            // 关联数据数量统计（ICRM_ContactService/ISale_orderService 未提供 CountAsync，
            // 采用 GridAsync(1,1).count 的轻量计数模式，与 Delete 保持同一模式）
            long contactCountL = (await _contactService.GridAsync(c => c.customer_id == id, 1, 1)).count;
            long followCountL = (await _followService.GridAsync(f => f.customer_id == id, 1, 1)).count;
            long orderCountL = (await _orderService.GridAsync(o => o.customer_id == id, 1, 1)).count;
            int contactCount = (int)contactCountL;
            int followCount = (int)followCountL;
            int orderCount = (int)orderCountL;

            // 执行软删
            var ok = await _service.AdvanceDeleteAsync(id, GetUserId());
            if (!ok)
            {
                return XHDResult.Error("删除失败！").ToString();
            }

            // 写 Sys_log（参照 Delete L474-487，注意用 DeleteLog 而非 UpdateLog）
            _logExt.getEntityText(customer);
            await _logService.DeleteLog(new Sys_log
            {
                id = Guid.NewGuid().ToString(),
                EventType = "[客户]预删除",
                EventID = id,
                cus_id = id,
                EventTitle = customer.cus_name,
                UserID = GetUserId(),
                UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString(),
                EventDate = DateTime.Now
            });

            // 组装返回文案：有关联数据时附带计数，便于回收站还原前判断影响面
            int totalRelated = contactCount + followCount + orderCount;
            if (totalRelated > 0)
            {
                string msg = $"此客户已放入回收站。（含 {contactCount} 个联系人、{followCount} 条跟进、{orderCount} 个订单，共 {totalRelated} 项关联数据）";
                return XHDResult.Success(msg).ToString();
            }

            return XHDResult.Success("此客户已放入回收站。").ToString();
        }

        /// <summary>
        /// Sprint 4 Wave 1b #01：客户重取（从回收站恢复）。
        /// 对应 A 侧 Server.CRM_Customer.regain（A 侧内部调 AdvanceDelete(id, 0, now)）。
        /// 与 <see cref="AdvanceDelete"/> 语义镜像、方向相反：isDelete 置 0，清空 Delete_time / Delete_id。
        /// 权限口径：复用 CRM_Customer|del 按钮权限（A 侧用按钮 GUID D2769CAF-8BC2-46D4-9758-7EE5EC4626C6，
        /// Sprint 3 落地时映射为 CRM_Customer|del，保持业务口径一致）。
        /// 数据权限：沿用 AdvanceDelete 的 authtype 判定（0 直接拒绝；非 4 且 emp_id 不在 empList 内拒绝）。
        /// 幂等安全：未预删除的客户再次调用返回成功（已处于恢复态）。
        /// </summary>
        /// <param name="id">客户 ID（GUID 格式）</param>
        /// <returns>标准 XHDResult 字符串</returns>
        [HttpPost("regain")]
        public async Task<string> Regain(string id)
        {
            // 参数校验
            if (string.IsNullOrWhiteSpace(id))
            {
                return XHDResult.Error("系统错误，找不到数据！").ToString();
            }

            if (!PageValidate.checkID(id))
            {
                return XHDResult.Error("系统错误，找不到数据！").ToString();
            }

            // 按钮权限校验（与 AdvanceDelete 使用同一按钮键）
            if (!await _dBAuthService.GetAuth(GetUserId(), "CRM_Customer|del"))
            {
                return XHDResult.Error("无权限！").ToString();
            }

            // 数据权限校验（与 AdvanceDelete 保持同一模式）
            var roledata = await _dBAuthService.GetDataAuth(GetUserId());
            if (roledata.authtype == 0)
            {
                return XHDResult.Error("无权限！").ToString();
            }

            var customer = (await _service.GridAsync(c => c.id == id, 1, 1)).data.FirstOrDefault();
            if (customer == null)
            {
                return XHDResult.Error("系统错误，找不到数据！").ToString();
            }

            if (roledata.authtype != 4 && !roledata.empList.Contains(customer.emp_id))
            {
                return XHDResult.Error("无权限！").ToString();
            }

            var ok = await _service.RegainAsync(id);
            if (!ok)
            {
                return XHDResult.Error("恢复失败！").ToString();
            }

            // 写 Sys_log（参照 AdvanceDelete L623-634，用 DeleteLog 走同一审计通道）
            _logExt.getEntityText(customer);
            await _logService.DeleteLog(new Sys_log
            {
                id = Guid.NewGuid().ToString(),
                EventType = "[客户]恢复",
                EventID = id,
                cus_id = id,
                EventTitle = customer.cus_name,
                UserID = GetUserId(),
                UserName = User.FindFirst(ClaimTypes.Name)?.Value,
                IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString(),
                EventDate = DateTime.Now
            });

            return XHDResult.Success("恢复成功").ToString();
        }

        /// <summary>
        /// Sprint 4 Wave 1b #02：移动端客户更新（简化字段子集）。
        /// 对应 A 侧 BLL.CRM_Customer.UpdateApp（A 侧 DAL/CRM_Customer.cs:745-810）。
        /// 与 PC 端 <see cref="Save"/> 的区别：仅更新 15 个业务字段
        /// （cus_name/cus_add/cus_tel/cus_fax/cus_website/cus_industry_id/Provinces_id/City_id/
        /// cus_type_id/cus_level_id/cus_source_id/DesCripe/Remarks/emp_id/isPrivate），
        /// create_time/sn/isDelete/Delete_time/Delete_id/lastfollow/state/x/y 等管理字段保持不变。
        /// 权限口径：CRM_Customer|edit；数据权限：authtype=0 直接拒绝，非 4 且归属人不在 empList 内拒绝。
        /// </summary>
        /// <param name="model">移动端提交的客户模型（id 必填）</param>
        /// <returns>标准 XHDResult 字符串</returns>
        [HttpPost("UpdateApp")]
        public async Task<string> UpdateApp([FromBody] CRM_Customer model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.id))
            {
                return XHDResult.Error("客户ID无效").ToString();
            }

            if (!PageValidate.checkID(model.id))
            {
                return XHDResult.Error("客户ID无效").ToString();
            }

            if (!await CheckAuthAsync("edit"))
            {
                return XHDResult.Error("无权限！").ToString();
            }

            // 数据权限校验：只能更新自己有权限访问的客户
            var roledata = await _dBAuthService.GetDataAuth(GetUserId());
            if (roledata.authtype == 0)
            {
                return XHDResult.Error("无权限更新该客户").ToString();
            }

            var existing = (await _service.GridAsync(c => c.id == model.id, 1, 1)).data.FirstOrDefault();
            if (existing == null)
            {
                return XHDResult.Error("找不到数据！").ToString();
            }

            if (roledata.authtype != 4 && !roledata.empList.Contains(existing.emp_id))
            {
                return XHDResult.Error("无权限更新该客户").ToString();
            }

            // 移动端强制归属为当前登录用户，避免客户端伪造 emp_id 抢占他人客户
            model.emp_id = GetUserId();

            var ok = await _service.UpdateAppAsync(model);
            if (!ok)
            {
                return XHDResult.Error("更新失败").ToString();
            }

            return XHDResult.Success("更新成功").ToString();
        }

        public async Task<ActionResult> Export()
        {
            // 原有业务逻辑完全保留
            var exp = await BuildCustomerQueryExpression(includePrivate: true);
            var customers = (await _service.GridAsync(exp, "a.create_time desc")).data;

            // 表头定义（和原代码完全一致）
            var sheetTitle = new[] {
        "编号","客户名字","地址","电话","传真",
        "网址","行业","省份","城市","类别",
        "级别","来源","描述","备注","归属",
        "公私","最后跟进","坐标","创建时间"
    };

            // 1. 构建DataTable：先固定表头列，空数据也强制保留表头
            var dt = new DataTable();
            foreach (var title in sheetTitle)
            {
                dt.Columns.Add(title);
            }

            // 2. 填充数据行，完全保留原映射逻辑
            foreach (var c in customers)
            {
                var row = dt.NewRow();
                row["编号"] = c.sn ?? "";
                row["客户名字"] = c.cus_name ?? "";
                row["地址"] = c.cus_add ?? "";
                row["电话"] = c.cus_tel ?? "";
                row["传真"] = c.cus_fax ?? "";
                row["网址"] = c.cus_website ?? "";
                row["行业"] = c.cus_industry?.params_name ?? "";
                row["省份"] = c.Provinces?.Provinces ?? "";
                row["城市"] = c.City?.City ?? "";
                row["类别"] = c.cus_type?.params_name ?? "";
                row["级别"] = c.cus_level?.params_name ?? "";
                row["来源"] = c.cus_source?.params_name ?? "";
                row["描述"] = c.DesCripe ?? "";
                row["备注"] = c.Remarks ?? "";
                row["归属"] = c.Employee?.name ?? "";
                row["公私"] = c.isPrivate == 1 ? "公客" : "私客";
                row["最后跟进"] = c.lastfollow ?? null;
                row["坐标"] = c.xy ?? "";
                row["创建时间"] = c.create_time ?? null;
                dt.Rows.Add(row);
            }

            // 3. 配置样式
            var excelConfig = new OpenXmlConfiguration
            {
                AutoFilter = true,
                TableStyles = TableStyles.Default,
                DynamicColumns = sheetTitle.Select((title, index) => new DynamicExcelColumn(title)
                {
                    Index = index,
                    Width = 15
                }).ToArray()
            };

            // 4. 写入流
            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await ms.SaveAsAsync(
                    value: dt,
                    sheetName: "客户列表",
                    excelType: ExcelType.XLSX,
                    configuration: excelConfig
                );
                fileBytes = ms.ToArray();
            }

            // 5. 返回文件
            return File(
                fileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"客户信息{DateTime.Now:yyyyMMddHHmmss}.xlsx"
            );
        }

        // ========== Sprint 4 Wave 3：Excel 导入（#04 / #06） ==========

        /// <summary>
        /// Sprint 10.24：打开客户 Excel 导入对话框（页面）。
        /// 供 SysMenus 菜单 "客户导入" 打开——独立于 <see cref="Import(IFormFile)"/> 上传端点。
        /// View 名 "Import" 对应 Views/Customer/Import.cshtml。
        /// </summary>
        [HttpGet("ImportView")]
        public IActionResult ImportView()
        {
            return View("Import");
        }

        /// <summary>
        /// Sprint 10.24：打开管理员客户覆盖导入对话框（页面）。
        /// 供 SysMenus 菜单 "管理员导入" 打开——独立于 <see cref="AdminImport(IFormFile)"/> 上传端点。
        /// View 名 "AdminImport" 对应 Views/Customer/AdminImport.cshtml。
        /// </summary>
        [HttpGet("AdminImportView")]
        public IActionResult AdminImportView()
        {
            return View("AdminImport");
        }

        /// <summary>
        /// Sprint 4 Wave 3 #04：Excel 客户导入（普通用户）。
        /// 对应 A 侧 Server.CRM_Customer.import（ext_rar2018/Server/CRM_Customer.cs:1339）。
        /// A 侧走 NPOI + XML 模板，B 侧统一用 MiniExcel 反向读取。
        /// 列名与 A 侧 Customer.xml 模板 100% 对齐，并兼容 B 侧 Export 输出的中文列名。
        /// 逐行校验必填（客户名）、CodeKey 值合法性（省份/城市/行业/类型/级别/来源/员工），
        /// 数据库去重（同名未删除客户视为冲突，跳过并计入失败）。
        /// 权限：CRM_Customer|import。
        /// 请求体：multipart/form-data，字段名 file（.xls/.xlsx 均支持）。
        /// </summary>
        /// <param name="file">Excel 文件（≤ 10 MB）</param>
        /// <returns>XHDResult JSON 字符串（含 success/update/error/message 字段）</returns>
        [HttpPost("import")]
        [RequestSizeLimit(11 * 1024 * 1024)]
        public async Task<string> Import(IFormFile file)
        {
            // 1. 权限校验
            if (!await CheckAuthAsync("import"))
            {
                return XHDResult.Error("无权限！").ToString();
            }

            // 2. 文件校验
            if (file == null || file.Length == 0)
            {
                return XHDResult.Error("请选择要导入的文件").ToString();
            }
            if (file.Length > ExcelImportHelper.MaxFileSizeBytes)
            {
                return XHDResult.Error("文件大小不能超过10MB").ToString();
            }

            var userId = GetUserId();
            var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "";

            // 3. 加载代码表（一次性查询，避免逐行打 DB）
            var tables = await ExcelImportHelper.LoadCodeTablesAsync(_fsql);

            // 4. 读取 Excel + 构建实体
            var models = new List<CRM_Customer>();
            var result = new ExcelImportResult();
            int rowNum = 0;
            try
            {
                using var stream = file.OpenReadStream();
                var rows = await ExcelImportHelper.ReadRowsAsync(stream);
                foreach (var row in rows)
                {
                    rowNum++;
                    // 跳过完全空行
                    if (IsRowEmpty(row)) continue;

                    var entity = ExcelImportHelper.BuildCustomer(row, userId, tables, rowNum, result);
                    if (entity != null) models.Add(entity);
                }
            }
            catch (Exception ex)
            {
                return XHDResult.Error($"Excel 解析失败：{ex.Message}").ToString();
            }

            // 5. 批量入库（去重 + 插入）
            var importResult = await _service.ImportAsync(models);
            // 合并两阶段的结果（构建阶段失败 + 入库阶段失败）
            MergeResult(result, importResult);

            // 6. 写日志
            await WriteImportLogAsync(userId, userName, "客户导入", file.FileName, importResult, result);

            // 7. 返回
            if (result.Error > 0 && models.Count == 0)
            {
                return XHDResult.Error(result.ToJson()).ToString();
            }
            return XHDResult.Success(result.ToJson()).ToString();
        }

        /// <summary>
        /// Sprint 4 Wave 3 #06：Excel 客户导入（管理员，覆盖模式）。
        /// 对应 A 侧 Server.CRM_Customer.adminimport（ext_rar2018/Server/CRM_Customer.cs:1379）。
        /// 与 #04 的区别：
        ///   - 权限键为 CRM_Customer|adminimport（管理员专用）
        ///   - 已存在客户按 cus_name 覆盖业务字段（保留 id / create_time / sn / isDelete 等管理字段）
        ///   - 结果区分 Success（新增）与 Update（覆盖），Error 计数不变
        /// </summary>
        /// <param name="file">Excel 文件（≤ 10 MB）</param>
        /// <returns>XHDResult JSON 字符串</returns>
        [HttpPost("adminimport")]
        [RequestSizeLimit(11 * 1024 * 1024)]
        public async Task<string> AdminImport(IFormFile file)
        {
            if (!await CheckAuthAsync("adminimport"))
            {
                return XHDResult.Error("无权限！").ToString();
            }

            if (file == null || file.Length == 0)
            {
                return XHDResult.Error("请选择要导入的文件").ToString();
            }
            if (file.Length > ExcelImportHelper.MaxFileSizeBytes)
            {
                return XHDResult.Error("文件大小不能超过10MB").ToString();
            }

            var userId = GetUserId();
            var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "";

            var tables = await ExcelImportHelper.LoadCodeTablesAsync(_fsql);

            var models = new List<CRM_Customer>();
            var result = new ExcelImportResult();
            int rowNum = 0;
            try
            {
                using var stream = file.OpenReadStream();
                var rows = await ExcelImportHelper.ReadRowsAsync(stream);
                foreach (var row in rows)
                {
                    rowNum++;
                    if (IsRowEmpty(row)) continue;

                    var entity = ExcelImportHelper.BuildCustomer(row, userId, tables, rowNum, result);
                    if (entity != null) models.Add(entity);
                }
            }
            catch (Exception ex)
            {
                return XHDResult.Error($"Excel 解析失败：{ex.Message}").ToString();
            }

            var importResult = await _service.AdminImportAsync(models);
            MergeResult(result, importResult);

            await WriteImportLogAsync(userId, userName, "客户覆盖导入", file.FileName, importResult, result);

            return XHDResult.Success(result.ToJson()).ToString();
        }

        /// <summary>
        /// 合并两段导入结果（构建阶段的失败 + 入库阶段的失败）。
        /// </summary>
        private static void MergeResult(ExcelImportResult baseR, ExcelImportResult extra)
        {
            if (extra == null) return;
            baseR.Success += extra.Success;
            baseR.Update += extra.Update;
            baseR.Error += extra.Error;
            if (!string.IsNullOrWhiteSpace(extra.Message))
            {
                baseR.Message = string.IsNullOrWhiteSpace(baseR.Message)
                    ? extra.Message
                    : baseR.Message + "；" + extra.Message;
            }
        }

        /// <summary>
        /// 判断 Excel 一行是否完全为空（所有单元格都是 null 或空字符串）。
        /// </summary>
        private static bool IsRowEmpty(Dictionary<string, object>? row)
        {
            if (row == null || row.Count == 0) return true;
            foreach (var v in row.Values)
            {
                if (v != null && !string.IsNullOrWhiteSpace(v.ToString()))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 写 Excel 导入 Sys_log 日志（复用 Sprint 2 已落地的 DeleteLog 通道，事件类型区分）。
        /// </summary>
        private async Task WriteImportLogAsync(string userId, string userName, string eventType, string fileName, ExcelImportResult importResult, ExcelImportResult combined)
        {
            try
            {
                var log = new Sys_log
                {
                    id = UUIDNext.Uuid.NewSequential().ToString(),
                    EventType = $"Excel{eventType}",
                    EventTitle = $"【{userName}】{eventType}",
                    EventID = userId,
                    UserID = userId,
                    UserName = userName,
                    IPStreet = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                    EventDate = DateTime.Now,
                    Log_Content = $"文件：{fileName}；{combined.ToJson()}"
                };
                await _logService.DeleteLog(log);
            }
            catch
            {
                // 日志写失败不应阻塞主流程
            }
        }

        // ========== 私有辅助方法 ==========

        /// <summary>
        /// 构建客户查询的通用表达式（含过滤条件和数据权限）
        /// </summary>
        /// <param name="includePrivate">是否包含私客（true表示全部，false表示只取公客且受权限控制）</param>
        private async Task<Expression<Func<CRM_Customer, bool>>> BuildCustomerQueryExpression(bool includePrivate = false)
        {
            Expression<Func<CRM_Customer, bool>> exp = c => true;

            if (Request.Query["type"] == "bath")
            {
                if (!string.IsNullOrWhiteSpace(Request.Query["old_emp_id"]))
                {
                    exp = exp.And(c => c.emp_id == Request.Query["old_emp_id"]);
                }
                else
                {
                    exp = c => false;
                }
            }

            // 查询参数
            if (!string.IsNullOrWhiteSpace(Request.Query["cus_name"]))
            {
                exp = exp.And(c => c.cus_name.Contains(Request.Query["cus_name"]));
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["keyword"]))
            {
                exp = exp.And(c => c.cus_name.Contains(Request.Query["keyword"]));
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["id"]))
            {
                exp = exp.And(c => c.id == Request.Query["id"]);
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["cus_tel"]))
            {
                exp = exp.And(c => c.cus_tel.Contains(Request.Query["cus_tel"]));
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["cus_industry_id"]))
            {
                exp = exp.And(c => c.cus_industry_id == Request.Query["cus_industry_id"]);
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["cus_type_id"]))
            {
                exp = exp.And(c => c.cus_type_id == Request.Query["cus_type_id"]);
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["cus_level_id"]))
            {
                exp = exp.And(c => c.cus_level_id == Request.Query["cus_level_id"]);
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["cus_source_id"]))
            {
                exp = exp.And(c => c.cus_source_id == Request.Query["cus_source_id"]);
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["Provinces_id"]))
            {
                exp = exp.And(c => c.Provinces_id == Request.Query["Provinces_id"]);
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["City_id"]))
            {
                exp = exp.And(c => c.City_id == Request.Query["City_id"]);
            }
            if (!string.IsNullOrWhiteSpace(Request.Query["emp_id"]))
            {
                exp = exp.And(c => c.emp_id == Request.Query["emp_id"]);
            }
            if (PageValidate.IsDateTime(Request.Query["date1"]))
            {
                exp = exp.And(c => c.create_time >= DateTime.Parse(Request.Query["date1"]));
            }
            if (PageValidate.IsDateTime(Request.Query["date2"]))
            {
                exp = exp.And(c => c.create_time <= DateTime.Parse(Request.Query["date2"]));
            }

            // ========== 数据权限过滤（Auth） ==========
            // authtype 语义（DBAuthRepository.GetDataAuth 返回值）：
            //   0 = 无权限（empList 为空，返回空结果集）
            //   1 = 本人（empList = [当前用户]）
            //   2 = 本部门（empList = 同部门员工）
            //   3 = 本部及下级（empList = 本部门 + 下级部门员工）
            //   4 = 全公司（不追加过滤，看全部）
            // 参照 A 侧 CRM_Customer.Auth()（ext_rar2018\Server\CRM_Customer.cs L1414-1431）
            var roledata = await _dBAuthService.GetDataAuth(GetUserId());
            var isFullAccess = roledata.authtype == 4;

            // 无权限（authtype=0）直接返回空结果，避免后续 isPrivate 分支绕过
            if (roledata.authtype == 0)
            {
                return c => false;
            }

            if (!includePrivate)
            {
                // 表格查询：根据 isPrivate 参数决定
                if (PageValidate.IsNumber(Request.Query["isPrivate"]))
                {
                    var isPrivate = int.Parse(Request.Query["isPrivate"]);
                    if (isPrivate == 1)
                    {
                        // 仅看公客：公客本身可见，不再叠加员工归属过滤
                        exp = exp.And(c => c.isPrivate == 1);
                    }
                    else
                    {
                        // 仅看私客：叠加数据权限过滤（本人/本部/本部及下级）
                        exp = exp.And(c => c.isPrivate == 0);
                        if (!isFullAccess)
                        {
                            exp = exp.And(c => roledata.empList.Contains(c.emp_id));
                        }
                    }
                }
                else
                {
                    // 未指定公私：全公司权限看全部；其他权限看自己员工归属的私客 + 全部公客
                    if (!isFullAccess)
                    {
                        exp = exp.And(c => roledata.empList.Contains(c.emp_id) || c.isPrivate == 1);
                    }
                }
            }
            else
            {
                // 地图/导出：只应用普通数据权限（不加 isPrivate 过滤）
                if (!isFullAccess)
                {
                    exp = exp.And(c => roledata.empList.Contains(c.emp_id));
                }
            }

            return exp;
        }

        // ========== Sprint 10.27b 客户回收站 ==========

        /// <summary>
        /// Sprint 10.27b：客户回收站页面入口。
        /// 展示已软删除（isDelete=1）的客户列表，供管理员恢复或删除前最后确认。
        /// 对应 A 侧 View/Toolbar/Recycle/CRM/Customer.aspx。
        /// View 名 "Recycle" 对应 Views/Customer/Recycle.cshtml。
        /// </summary>
        [HttpGet("Recycle")]
        public IActionResult RecycleView()
        {
            return View("Recycle");
        }

        // ========== Sprint 10.28 重复客户查询 ==========

        /// <summary>
        /// Sprint 10.28：重复客户查询页面入口。
        /// 展示 cus_name 出现 2+ 次（未删除）的客户列表。
        /// 对应 A 侧 View/Toolbar/Repeat.aspx。
        /// View 名 "Repeat" 对应 Views/Customer/Repeat.cshtml。
        /// </summary>
        [HttpGet("Repeat")]
        public IActionResult RepeatView()
        {
            return View("Repeat");
        }

        /// <summary>
        /// Sprint 10.27b：客户回收站 Grid。
        /// 语义：仅显示 isDelete=1 的已软删除客户；按 Delete_time DESC 排序（最近删除优先）。
        /// 权限：无按钮级校验（列表查看），恢复由 <see cref="Regain"/> 做权限校验。
        /// 参数：支持 cus_name / cus_tel / keyword 搜索过滤；数据权限同 <see cref="BuildCustomerQueryExpression"/>。
        /// </summary>
        /// <param name="model">分页参数</param>
        /// <returns>XHDData&lt;CRM_Customer&gt; JSON 字符串</returns>
        [HttpGet("RecycleGrid")]
        public async Task<string> RecycleGrid(PageView<CRM_Customer> model)
        {
            var exp = await BuildCustomerQueryExpression();
            exp = exp.And(c => c.isDelete == 1);
            var result = await _service.GridAsync(exp, model.Page, model.Limit, "a.Delete_time desc");

            // 补充 Deleter 导航属性（Repository 默认不 join Deleter）
            if (_fsql != null && result.data != null)
            {
                var deleteIds = result.data
                    .Where(c => !string.IsNullOrEmpty(c.Delete_id))
                    .Select(c => c.Delete_id)
                    .Distinct()
                    .ToList();
                if (deleteIds.Count > 0)
                {
                    var deleters = await _fsql.Select<hr_employee>()
                        .Where(e => deleteIds.Contains(e.id))
                        .ToListAsync(true);
                    var deleterMap = deleters.ToDictionary(e => e.id, e => e);
                    foreach (var c in result.data)
                    {
                        if (!string.IsNullOrEmpty(c.Delete_id) && deleterMap.TryGetValue(c.Delete_id, out var d))
                        {
                            c.Deleter = d;
                        }
                    }
                }
            }

            return result.ToString();
        }

    }
}