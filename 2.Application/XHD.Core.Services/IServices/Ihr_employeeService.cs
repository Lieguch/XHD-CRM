using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using XHD.Core.Models;
using XHD.Core.Common;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq.Expressions;

namespace XHD.Core.IServices
{
    public interface Ihr_employeeService:IBaseService<hr_employee>
    {
        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        Task<JObject> Login(hr_employee model);

        /// <summary>
        /// Sprint 4 Wave 1b #10：员工唯一性校验计数，委托 Repository 执行。
        /// </summary>
        /// <param name="expWhere">完整过滤表达式（含 field==value 与可选的 id 排除）</param>
        /// <returns>符合条件的员工数量</returns>
        Task<int> ExistsAsync(Expression<Func<hr_employee, bool>> expWhere);

        /// <summary>
        /// Sprint 4 Wave 1b #11：读取员工默认城市，委托 Repository 执行。
        /// </summary>
        /// <param name="empId">员工 ID</param>
        /// <returns>默认城市；员工不存在返回 null，城市为空返回空字符串</returns>
        Task<string> GetDefaultCityAsync(string empId);

        /// <summary>
        /// Sprint 4 Wave 1b #12：更新员工默认城市，委托 Repository 执行。
        /// </summary>
        /// <param name="empId">员工 ID</param>
        /// <param name="city">目标城市值</param>
        /// <returns>是否成功</returns>
        Task<bool> UpdateDefaultCityAsync(string empId, string city);

        /// <summary>
        /// Sprint 5 Wave 1 #27：变更员工岗位三元组（dep_id / post_id / position_id）。
        /// </summary>
        /// <param name="empId">员工 ID</param>
        /// <param name="depId">目标部门 ID</param>
        /// <param name="postId">目标岗位 ID</param>
        /// <param name="positionId">目标职务级别 ID</param>
        /// <returns>是否成功</returns>
        Task<bool> UpdatePostAsync(string empId, string depId, string postId, string positionId);
    }
}
