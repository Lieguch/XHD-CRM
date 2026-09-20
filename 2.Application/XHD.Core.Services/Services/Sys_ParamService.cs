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
    internal class Sys_ParamService : BaseService<Sys_Param>, ISys_ParamService
    {
        // BaseService._irepository 是基类接口引用，拿不到 ValidateNameAsync，额外持有具体接口引用。
        private readonly ISys_ParamRepository _irepositoryBase;

        public Sys_ParamService(ISys_ParamRepository repository)
        {
            _irepository = repository;
            _irepositoryBase = repository;
        }

        /// <summary>
        /// Sprint 6 Wave 1 #116：参数名唯一性校验，Service 层薄封装。
        /// </summary>
        public async Task<bool> ValidateNameAsync(string paramName, string parentId, string excludeId)
        {
            return await _irepositoryBase.ValidateNameAsync(paramName, parentId, excludeId);
        }
    }
}
