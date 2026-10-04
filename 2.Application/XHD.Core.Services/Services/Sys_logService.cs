using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;
using System.Threading.Tasks;

using XHD.Core.IServices;
using XHD.Core.IRepository;
using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

namespace XHD.Core.Services
{
internal class Sys_logService : BaseService<Sys_log>, ISys_logService
    {
        private ISys_logRepository irepository;
        public Sys_logService(ISys_logRepository repository)
        {
            _irepository = repository;
            irepository = repository;
        }


        public async Task<JObject> LogType()
        {
            var result = await irepository.Logtype();

            var json = JsonConvert.SerializeObject(result, new IsoDateTimeConverter { DateTimeFormat = "yyyy-MM-dd HH:mm:ss" });
            JArray arr = JArray.Parse(json);

            return XHDResult.Result(0, "", arr);
        }

        //修改日志（INSERT 语义）
        //根因修复：所有 ~26 个调用点都是先 new Sys_log { id = UUIDNext... } 再调用本方法，
        //语义是「新增一条修改日志」。原实现走 UpdateAsync（按主键 UPDATE），新 id 在库里不存在
        // => 0 行受影响 => 全站修改日志从未落库。改为 AddAsync 真正插入。
        public async Task<int> UpdateLog(Sys_log models)
        {
           return await _irepository.AddAsync(models);
        }

        //删除日志（INSERT 语义）
        //根因修复：与 UpdateLog 同类。全部 26 个调用点（CRMFollowController:234、
        //CRM_ContactController:267/479、CustomerController:696/848/924/1282、
        //FinanceInvoiceController:229、FinanceReceiveController:222、Finance_ReceivableController:267、
        //HrDepartmentController:236、HrEmployeeController:315、HrPositionController:209、
        //MessageNewsController:219、ParamsCityController:121、ParamsProvinceController:233、
        //ProductController:197、ProductCategoryController:226、SaleOrderController:305、
        //SaleContractController:246、SysParamController:321、SysRoleController:133）都是先
        // new Sys_log { id = UUIDNext.Uuid.NewSequential()/Guid.NewGuid()... } 再调用本方法，
        //语义是「新增一条删除事件日志」（A 版 Syslog.Add_log = log.Add(modellog)，INSERT）。
        //原实现走 DeleteAsync(models.id)（按主键 DELETE 一个刚 new 出来、库里不存在的 id）
        // => 0 行受影响 => 全站删除日志从未落库。改为 AddAsync 真正插入。
        public async Task<int> DeleteLog(Sys_log models)
        {
            return await _irepository.AddAsync(models);
        }

        //登录日志
        public async Task<int> LoginLog(string emp_id, string emp_name, string ip)
        {
            Sys_log models = new Sys_log();

            models.id = System.Guid.NewGuid().ToString();
            models.EventType = "用户登录";
            models.EventID = emp_id;
            models.EventTitle = emp_name;
            models.UserID = emp_id;
            models.UserName = emp_name;
            models.IPStreet = ip;
            models.EventDate = DateTime.Now;

            return await _irepository.AddAsync(models);
        }
    }
}
