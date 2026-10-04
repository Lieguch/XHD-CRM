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

        //删除日志
        public async Task<int> DeleteLog(Sys_log models)
        {
            return await _irepository.DeleteAsync(models.id);
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
