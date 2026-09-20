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
    public interface ISys_ParamService : IBaseService<Sys_Param>
    {
        /// <summary>
        /// Sprint 6 Wave 1 #116：参数名唯一性校验，Service 层薄封装。
        /// 委托 Repository 执行。
        /// </summary>
        /// <param name="paramName">待校验的参数名</param>
        /// <param name="parentId">父级参数 id（params_type）</param>
        /// <param name="excludeId">排除自身的 id（新增场景传 "root" 或空字符串）</param>
        /// <returns>true=唯一可用；false=重名</returns>
        Task<bool> ValidateNameAsync(string paramName, string parentId, string excludeId);
    }
}
