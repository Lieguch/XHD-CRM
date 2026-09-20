using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

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
    internal class hr_employeeService : BaseService<hr_employee>, Ihr_employeeService
    {
        // BaseService 的 _irepository 字段声明为 IXHDBaseRepository&lt;hr_employee&gt;，
        // 拿不到 ExistsAsync/GetDefaultCityAsync/UpdateDefaultCityAsync 三个扩展方法，
        // 因此额外持有一个具体接口引用（与 CRM_CustomerService._irepositoryBase 同一模式）。
        private readonly Ihr_employeeRepository _irepositoryBase;

        public hr_employeeService(Ihr_employeeRepository repository)
        {
            _irepository = repository;
            _irepositoryBase = repository;
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="Page"></param>
        /// <param name="Limit"></param>
        /// <param name="expOrder"></param>
        /// <returns></returns>
        public new async Task<XHDData<hr_employee>> Grid(Expression<Func<hr_employee, bool>> expWhere, int Page, int Limit, string OrderBy)
        {
            if (string.IsNullOrWhiteSpace(OrderBy))
            {
                return await Grid(expWhere, Page, Limit);
            }

            expWhere = expWhere.And(a => a.uid != "admin");

            var result = await _irepository.GridAsync(expWhere, Page, Limit, OrderBy);

            var data = new XHDData<hr_employee>()
            {
                data = result.data,
                count = result.count
            };

            return data;
        }

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <param name="Page"></param>
        /// <param name="Limit"></param>
        /// <param name="expOrder"></param>
        /// <returns></returns>
        public new async Task<XHDData<hr_employee>> Grid(Expression<Func<hr_employee, bool>> expWhere, int Page, int Limit)
        {
            expWhere = expWhere.And(a => a.uid != "admin");

            var result = await _irepository.GridAsync(expWhere, Page, Limit);

            var data = new XHDData<hr_employee>()
            {
                data = result.data,
                count = result.count
            };

            return data;
        }

        /// <summary>
        /// 普通条件查询
        /// </summary>
        /// <param name="expWhere"></param>
        /// <returns></returns>
        public new async Task<XHDData<hr_employee>> Grid(Expression<Func<hr_employee, bool>> expWhere)
        {
            expWhere = expWhere.And(a => a.uid != "admin");

            var result = await _irepository.GridAsync(expWhere);

            var data = new XHDData<hr_employee>()
            {
                data = result,
                count = result.Count
            };

            return data;
        }

        /// <summary>
        /// 普通条件查询带排序
        /// </summary>
        /// <param name="expWhere"></param>
        /// <returns></returns>
        public new async Task<XHDData<hr_employee>> Grid(Expression<Func<hr_employee, bool>> expWhere, string OrderBy)
        {
            expWhere = expWhere.And(a => a.uid != "admin");

            var result = await _irepository.GridAsync(expWhere, OrderBy);

            var data = new XHDData<hr_employee>()
            {
                data = result,
                count = result.Count
            };

            return data;
        }

        public async Task<JObject> Login(hr_employee model)
        {
            Expression<Func<hr_employee, bool>> expression = a => a.uid == model.uid && a.pwd == Common.DEncrypt.MD5Comm.MD5Hash(model.pwd);
            //Expression<Func<hr_employee, bool>> expression = a => a.uid == model.uid && a.pwd.ToLower() == model.pwd.ToLower();

            var result = await _irepository.GridAsync(expression);

            if (result.Count == 0)
            {
                return XHDResult.Error("用户名或密码错误！");
            }

            if (result[0].canlogin == 0 && model.id != "admin")
            {
                return XHDResult.Error("此用户限制登录！");
            }

            var json = JsonConvert.SerializeObject(result[0], new IsoDateTimeConverter { DateTimeFormat = "yyyy-MM-dd HH:mm:ss" });
            JObject obj = JObject.Parse(json);

            return XHDResult.Success(obj);
        }

        /// <summary>
        /// Sprint 4 Wave 1b #10：员工唯一性校验计数，service 层薄封装，委托 Repository 执行。
        /// </summary>
        /// <param name="expWhere">完整过滤表达式（含 field==value 与可选的 id 排除）</param>
        /// <returns>符合条件的员工数量</returns>
        public async Task<int> ExistsAsync(Expression<Func<hr_employee, bool>> expWhere)
        {
            return await _irepositoryBase.ExistsAsync(expWhere);
        }

        /// <summary>
        /// Sprint 4 Wave 1b #11：读取员工默认城市，service 层薄封装，委托 Repository 执行。
        /// </summary>
        /// <param name="empId">员工 ID</param>
        /// <returns>默认城市；员工不存在返回 null，城市为空返回空字符串</returns>
        public async Task<string> GetDefaultCityAsync(string empId)
        {
            if (string.IsNullOrWhiteSpace(empId))
            {
                return null;
            }

            return await _irepositoryBase.GetDefaultCityAsync(empId);
        }

        /// <summary>
        /// Sprint 4 Wave 1b #12：更新员工默认城市，service 层薄封装，委托 Repository 执行。
        /// </summary>
        /// <param name="empId">员工 ID</param>
        /// <param name="city">目标城市值</param>
        /// <returns>是否成功</returns>
        public async Task<bool> UpdateDefaultCityAsync(string empId, string city)
        {
            if (string.IsNullOrWhiteSpace(empId) || string.IsNullOrWhiteSpace(city))
            {
                return false;
            }

            return await _irepositoryBase.UpdateDefaultCityAsync(empId, city);
        }
    }
}
