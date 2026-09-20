using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;

using FreeSql;
using XHD.Core.Models;
using XHD.Core.Common;


namespace XHD.Core.IRepository
{
    public interface ISys_ParamRepository : IXHDBaseRepository<Sys_Param>
    {
        /// <summary>
        /// Sprint 6 Wave 1 #116：参数名唯一性校验。
        /// 对应 A 侧 Server.Sys_Param.validate：
        ///   params_name=@name AND params_type=@parentid AND id!=@excludeId
        /// 命中即返回 false（不通过），否则 true。
        /// </summary>
        /// <param name="paramName">待校验的参数名</param>
        /// <param name="parentId">父级参数 id（params_type）</param>
        /// <param name="excludeId">排除自身的 id（新增场景传 "root" 或空字符串）</param>
        /// <returns>true=唯一可用；false=重名</returns>
        Task<bool> ValidateNameAsync(string paramName, string parentId, string excludeId);
    }
}
